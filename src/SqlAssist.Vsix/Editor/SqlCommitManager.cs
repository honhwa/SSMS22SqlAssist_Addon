using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Snippets;

namespace SqlAssist.Editor
{
    [Export(typeof(IAsyncCompletionCommitManagerProvider))]
    [Name("SqlAssist commit manager")]
    [ContentType(SqlContentType.Name)]
    internal sealed class SqlCommitManagerProvider : IAsyncCompletionCommitManagerProvider
    {
        [Import] internal IAsyncCompletionBroker Broker { get; set; }

        public IAsyncCompletionCommitManager GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new SqlCommitManager(textView, Broker));
    }

    /// <summary>Only Tab and Enter commit — never a typed character — so typing "c " stays "c ".
    /// Enter on a word that is already fully typed commits it and still starts the new line.</summary>
    internal sealed class SqlCommitManager : IAsyncCompletionCommitManager
    {
        private readonly ITextView _view;
        private readonly IAsyncCompletionBroker _broker;

        public SqlCommitManager(ITextView view, IAsyncCompletionBroker broker)
        {
            _view = view;
            _broker = broker;
        }

        public IEnumerable<char> PotentialCommitCharacters => Array.Empty<char>();

        public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token) => false;

        public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token)
        {
            try
            {
                var span = session.ApplicableToSpan.GetSpan(buffer.CurrentSnapshot);

                if (item.Properties.TryGetProperty(ItemProps.Snippet, out Snippet snippet))
                {
                    SnippetInserter.Insert(_view, _broker, span, snippet);
                    return new CommitResult(true, CommitBehavior.None);
                }

                // Table tree ("SELECT |"): write the column list and FROM/JOINs for what is ticked, or else for the
                // committed table (all columns) or column.
                var node = TreeState.NodeOf(item);
                if (node != null && session.Properties.TryGetProperty(typeof(TreeState), out TreeState tree))
                {
                    var line = span.Start.GetContainingLine();
                    string linePrefix = span.Snapshot.GetText(line.Start, span.Start - line.Start);
                    string newLine = line.GetLineBreakText();
                    if (string.IsNullOrEmpty(newLine)) newLine = _view.Options.GetOptionValue(DefaultOptions.NewLineCharacterOptionId);

                    string sql = SelectBuilder.Build(tree.Schema, tree.Picks(node), linePrefix, newLine, tree.TakenAliases);
                    var after = buffer.Replace(span, sql);
                    _view.Caret.MoveTo(new SnapshotPoint(after, span.Start.Position + sql.Length));
                    return new CommitResult(true, CommitBehavior.None);
                }

                bool enter = typedChar == '\n' || typedChar == '\r';
                if (enter && string.Equals(span.GetText().TrimStart('['), item.FilterText, StringComparison.OrdinalIgnoreCase))
                {
                    buffer.Replace(span, item.InsertText);
                    return new CommitResult(true, CommitBehavior.RaiseFurtherReturnKeyAndTabKeyCommandHandlers);
                }
            }
            catch (Exception ex)
            {
                Log.Error("TryCommit", ex);
            }
            return CommitResult.Unhandled;
        }
    }
}
