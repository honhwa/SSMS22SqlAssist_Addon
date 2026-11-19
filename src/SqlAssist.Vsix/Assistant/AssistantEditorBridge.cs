using System;
using System.Linq;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using SqlAssist.Editor;

namespace SqlAssist.Assistant
{
    internal static class AssistantEditorBridge
    {
        public static string SelectedSql()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var view = ActiveSqlEditor.Find();
            if (view == null || view.Selection.IsEmpty) throw new ArgumentException("เลือก SQL ในหน้า query ก่อน แล้วกดแนบ SQL ที่เลือก");
            // Box selections contain disjoint fragments: never include the text between them.
            string sql = string.Join(Environment.NewLine, view.Selection.SelectedSpans.Select(span => span.GetText()));
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("ส่วนที่เลือกไม่มีข้อความ SQL");
            if (sql.Length > SqlAssist.Core.Assistant.ChatSession.DefaultContextLimit) throw new ArgumentException("SQL ที่เลือกยาวเกิน 32,000 ตัวอักษร กรุณาเลือกส่วนที่เล็กลง");
            return sql;
        }
        public static void Insert(string sql)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var view = ActiveSqlEditor.Find();
            if (view == null || view.IsClosed) throw new ArgumentException("เปิดหน้า SQL query ที่ต้องการแทรกก่อน");
            if (!view.Selection.IsEmpty) throw new ArgumentException("มีข้อความที่เลือกอยู่ กรุณายกเลิก selection ก่อนแทรก SQL เพื่อไม่แทนที่ข้อความเดิม");
            var buffer = view.TextBuffer;
            if (!buffer.CheckEditAccess() || buffer.IsReadOnly(view.Caret.Position.BufferPosition.Position)) throw new ArgumentException("หน้า query นี้เป็น read-only หรือแก้ไขไม่ได้");
            var component = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
            var registry = component?.GetService<ITextUndoHistoryRegistry>();
            if (registry == null) throw new ArgumentException("ไม่สามารถสร้าง Undo transaction ในหน้า query นี้ได้");
            if (!registry.TryGetHistory(buffer, out ITextUndoHistory history)) history = registry.RegisterHistory(buffer);
            int position = view.Caret.Position.BufferPosition.Position;
            using (var transaction = history.CreateTransaction("Insert SQL from AI Assistant"))
            using (var edit = buffer.CreateEdit())
            {
                if (!edit.Insert(position, sql)) { edit.Cancel(); throw new ArgumentException("แทรก SQL ไม่สำเร็จ ไม่มีข้อความถูกเปลี่ยน"); }
                var after = edit.Apply();
                if (edit.Canceled) throw new ArgumentException("แทรก SQL ถูกยกเลิก");
                view.Caret.MoveTo(new SnapshotPoint(after, position + sql.Length));
                transaction.Complete();
            }
            view.VisualElement.Focus();
        }
    }
}
