using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace SqlAssist.Editor
{
    /// <summary>The query window being worked in, as an editor view.</summary>
    internal static class ActiveSqlEditor
    {
        /// <summary>The text view of the query window that was last active, when it is a T-SQL editor; otherwise null.</summary>
        public static IWpfTextView Find()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(Package.GetGlobalService(typeof(SVsTextManager)) is IVsTextManager manager)) return null;
            if (manager.GetActiveView(0, null, out IVsTextView active) != 0 || active == null) return null;
            var model = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
            var view = model?.GetService<IVsEditorAdaptersFactoryService>()?.GetWpfTextView(active);
            return view != null && view.TextBuffer.ContentType.IsOfType(SqlContentType.Name) ? view : null;
        }

        /// <summary>What running the query window would run: the selection, or the whole text when nothing is selected. A box
        /// selection counts as the lines it touches, as the editor sees it.</summary>
        public static SnapshotSpan RunScope(ITextView view, out bool isSelection)
        {
            var snapshot = view.TextSnapshot;
            isSelection = !view.Selection.IsEmpty;
            if (!isSelection) return new SnapshotSpan(snapshot, 0, snapshot.Length);
            var spans = view.Selection.SelectedSpans;
            var start = spans[0].Start;
            var end = spans[spans.Count - 1].End;
            return new SnapshotSpan(start, end);
        }
    }
}
