using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SqlAssist.Setup
{
    /// <summary>Double-click: the setup window; /remove opens it with "uninstall" chosen (Programs and Features).
    /// Command line (no window): /install or /uninstall, with /quiet.
    /// Exit codes: 0 done, 1 failed, 2 SSMS is running, 3 SSMS 22 not found.</summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
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

        private static bool Is(string arg, string name) =>
            Regex.IsMatch(arg, "^[-/]{1,2}" + name + "$", RegexOptions.IgnoreCase);
    }
}
