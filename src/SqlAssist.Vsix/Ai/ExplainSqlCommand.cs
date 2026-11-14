using System;
using Microsoft.VisualStudio.Shell;
using SqlAssist.Core.Ai;
using SqlAssist.Core.Schema;
using SqlAssist.Editor;
using SqlAssist.Results;
using SqlAssist.Schema;
using SqlAssist.Settings;

namespace SqlAssist.Ai
{
    /// <summary>Tools › SqlAssist › Explain SQL (AI)… and the query editor's right-click copy: sends the selection (or the whole query
    /// window) to the Claude API and shows the explanation. Nothing is sent unless the user turned it on in Options and gave a key, and
    /// before the first send the exact text is shown for a yes. The schema used is only what is already loaded; nothing is queried.</summary>
    internal static class ExplainSqlCommand
    {
        private const string Caption = "SqlAssist: Explain SQL";

        public static bool CanRun()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return ActiveSqlEditor.Find() != null;
            }
            catch (Exception ex)
            {
                Log.Error("Explain SQL: query status", ex);
                return false;
            }
        }

        public static void Run()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var view = ActiveSqlEditor.Find();
                if (view == null)
                {
                    ScriptDelivery.Status("SqlAssist: Explain SQL — เปิดหน้า query ก่อน");
                    return;
                }
                var settings = SettingsProvider.Load();
                if (!settings.Ai.Enabled || !AiKeyStore.HasKey)
                {
                    ScriptDelivery.Show("Explain SQL ยังไม่ได้เปิดใช้ เปิดและใส่ API key ที่ Tools › SqlAssist › Options… › AI", Caption);
                    return;
                }
                var scope = ActiveSqlEditor.RunScope(view, out bool isSelection);
                string text = scope.GetText();
                if (string.IsNullOrWhiteSpace(text))
                {
                    ScriptDelivery.Status("SqlAssist: Explain SQL — ไม่มี SQL ให้อธิบาย");
                    return;
                }
                if (text.Length > ExplainRequest.MaxSqlChars)
                {
                    ScriptDelivery.Show("SQL ยาว " + text.Length.ToString("N0") + " ตัวอักษร เกินที่ส่งได้ (" + ExplainRequest.MaxSqlChars.ToString("N0") +
                                        ") ให้เลือกเฉพาะส่วนที่ต้องการอธิบายแทน", Caption);
                    return;
                }

                var request = ExplainRequest.Build(text, LoadedSchema(scope), settings.Ai);
                if (!settings.Ai.ConsentGiven)
                {
                    if (!AiConsentWindow.Ask(request, settings.Ai.Endpoint, out bool dontAskAgain)) return;
                    if (dontAskAgain)
                    {
                        settings.Ai.ConsentGiven = true;
                        SettingsProvider.Save(settings);
                    }
                }
                string key = AiKeyStore.Read();
                if (key == null)
                {
                    ScriptDelivery.Show("อ่าน API key ที่บันทึกไว้ไม่ได้ ให้ใส่ใหม่ที่ Tools › SqlAssist › Options… › AI", Caption);
                    return;
                }
                Log.Info("Explain SQL: " + text.Length + " chars, " + request.TableCount + " tables in the schema digest");
                ExplainWindow.Start(view, scope, isSelection, request, settings.Ai.Endpoint, key);
            }
            catch (Exception ex)
            {
                Log.Error("Explain SQL", ex);
                ScriptDelivery.Show("Explain SQL ไม่สำเร็จ: " + ex.GetType().Name + "\n(ดูรายละเอียดที่ Tools › SqlAssist › Options… › เปิดไฟล์ Log)", Caption);
            }
        }

        /// <summary>The schema of the database the text is written against (the last USE above it, else the window's), only if it is
        /// already loaded; null otherwise. Explain SQL never waits for a schema load.</summary>
        private static SchemaModel LoadedSchema(Microsoft.VisualStudio.Text.SnapshotSpan scope)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var target = EditorConnection.Get(TextWindow.Around(scope.Snapshot, scope.Start.Position));
                return target == null ? null : SchemaCache.Instance.TryGetLoaded(target);
            }
            catch (Exception ex)
            {
                Log.Error("Explain SQL: schema", ex);
                return null;
            }
        }
    }
}
