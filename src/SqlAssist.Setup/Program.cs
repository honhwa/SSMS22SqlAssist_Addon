using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SqlAssist.Setup
{
    /// <summary>Double-click: the setup window. Command line (no window): /install or /uninstall, with /quiet.
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
            Application.Run(new SetupForm());
            return 0;
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
