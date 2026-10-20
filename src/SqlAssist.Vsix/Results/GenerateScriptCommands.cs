using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlAssist.Connection;
using SqlAssist.Core.Results;
using SqlAssist.Search;

namespace SqlAssist.Results
{
    internal enum ScriptTarget
    {
        NewWindow = 0,
        Clipboard = 1,
        File = 2,
    }

    /// <summary>Results grid › Generate Script As › INSERT / INSERT #tmpTable / WHERE IN() / WHERE AND OR / UPDATE ›
    /// New Query Window / Clipboard / File, plus the dbForge shortcuts Ctrl+J then I/T/W/O/U (to the clipboard) while
    /// a results grid has focus.</summary>
    internal static class GenerateScriptCommands
    {
        /// <summary>Command id = First + kind * 0x10 + target (see SqlAssistPackage.vsct).</summary>
        public const int First = 0x0200;
        /// <summary>More rows than this would make a script SSMS's editor struggles to open.</summary>
        private const int MaxRows = 100000;
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
        private static bool _chordPending;

        public static void Register(OleMenuCommandService commands)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (ResultScriptKind kind in Enum.GetValues(typeof(ResultScriptKind)))
                foreach (ScriptTarget target in Enum.GetValues(typeof(ScriptTarget)))
                {
                    var k = kind; var t = target;
                    var command = new OleMenuCommand((s, e) => Run(k, t), new CommandID(SqlAssistPackage.CommandSet, First + (int)kind * 0x10 + (int)target));
                    // Queried while the grid context menu opens, when focus is still in the grid.
                    command.BeforeQueryStatus += (s, e) => ResultsGridReader.Capture();
                    commands.AddCommand(command);
                }
            ComponentDispatcher.ThreadPreprocessMessage += OnThreadMessage;
        }

        public static void Run(ResultScriptKind kind, ScriptTarget target)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var selection = ResultsGridReader.TryRead(out string problem);
                if (selection == null) { Show(problem); return; }
                if (selection.Rows.Count > MaxRows)
                {
                    Show("The selection has " + selection.Rows.Count.ToString("N0") + " rows; Generate Script As handles up to " + MaxRows.ToString("N0") + ".");
                    return;
                }

                var needed = ResultScriptBuilder.RequiredColumns(kind, selection.Columns, selection.Selected);
                var rows = selection.Rows.Select(r => selection.ReadRow(r, needed)).ToList();
                string script = ResultScriptBuilder.Build(kind, selection.Columns, selection.Selected, rows);
                string what = Describe(kind) + " script (" + rows.Count + (rows.Count == 1 ? " row)" : " rows)");

                switch (target)
                {
                    case ScriptTarget.NewWindow:
                        string error = SsmsNavigator.OpenQueryWindow(ActiveConnection.TryGet()?.UiConnectionInfo, script);
                        if (error != null) Show(error);
                        break;
                    case ScriptTarget.Clipboard:
                        if (CopyToClipboard(script)) Status("SqlAssist: " + what + " copied to the clipboard");
                        break;
                    case ScriptTarget.File:
                        SaveToFile(script, what);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Generate script", ex);
                Show("Generate Script As failed: " + ex.Message);
            }
        }

        /// <summary>Ctrl+J starts the chord only while a results grid has focus, so the editor's Ctrl+J (List Members)
        /// is untouched; the next key picks the statement, and any other key cancels.</summary>
        private static void OnThreadMessage(ref MSG msg, ref bool handled)
        {
            if (handled || msg.message != WM_KEYDOWN) return;
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread(); // raised by the main thread's message loop
                int key = msg.wParam.ToInt32() & 0xFFFF;
                if (key == VK_SHIFT || key == VK_CONTROL || key == VK_MENU) return;
                if (_chordPending)
                {
                    _chordPending = false;
                    ResultScriptKind? kind = KindForKey(key);
                    if (kind == null) return;
                    handled = true;
                    Run(kind.Value, ScriptTarget.Clipboard);
                    return;
                }
                if (key == 'J' && IsDown(VK_CONTROL) && !IsDown(VK_SHIFT) && !IsDown(VK_MENU) && ResultsGridReader.IsGridFocused())
                {
                    _chordPending = true;
                    handled = true;
                    Status("SqlAssist: Ctrl+J, then I (INSERT), T (INSERT #tmpTable), W (WHERE IN), O (WHERE AND OR) or U (UPDATE)");
                }
            }
            catch (Exception ex)
            {
                _chordPending = false;
                Log.Error("Generate script shortcut", ex);
            }
        }

        private static ResultScriptKind? KindForKey(int key)
        {
            switch (key)
            {
                case 'I': return ResultScriptKind.Insert;
                case 'T': return ResultScriptKind.InsertTempTable;
                case 'W': return ResultScriptKind.WhereIn;
                case 'O': return ResultScriptKind.WhereAndOr;
                case 'U': return ResultScriptKind.Update;
                default: return null;
            }
        }

        private static string Describe(ResultScriptKind kind)
        {
            switch (kind)
            {
                case ResultScriptKind.Insert: return "INSERT";
                case ResultScriptKind.InsertTempTable: return "INSERT #tmpTable";
                case ResultScriptKind.WhereIn: return "WHERE IN()";
                case ResultScriptKind.WhereAndOr: return "WHERE AND OR";
                default: return "UPDATE";
            }
        }

        private static bool CopyToClipboard(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Another program can hold the clipboard open for a moment.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(text);
                    return true;
                }
                catch (System.Runtime.InteropServices.COMException) when (attempt < 3)
                {
                    System.Threading.Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    Log.Error("Copy script", ex);
                    Show("Could not copy to the clipboard: " + ex.Message);
                    return false;
                }
            }
        }

        private static void SaveToFile(string script, string what)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save " + what,
                Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*",
                DefaultExt = ".sql",
                FileName = "Script.sql",
            };
            if (dialog.ShowDialog() != true) return;
            File.WriteAllText(dialog.FileName, script, new UTF8Encoding(true));
            Status("SqlAssist: " + what + " saved to " + dialog.FileName);
        }

        private static bool IsDown(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetKeyState(int virtualKey);

        private static void Status(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar status)
            {
                status.IsFrozen(out int frozen);
                if (frozen == 0) status.SetText(text);
            }
        }

        private static void Show(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, "SqlAssist: Generate Script As",
                OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
