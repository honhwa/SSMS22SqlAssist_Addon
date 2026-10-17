using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlAssist.Connection;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;
using SqlAssist.Schema;
using SqlAssist.Snippets;

namespace SqlAssist.Editor
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SqlAssist completion")]
    [ContentType(SqlContentType.Name)]
    [Order(Before = "default")]
    internal sealed class SqlCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new SqlCompletionSource());
    }

    internal static class SqlContentType
    {
        /// <summary>The SSMS 22 T-SQL query editor's content type.</summary>
        public const string Name = "SQL";
    }

    /// <summary>Keys for data carried on completion items.</summary>
    internal static class ItemProps
    {
        public const string Description = "SqlAssist.Description";
        public const string Snippet = "SqlAssist.Snippet";
        /// <summary>Set on T-SQL keyword items so the list can draw them in the editor's keyword colour.</summary>
        public const string IsKeyword = "SqlAssist.IsKeyword";
    }

    internal sealed class SqlCompletionSource : IAsyncCompletionSource
    {
        private static readonly TimeSpan SchemaWait = TimeSpan.FromSeconds(3);

        // Captured on the UI thread in InitializeCompletion; GetCompletionContextAsync runs on a background thread.
        private ConnectionTarget _target;

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            try
            {
                var window = TextWindow.Around(triggerLocation.Snapshot, triggerLocation.Position);
                var ctx = SqlContextAnalyzer.Analyze(window.Text, window.Caret);
                if (ctx.InStringOrComment) return CompletionStartData.DoesNotParticipateInCompletion;

                bool explicitInvoke = trigger.Reason == CompletionTriggerReason.Invoke || trigger.Reason == CompletionTriggerReason.InvokeAndCommitIfUnique;
                if (!explicitInvoke)
                {
                    if (ctx.IsAliasPosition) return CompletionStartData.DoesNotParticipateInCompletion;
                    switch (trigger.Reason)
                    {
                        case CompletionTriggerReason.Insertion:
                            char ch = trigger.Character;
                            bool starts =
                                (ch == '.' && ctx.Kind == CaretContextKind.Qualified) ||
                                (ch == ' ' && (ctx.Kind == CaretContextKind.TableName || ctx.Kind == CaretContextKind.JoinCondition)) ||
                                ((char.IsLetter(ch) || ch == '_' || ch == '#' || ch == '[') && ctx.TypedText.Length > 0);
                            if (!starts) return CompletionStartData.DoesNotParticipateInCompletion;
                            break;
                        case CompletionTriggerReason.Deletion:
                            if (ctx.TypedText.Length == 0) return CompletionStartData.DoesNotParticipateInCompletion;
                            break;
                        default:
                            return CompletionStartData.DoesNotParticipateInCompletion;
                    }
                }

                // Called on the UI thread, which is where SSMS's connection objects must be read.
                if (ThreadHelper.CheckAccess()) _target = ActiveConnection.TryGet();
                var span = new SnapshotSpan(triggerLocation.Snapshot, window.Offset + ctx.WordStart, ctx.WordEnd - ctx.WordStart);
                return new CompletionStartData(CompletionParticipation.ProvidesItems, span);
            }
            catch (Exception ex)
            {
                Log.Error("InitializeCompletion", ex);
                return CompletionStartData.DoesNotParticipateInCompletion;
            }
        }

        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger,
            SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            try
            {
                var target = _target;
                var window = TextWindow.Around(triggerLocation.Snapshot, triggerLocation.Position);
                var ctx = SqlContextAnalyzer.Analyze(window.Text, window.Caret);
                var schema = await SchemaCache.Instance.GetAsync(target, SchemaWait, token).ConfigureAwait(false);

                if (schema.Objects.Count > 0 && ctx.TablesInScope.Any(t => t.HasColumns && schema.Find(t) == null))
                    SchemaCache.Instance.RefreshIfOlderThanThrottle(target);

                var items = new List<CompletionItem>();
                switch (ctx.Kind)
                {
                    case CaretContextKind.TableName: AddTableItems(items, ctx, schema); break;
                    case CaretContextKind.Qualified: AddQualifiedItems(items, ctx, schema); break;
                    case CaretContextKind.JoinCondition:
                        AddJoinConditionItems(items, ctx, schema);
                        AddGeneralItems(items, ctx, schema); // anything else the user wants to type after ON
                        break;
                    default: AddGeneralItems(items, ctx, schema); break;
                }
                return new CompletionContext(items.ToImmutableArray());
            }
            catch (Exception ex)
            {
                Log.Error("GetCompletionContextAsync", ex);
                return CompletionContext.Empty;
            }
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token) =>
            Task.FromResult<object>(item.Properties.TryGetProperty(ItemProps.Description, out string d) ? d : null);

        // Sort-text prefixes order the groups when nothing (or a tie) decides between them.
        // "JOIN t x ON |": the predicates, ahead of the column list (group "0").
        private void AddJoinConditionItems(List<CompletionItem> items, CaretContext ctx, SchemaModel schema)
        {
            int order = 0;
            foreach (var c in JoinConditionSuggester.Suggest(schema, ctx.JoinTarget, ctx.JoinPartners, ctx.JoinConditionSoFar))
            {
                string description = c.ForeignKeyName != null ? "Foreign key " + c.ForeignKeyName : "Columns with the same name";
                // The sort key keeps the suggester's order (foreign keys, then id-like names) instead of alphabetical.
                var item = new CompletionItem(c.Text, this, Icons.Join, ImmutableArray<CompletionFilter>.Empty,
                    c.ForeignKeyName != null ? "FK" : "", c.Text, "0" + order++.ToString("D4"), c.Text, ImmutableArray<ImageElement>.Empty);
                item.Properties.AddProperty(ItemProps.Description, description);
                items.Add(item);
            }
        }

        private void AddTableItems(List<CompletionItem> items, CaretContext ctx, SchemaModel schema)
        {
            if (ctx.IsJoin)
            {
                foreach (var j in JoinSuggester.Suggest(schema, ctx.SiblingTables, ctx.AliasesInStatement))
                    items.Add(Item(j.Text, Icons.Join, j.Text, "0", j.Target.Name, j.ForeignKeyName, "Join on " + j.ForeignKeyName));
            }
            foreach (var o in schema.Objects)
                items.Add(ObjectItem(o, SchemaModel.DisplayName(o), ctx));
            foreach (var s in schema.Schemas)
                items.Add(Item(s, Icons.Schema, SqlIdentifier.QuoteIfNeeded(s), "2", s, "schema", null));
        }

        private void AddQualifiedItems(List<CompletionItem> items, CaretContext ctx, SchemaModel schema)
        {
            if (ctx.QualifierIsSchema)
            {
                foreach (var o in schema.Objects.Where(o => string.Equals(o.Schema, ctx.Qualifier, StringComparison.OrdinalIgnoreCase)))
                    items.Add(ObjectItem(o, SqlIdentifier.QuoteIfNeeded(o.Name), ctx));
                return;
            }

            DbObject table = null;
            var r = ctx.TablesInScope.FirstOrDefault(t => t.HasColumns && t.Answers(ctx.Qualifier) &&
                (ctx.QualifierSchema == null || string.Equals(t.Schema, ctx.QualifierSchema, StringComparison.OrdinalIgnoreCase)));
            if (r != null) table = schema.Find(r);
            if (table == null) table = schema.Find(ctx.QualifierSchema, ctx.Qualifier);
            if (table != null)
            {
                foreach (var c in table.Columns) items.Add(ColumnItem(c, SqlIdentifier.QuoteIfNeeded(c.Name), table));
                return;
            }
            // "SELECT ... WHERE dbo." — a schema outside table position.
            foreach (var o in schema.Objects.Where(o => string.Equals(o.Schema, ctx.Qualifier, StringComparison.OrdinalIgnoreCase)))
                items.Add(ObjectItem(o, SqlIdentifier.QuoteIfNeeded(o.Name), ctx));
        }

        private void AddGeneralItems(List<CompletionItem> items, CaretContext ctx, SchemaModel schema)
        {
            var tables = ctx.TablesInScope
                .Select(r => (r, o: schema.Find(r)))
                .Where(x => x.o != null)
                .ToList();
            var ambiguous = new HashSet<string>(
                tables.SelectMany(x => x.o.Columns.Select(c => c.Name)).GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key),
                StringComparer.OrdinalIgnoreCase);

            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (r, o) in tables)
            {
                foreach (var c in o.Columns)
                {
                    // A column that exists in two tables only runs when qualified, so qualify it.
                    string insert = ambiguous.Contains(c.Name)
                        ? SqlIdentifier.QuoteIfNeeded(r.RefName) + "." + SqlIdentifier.QuoteIfNeeded(c.Name)
                        : SqlIdentifier.QuoteIfNeeded(c.Name);
                    if (added.Add(insert)) items.Add(ColumnItem(c, insert, o, suffix: r.RefName));
                }
            }

            foreach (var k in SqlKeywords.CompletionKeywords)
            {
                var item = Item(k, Icons.Keyword, k, "3", k.Replace(" ", ""), null, null);
                item.Properties.AddProperty(ItemProps.IsKeyword, true);
                items.Add(item);
            }

            foreach (var s in SnippetProvider.Current.All)
            {
                var item = Item(s.Key, Icons.Snippet, s.Key, "4", s.Key, s.Description, s.Body.Replace("$end$", ""));
                item.Properties.AddProperty(ItemProps.Snippet, s);
                items.Add(item);
            }
        }

        private CompletionItem ObjectItem(DbObject o, string name, CaretContext ctx)
        {
            string insert = name;
            if (ctx.AllowAlias) insert += " " + AliasGenerator.Generate(o.Name, ctx.AliasesInStatement);
            string description = $"{o.Schema}.{o.Name} ({(o.IsView ? "view" : "table")}, {o.Columns.Count} columns)";
            return Item(o.Name, o.IsView ? Icons.View : Icons.Table, insert, "1", o.Name,
                string.Equals(o.Schema, SchemaModel.DefaultSchema, StringComparison.OrdinalIgnoreCase) ? null : o.Schema, description);
        }

        private CompletionItem ColumnItem(DbColumn c, string insert, DbObject owner, string suffix = null) =>
            Item(c.Name, Icons.Column, insert, "1", c.Name, suffix ?? c.TypeName, $"{owner.Schema}.{owner.Name}.{c.Name}  {c.TypeName}");

        private CompletionItem Item(string display, ImageElement icon, string insert, string group, string filter, string suffix, string description)
        {
            var item = new CompletionItem(display, this, icon, ImmutableArray<CompletionFilter>.Empty, suffix ?? "",
                insert, group + display, filter, ImmutableArray<ImageElement>.Empty);
            if (description != null) item.Properties.AddProperty(ItemProps.Description, description);
            return item;
        }
    }
}
