using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace SqlAssist.Setup
{
    /// <summary>Double-click: the setup window; /remove opens it with "uninstall" chosen (Programs and Features).
    /// Command line (no window): /install or /uninstall, with /quiet; /installonexit waits for every SSMS to close,
    /// then installs (started by the extension's "Install when SSMS closes").
    /// Exit codes: 0 done, 1 failed, 2 SSMS is running, 3 SSMS 22 not found.</summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Any(a => Is(a, "installonexit"))) return InstallOnExit();
            bool install = args.Any(a => Is(a, "install"));
            bool uninstall = args.Any(a => Is(a, "uninstall"));
            if (install || uninstall) return RunUnattended(uninstall);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // /remove: started from Programs and Features' Uninstall button.
            bool remove = args.Any(a => Is(a, "remove"));
            if (remove && !ExtensionInstalled())
            {
                // Removed some other way (e.g. SSMS's Extension Manager): only the stale entry is left.
                SetupEngine.UnregisterUninstaller();
                MessageBox.Show("SqlAssist ไม่ได้ติดตั้งอยู่ใน SSMS 22 แล้ว\nนำรายการออกจาก Programs and Features เรียบร้อย",
                    "SqlAssist Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            Application.Run(new SetupForm(preferUninstall: remove));
            return 0;
        }

        private static bool ExtensionInstalled()
        {
            string ide = SetupEngine.FindSsmsIde();
            return ide != null && SetupEngine.FindInstalledVersion(ide) != null;
        }

        private static int RunUnattended(bool uninstall)
        {
            string ide = SetupEngine.FindSsmsIde();
            if (ide == null) return 3;
            if (SetupEngine.RunningSsmsCount() > 0) return 2;
            return (uninstall ? SetupEngine.Uninstall(ide) : SetupEngine.Install(ide)) == null ? 0 : 1;
        }

        /// <summary>Waits in the background until no SSMS is running, installs, and leaves the outcome in
        /// %LOCALAPPDATA%\SqlAssist\update-result.txt for the extension to report on the next SSMS start.</summary>
        private static int InstallOnExit()
        {
            // One waiter is enough: a second "Install when SSMS closes" (or a restart of the waiter) just leaves.
            using (var mutex = new Mutex(true, @"Local\SqlAssist.InstallOnExit", out bool first))
            {
                if (!first) return 0;
                // VSIXInstaller needs every SSMS closed; two quiet polls in a row so a restarting SSMS isn't missed.
                int quiet = 0;
                while (quiet < 2)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(3));
                    quiet = SetupEngine.RunningSsmsCount() == 0 ? quiet + 1 : 0;
                }

                string ide = SetupEngine.FindSsmsIde();
                if (ide == null) return WriteResult(false, "SSMS 22 not found", 3);
                // Already this version or newer (installed some other way meanwhile): nothing to do.
                if (SetupEngine.CompareInstalled(SetupEngine.FindInstalledVersion(ide)) >= 0) return 0;
                string error;
                try { error = SetupEngine.Install(ide); }
                catch (Exception ex) { error = ex.Message; }
                return WriteResult(error == null, error, error == null ? 0 : 1);
            }
        }

        private static int WriteResult(bool ok, string error, int exitCode)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlAssist");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "update-result.txt"),
                    "Version=" + SetupEngine.SetupVersion + "\nOk=" + (ok ? "true" : "false") + "\n" +
                    (error != null ? "Error=" + error.Replace("\r", " ").Replace("\n", " ") + "\n" : ""));
            }
            catch { /* nowhere left to report it */ }
            return exitCode;
        }

        private static bool Is(string arg, string name) =>
            Regex.IsMatch(arg, "^[-/]{1,2}" + name + "$", RegexOptions.IgnoreCase);
    }
}
