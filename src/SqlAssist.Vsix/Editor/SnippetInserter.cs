using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlAssist.Core.Snippets;

namespace SqlAssist.Editor
{
    internal static class SnippetInserter
    {
        /// <summary>Replaces the shortcut with the snippet (one undo step) and puts the caret at $end$.</summary>
        public static void Insert(ITextView view, IAsyncCompletionBroker broker, SnapshotSpan shortcut, Snippet snippet)
        {
            var line = shortcut.Start.GetContainingLine();
            string lineText = line.GetText();
            string indent = lineText.Substring(0, lineText.Length - lineText.TrimStart().Length);
            string newLine = line.GetLineBreakText();
            if (string.IsNullOrEmpty(newLine)) newLine = view.Options.GetOptionValue(DefaultOptions.NewLineCharacterOptionId);

            var (text, caretOffset) = SnippetStore.Render(snippet, indent, newLine);
            var after = shortcut.Snapshot.TextBuffer.Replace(shortcut, text);
            view.Caret.MoveTo(new SnapshotPoint(after, shortcut.Start.Position + caretOffset));
            OpenListIfTablePosition(view, broker);
        }

        /// <summary>After "ssf" → "SELECT * FROM |" the next thing typed is a table, so show the tables now.
        /// Posted, because this can run inside a completion commit that is still finishing.</summary>
        public static void OpenListIfTablePosition(ITextView view, IAsyncCompletionBroker broker)
        {
            if (broker == null) return;
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await Task.Yield(); // let the commit or edit in progress finish first
                try
                {
                    if (view.IsClosed || broker.IsCompletionActive(view)) return;
                    var caret = view.Caret.Position.BufferPosition;
                    // A space trigger: the source only joins in where a table name is expected.
                    var trigger = new CompletionTrigger(CompletionTriggerReason.Insertion, caret.Snapshot, ' ');
                    broker.TriggerCompletion(view, trigger, caret, CancellationToken.None)?.OpenOrUpdate(trigger, caret, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log.Error("Opening list after snippet", ex);
                }
            }).FileAndForget("SqlAssist/OpenListAfterSnippet");
        }
    }
}
