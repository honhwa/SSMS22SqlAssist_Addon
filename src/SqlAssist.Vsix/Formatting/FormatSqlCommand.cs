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
        private const string Title = "SqlAssist: Format SQL";
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
                    ScriptDelivery.Status("SqlAssist: Format SQL — เปิดหน้า query ก่อน");
                    return;
                }
                Format(view);
            }
            catch (Exception ex)
            {
                Log.Error("Format SQL", ex);
                ScriptDelivery.Show("จัดรูปแบบ SQL ไม่สำเร็จ: " + ex.GetType().Name + "\n(ดูรายละเอียดที่ Tools › SqlAssist › Options… › เปิดไฟล์ Log)", Title);
            }
        }

        private static void Format(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var buffer = view.TextBuffer;
            var snapshot = buffer.CurrentSnapshot;
            if (!buffer.CheckEditAccess())
            {
                ScriptDelivery.Show("หน้า query นี้แก้ไขไม่ได้ (read-only)", Title);
                return;
            }
            if (!view.Selection.IsEmpty && view.Selection.Mode == TextSelectionMode.Box)
            {
                ScriptDelivery.Show("ยังไม่รองรับการเลือกแบบ box ให้เลือกแบบปกติหรือไม่เลือกอะไรเพื่อจัดทั้งเอกสาร", Title);
                return;
            }

            bool selected = !view.Selection.IsEmpty;
            var span = selected ? view.Selection.StreamSelectionSpan.SnapshotSpan : new SnapshotSpan(snapshot, 0, snapshot.Length);
            if (span.Length > MaxChars)
            {
                ScriptDelivery.Show("ข้อความยาวเกิน " + MaxChars.ToString("N0") + " ตัวอักษร ให้เลือกเฉพาะส่วนที่ต้องการจัดแทน", Title);
                return;
            }

            string original = span.GetText();
            string leading = original.Substring(0, original.Length - original.TrimStart().Length);
            string trailing = original.Substring(original.TrimEnd().Length);
            if (leading.Length == original.Length)
            {
                ScriptDelivery.Status("SqlAssist: Format SQL — ไม่มีข้อความ SQL ให้จัด");
                return;
            }
            string core = original.Substring(leading.Length, original.Length - leading.Length - trailing.Length);

            var options = SettingsProvider.Load().Format;
            options.Newline = DominantNewline(view, snapshot);
            int baseIndent = selected ? IndentWidth(view, span.Start.GetContainingLine().GetText()) : 0;

            var result = SqlFormatter.Format(core, options, baseIndent);
            if (!result.IsSuccess)
            {
                ScriptDelivery.Status("SqlAssist: Format SQL — ไม่ได้จัดรูปแบบ");
                ScriptDelivery.Show("ไม่ได้จัดรูปแบบ ข้อความยังเหมือนเดิม\n\n" + (selected ? "(ส่วนที่เลือก) " : "") + result.Error, Title);
                return;
            }

            string formatted = leading + result.Text + trailing;
            if (formatted == original)
            {
                ScriptDelivery.Status("SqlAssist: Format SQL — จัดรูปแบบอยู่แล้ว ไม่มีอะไรเปลี่ยน");
                return;
            }

            int caret = view.Caret.Position.BufferPosition.Position;
            var after = Replace(buffer, span, original, formatted);
            if (after == null)
            {
                ScriptDelivery.Status("SqlAssist: Format SQL — แก้ข้อความไม่สำเร็จ");
                return;
            }

            if (selected)
                view.Selection.Select(new SnapshotSpan(after, span.Start.Position, formatted.Length), false);
            int newCaret = caret < span.Start.Position ? caret
                : caret > span.End.Position ? caret + formatted.Length - original.Length
                : span.Start.Position + SqlFormatter.MapOffset(original, caret - span.Start.Position, formatted);
            view.Caret.MoveTo(new SnapshotPoint(after, Math.Min(newCaret, after.Length)));
            ScriptDelivery.Status("SqlAssist: จัดรูปแบบ SQL แล้ว (" + (selected ? "ส่วนที่เลือก" : "ทั้งเอกสาร") + ") — Ctrl+Z เพื่อย้อนกลับ");
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
