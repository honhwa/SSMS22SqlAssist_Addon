using System;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlAssist.Core.Formatting;
using SqlAssist.Editor;
using SqlAssist.Results;
using SqlAssist.Settings;

namespace SqlAssist.Formatting
{
    /// <summary>Tools › SqlAssist › Format SQL (Ctrl+K, Ctrl+Shift+F): formats the selection, or the whole query window when
    /// nothing is selected. One edit, so one Undo puts everything back; the caret or selection stays with the text.</summary>
    internal static class FormatSqlCommand
    {
        private const string Title = "SqlAssist：格式化 SQL";
        /// <summary>Formatting runs on the UI thread, so a script this big is left alone rather than freezing SSMS.</summary>
        private const int MaxChars = 3000000;

        public static bool CanRun()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return ActiveSqlView() != null;
        }

        public static void Run()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var view = ActiveSqlView();
                if (view == null)
                {
                    ScriptDelivery.Status("SqlAssist：格式化 SQL —— 请先打开查询窗口");
                    return;
                }
                Format(view);
            }
            catch (Exception ex)
            {
                Log.Error("Format SQL", ex);
                ScriptDelivery.Show("格式化 SQL 失败：" + ex.GetType().Name + "\n（详情请见 工具 › SqlAssist › 选项… › 打开日志文件）", Title);
            }
        }

        private static void Format(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var buffer = view.TextBuffer;
            var snapshot = buffer.CurrentSnapshot;
            if (!buffer.CheckEditAccess())
            {
                ScriptDelivery.Show("此查询窗口为只读，无法修改", Title);
                return;
            }
            if (!view.Selection.IsEmpty && view.Selection.Mode == TextSelectionMode.Box)
            {
                ScriptDelivery.Show("暂不支持列块选择，请改用普通选择，或不选择任何内容以格式化整个文档", Title);
                return;
            }

            bool selected = !view.Selection.IsEmpty;
            var span = selected ? view.Selection.StreamSelectionSpan.SnapshotSpan : new SnapshotSpan(snapshot, 0, snapshot.Length);
            if (span.Length > MaxChars)
            {
                ScriptDelivery.Show("文本过长，超过 " + MaxChars.ToString("N0") + " 个字符，请只选择要格式化的部分", Title);
                return;
            }

            string original = span.GetText();
            string leading = original.Substring(0, original.Length - original.TrimStart().Length);
            string trailing = original.Substring(original.TrimEnd().Length);
            if (leading.Length == original.Length)
            {
                ScriptDelivery.Status("SqlAssist：格式化 SQL —— 没有可格式化的 SQL 文本");
                return;
            }
            string core = original.Substring(leading.Length, original.Length - leading.Length - trailing.Length);

            var options = SettingsProvider.Load().Format;
            options.Newline = DominantNewline(view, snapshot);
            int baseIndent = selected ? IndentWidth(view, span.Start.GetContainingLine().GetText()) : 0;

            var result = SqlFormatter.Format(core, options, baseIndent);
            if (!result.IsSuccess)
            {
                ScriptDelivery.Status("SqlAssist：格式化 SQL —— 未格式化");
                ScriptDelivery.Show("未格式化，文本保持不变\n\n" + (selected ? "（所选内容）" : "") + result.Error, Title);
                return;
            }

            string formatted = leading + result.Text + trailing;
            if (formatted == original)
            {
                ScriptDelivery.Status("SqlAssist：格式化 SQL —— 已是格式化状态，没有变化");
                return;
            }

            int caret = view.Caret.Position.BufferPosition.Position;
            var after = Replace(buffer, span, original, formatted);
            if (after == null)
            {
                ScriptDelivery.Status("SqlAssist：格式化 SQL —— 修改文本失败");
                return;
            }

            if (selected)
                view.Selection.Select(new SnapshotSpan(after, span.Start.Position, formatted.Length), false);
            int newCaret = caret < span.Start.Position ? caret
                : caret > span.End.Position ? caret + formatted.Length - original.Length
                : span.Start.Position + SqlFormatter.MapOffset(original, caret - span.Start.Position, formatted);
            view.Caret.MoveTo(new SnapshotPoint(after, Math.Min(newCaret, after.Length)));
            ScriptDelivery.Status("SqlAssist：已格式化 SQL（" + (selected ? "所选内容" : "整个文档") + "）—— 按 Ctrl+Z 可撤销");
        }

        /// <summary>Replaces only the stretch that differs, as one edit (one Undo), so the rest keeps its bookmarks, breakpoints and place.</summary>
        private static ITextSnapshot Replace(ITextBuffer buffer, SnapshotSpan span, string original, string formatted)
        {
            int prefix = 0;
            int limit = Math.Min(original.Length, formatted.Length);
            while (prefix < limit && original[prefix] == formatted[prefix]) prefix++;
            int suffix = 0;
            while (suffix < limit - prefix && original[original.Length - 1 - suffix] == formatted[formatted.Length - 1 - suffix]) suffix++;

            using (var edit = buffer.CreateEdit(EditOptions.None, null, "SqlAssist Format SQL"))
            {
                edit.Replace(span.Start.Position + prefix, original.Length - prefix - suffix, formatted.Substring(prefix, formatted.Length - prefix - suffix));
                var applied = edit.Apply();
                return edit.Canceled ? null : applied;
            }
        }

        private static string DominantNewline(ITextView view, ITextSnapshot snapshot)
        {
            foreach (var line in snapshot.Lines)
                if (line.LineBreakLength > 0) return line.GetLineBreakText();
            return view.Options.GetOptionValue(DefaultOptions.NewLineCharacterOptionId);
        }

        /// <summary>Width of the whitespace that starts the line, a tab counting as a tab stop.</summary>
        private static int IndentWidth(ITextView view, string line)
        {
            int tab = Math.Max(1, view.Options.GetOptionValue(DefaultOptions.TabSizeOptionId));
            int width = 0;
            foreach (char c in line)
            {
                if (c == ' ') width++;
                else if (c == '\t') width += tab - width % tab;
                else break;
            }
            return width;
        }

        private static IWpfTextView ActiveSqlView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return ActiveSqlEditor.Find();
        }
    }
}
