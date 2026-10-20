using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text.Adornments;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;

namespace SqlAssist.Editor
{
    /// <summary>A completion item that is a node of the table tree ("SELECT |" with no FROM) or of the column picker
    /// ("SELECT | FROM t x"): a table, or one of its columns.</summary>
    internal sealed class TreeNode
    {
        public TreeNode(DbObject table, DbColumn column = null, string refName = null) { Table = table; Column = column; RefName = refName; }
        public DbObject Table { get; }
        /// <summary>Null for the table node itself.</summary>
        public DbColumn Column { get; }
        /// <summary>Column picker: the alias (or name) the query uses for the table, so one table joined twice is two nodes.</summary>
        public string RefName { get; }
        public bool IsTable => Column == null;
        public string TableNodeKey => (RefName != null ? RefName + "=" : "") + Table;
        public string Key => Column == null ? TableNodeKey : TableNodeKey + "|" + Column.Name;
    }

    /// <summary>Per completion session: which tables are expanded, what is ticked (in tick order) and the column items
    /// made for expanded tables. Lives in the session's property bag, shared by the item manager (which lists children
    /// under expanded tables), the presenter (expander/checkbox clicks), the command filter (←/→, Shift+Space) and the
    /// commit manager (which turns the ticks into a query).</summary>
    internal sealed class TreeState
    {
        private readonly Dictionary<string, List<CompletionItem>> _children = new Dictionary<string, List<CompletionItem>>();
        private readonly List<string> _checkedOrder = new List<string>();
        private readonly Dictionary<string, TreeNode> _checked = new Dictionary<string, TreeNode>();

