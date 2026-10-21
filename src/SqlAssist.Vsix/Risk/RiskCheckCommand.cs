using System;
using Microsoft.VisualStudio.Shell;
using SqlAssist.Core.Risk;
using SqlAssist.Editor;
using SqlAssist.Results;
using SqlAssist.Settings;

namespace SqlAssist.Risk
{
    /// <summary>Tools › SqlAssist › Check SQL Risks…: looks at the selection, or the whole query window, for UPDATE or DELETE with
    /// no WHERE, TRUNCATE TABLE and DROP TABLE, and lists them. It reads the text and changes nothing: no WHERE is added and nothing is run.</summary>
    internal static class RiskCheckCommand
    {
        private const int MaxChars = 5000000;

        public static bool CanRun()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return ActiveSqlEditor.Find() != null;
        }

        public static void Run()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var view = ActiveSqlEditor.Find();
                if (view == null)
                {
                    ScriptDelivery.Status("SqlAssist: Check SQL Risks — เปิดหน้า query ก่อน");
                    return;
                }
                var scope = ActiveSqlEditor.RunScope(view, out bool isSelection);
                if (scope.Length > MaxChars)
                {
                    ScriptDelivery.Show("ข้อความยาวเกิน " + MaxChars.ToString("N0") + " ตัวอักษร ให้เลือกเฉพาะส่วนที่ต้องการตรวจแทน", "SqlAssist: Check SQL Risks");
                    return;
                }
                var report = RiskAnalyzer.Analyze(scope.GetText(), SettingsProvider.Load().Risk);
                RiskWindow.ShowReport(view, scope, isSelection, report);
            }
            catch (Exception ex)
            {
                Log.Error("Check SQL risks", ex);
                ScriptDelivery.Show("ตรวจ SQL ไม่สำเร็จ: " + ex.GetType().Name + "\n(ดูรายละเอียดที่ Tools › SqlAssist › Options… › เปิดไฟล์ Log)", "SqlAssist: Check SQL Risks");
            }
        }
    }
}
