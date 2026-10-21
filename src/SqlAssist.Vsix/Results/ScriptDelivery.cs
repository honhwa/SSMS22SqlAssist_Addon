using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlAssist.Connection;
using SqlAssist.Search;

namespace SqlAssist.Results
{
    internal enum ScriptTarget
    {
        NewWindow = 0,
        Clipboard = 1,
        File = 2,
    }

    /// <summary>Where generated SQL goes: a new query window on the active connection, the clipboard, or a .sql file. It is
    /// only ever put somewhere to read; nothing here executes it.</summary>
    internal static class ScriptDelivery
    {
        /// <param name="what">For the status line and the Save dialog, e.g. "UPDATE script (3 rows)".</param>
        /// <param name="title">Caption of any message box.</param>
        public static void Send(string script, ScriptTarget target, string what, string title)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            switch (target)
            {
                case ScriptTarget.NewWindow:
                    string error = SsmsNavigator.OpenQueryWindow(ActiveConnection.TryGet()?.UiConnectionInfo, script);
                    if (error != null) Show(error, title);
                    break;
                case ScriptTarget.Clipboard:
                    if (CopyToClipboard(script, title)) Status("SqlAssist: " + what + " copied to the clipboard");
                    break;
                case ScriptTarget.File:
                    SaveToFile(script, what);
                    break;
            }
        }

        public static void Status(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar status)
            {
                status.IsFrozen(out int frozen);
                if (frozen == 0) status.SetText(text);
            }
        }

        public static void Show(string message, string title)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, title,
                OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        private static bool CopyToClipboard(string text, string title)
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
                    Show("Could not copy to the clipboard: " + ex.Message, title);
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
    }
}