        public SchemaModel Schema { get; set; } = SchemaModel.Empty;
        public IReadOnlyCollection<string> TakenAliases { get; set; } = new string[0];
        public HashSet<string> Expanded { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The item to select after the list is rebuilt by an expand/collapse.</summary>
        public CompletionItem SelectAfterUpdate { get; set; }

        public static TreeState For(IAsyncCompletionSession session) =>
            session.Properties.GetOrCreateSingletonProperty(() => new TreeState());

        public static TreeNode NodeOf(CompletionItem item) =>
            item != null && item.Properties.TryGetProperty(typeof(TreeNode), out TreeNode node) ? node : null;

        public bool HasChecks => _checkedOrder.Count > 0;
        public bool IsChecked(TreeNode node) => _checked.ContainsKey(node.Key);

        public void ToggleCheck(TreeNode node)
        {
            if (_checked.Remove(node.Key)) _checkedOrder.Remove(node.Key);
            else { _checked[node.Key] = node; _checkedOrder.Add(node.Key); }
        }

        public void ToggleExpanded(TreeNode table)
        {
            if (!Expanded.Remove(table.Key)) Expanded.Add(table.Key);
        }

        /// <summary>The table item a column item sits under.</summary>
        public CompletionItem ParentItem(TreeNode column) =>
            _parents.TryGetValue(column.TableNodeKey, out var item) ? item : null;

        /// <summary>Column picker mode ("SELECT | FROM t x"): each tick is written into the select list straight away.</summary>
        public bool IsPicker { get; set; }
        /// <summary>Picker: write "alias.Column" (more than one table, or an aliased one) rather than the bare name.</summary>
        public bool Qualify { get; set; }
        /// <summary>Picker: the text written so far, kept up to date as ticks change.</summary>
        private ITrackingSpan _pickerSpan;

        /// <summary>Ticks or unticks a node; in picker mode also rewrites the picked columns in the editor.</summary>
        public static void Toggle(IAsyncCompletionSession session, ITextView view, CompletionItem item, TreeNode node)
        {
            var tree = For(session);
            tree.ToggleCheck(node);
            if (tree.IsPicker) tree.WritePicks(session, view);
            Refresh(session, view, item);
        }

        /// <summary>Picker: the filter the user typed after the picked columns (the session's span also covers them).</summary>
        public string TypedAfterPicks(IAsyncCompletionSession session, ITextSnapshot snapshot) =>
            TypedSpanAfterPicks(session, snapshot).GetText();

        /// <summary>The part of the session's span after the picked columns: what a keyword or snippet commit replaces.</summary>
        public SnapshotSpan TypedSpanAfterPicks(IAsyncCompletionSession session, ITextSnapshot snapshot)
        {
            var applicable = session.ApplicableToSpan.GetSpan(snapshot);
            if (_pickerSpan == null) return applicable;
            int from = Math.Max(applicable.Start.Position, Math.Min(_pickerSpan.GetEndPoint(snapshot).Position, applicable.End.Position));
            return new SnapshotSpan(snapshot, from, applicable.End.Position - from);
        }

        /// <summary>Picker commit: with ticks, the list is already written — just drop any filter text typed after it.
        /// Without, the committed column (or a table's columns) is written as one pick. Returns false when not a picker.</summary>
        public bool CommitPicker(IAsyncCompletionSession session, ITextView view, TreeNode committed)
        {
            if (!IsPicker) return false;
            if (!HasChecks) ToggleCheck(committed);
            WritePicks(session, view);
            return true;
        }

        // Replaces the picked text (or, the first time, the word being typed) up to the caret with the ticked columns
        // in tick order, a ticked table standing for all of its columns.
        private void WritePicks(IAsyncCompletionSession session, ITextView view)
        {
            var buffer = view.TextBuffer;
            var snapshot = buffer.CurrentSnapshot;
            int start = (_pickerSpan?.GetStartPoint(snapshot) ?? session.ApplicableToSpan.GetStartPoint(snapshot)).Position;
            int end = Math.Max(start, session.ApplicableToSpan.GetEndPoint(snapshot).Position);

            var line = snapshot.GetLineFromPosition(start);
            string linePrefix = snapshot.GetText(line.Start, start - line.Start);
            string newLine = line.GetLineBreakText();
            if (string.IsNullOrEmpty(newLine)) newLine = view.Options.GetOptionValue(DefaultOptions.NewLineCharacterOptionId);

            var picks = new List<(string refName, string column)>();
            foreach (var node in _checkedOrder.Select(k => _checked[k]))
            {
                if (node.IsTable) picks.AddRange(node.Table.Columns.Select(c => (node.RefName, c.Name)));
                else picks.Add((node.RefName, node.Column.Name));
            }
            string text = SelectBuilder.ColumnList(picks.Distinct(), Qualify, linePrefix, newLine);

            var after = buffer.Replace(new Span(start, end - start), text);
            _pickerSpan = after.CreateTrackingSpan(start, text.Length, SpanTrackingMode.EdgeExclusive);
            view.Caret.MoveTo(new SnapshotPoint(after, start + text.Length));
        }

        /// <summary>Re-runs the item manager (to show/hide columns, or redraw ticks) and selects <paramref name="select"/> after.</summary>
        public static void Refresh(IAsyncCompletionSession session, ITextView view, CompletionItem select)
        {
            For(session).SelectAfterUpdate = select;
            var caret = view.Caret.Position.BufferPosition;
            session.OpenOrUpdate(new CompletionTrigger(CompletionTriggerReason.FilterChange, caret.Snapshot), caret, CancellationToken.None);
        }

        private readonly Dictionary<string, CompletionItem> _parents = new Dictionary<string, CompletionItem>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The column items shown under an expanded table, made once per table and session.</summary>
        public List<CompletionItem> ChildrenOf(CompletionItem tableItem, TreeNode table)
        {
            string key = table.TableNodeKey;
            _parents[key] = tableItem;
            if (_children.TryGetValue(key, out var list)) return list;
            list = new List<CompletionItem>();
            foreach (var c in table.Table.Columns)
            {
                var item = new CompletionItem(c.Name, tableItem.Source, Icons.Column, ImmutableArray<CompletionFilter>.Empty,
                    (c.TypeName ?? "").ToUpperInvariant(), c.Name, tableItem.SortText + "|" + c.Name, c.Name, ImmutableArray<ImageElement>.Empty);
                item.Properties.AddProperty(typeof(TreeNode), new TreeNode(table.Table, c, table.RefName));
                item.Properties.AddProperty(ItemProps.Description, ColumnInfo.Describe(Schema, table.Table, c));
                list.Add(item);
            }
            return _children[key] = list;
        }

        /// <summary>What a commit inserts: the ticked tables/columns (tables in tick order), or else the committed node.</summary>
        public IReadOnlyList<PickedTable> Picks(TreeNode committed)
        {
            var nodes = HasChecks ? _checkedOrder.Select(k => _checked[k]).ToList() : new List<TreeNode> { committed };
            var picks = new List<PickedTable>();
            foreach (var group in nodes.GroupBy(n => n.TableNodeKey))
            {
                var table = group.First().Table;
                // A ticked table means all of its columns, whatever else of it is ticked.
                picks.Add(group.Any(n => n.IsTable)
                    ? new PickedTable(table)
                    : new PickedTable(table, table.Columns.Where(c => group.Any(n => n.Column == c)).Select(c => c.Name)));
            }
            return picks;
        }
    }
}
