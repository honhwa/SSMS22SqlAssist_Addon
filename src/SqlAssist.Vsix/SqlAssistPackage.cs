using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using SqlAssist.Schema;
using SqlAssist.Snippets;
using Task = System.Threading.Tasks.Task;

namespace SqlAssist
{
    /// <summary>Hosts the Tools-menu commands. The editor features are MEF parts and load without it.</summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    // Bump the number whenever the .vsct changes, or SSMS keeps showing the old cached menu.
    [ProvideMenuResource("Menus.ctmenu", 2)]
    public sealed class SqlAssistPackage : AsyncPackage
    {
        public const string PackageGuidString = "9cc4aed8-f2ad-4120-ba6f-222b08aa04a0";
        public static readonly Guid CommandSet = new Guid("545a3b92-0849-4527-8c0a-effa65b4e0c1");
        private const int CmdRefreshSchema = 0x0100;
        private const int CmdEditSnippets = 0x0101;
        private const int CmdOpenLog = 0x0102;
        private const int CmdAbout = 0x0103;

        /// <summary>From Directory.Build.props via the assembly version.</summary>
        public static string Version { get; } = typeof(SqlAssistPackage).Assembly.GetName().Version.ToString(3);

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
            {
                commands.AddCommand(new MenuCommand((s, e) => SchemaCache.Instance.Clear(), new CommandID(CommandSet, CmdRefreshSchema)));
                commands.AddCommand(new MenuCommand((s, e) => OpenFile(SnippetProvider.FilePath), new CommandID(CommandSet, CmdEditSnippets)));
                commands.AddCommand(new MenuCommand((s, e) => OpenFile(Log.FilePath), new CommandID(CommandSet, CmdOpenLog)));
                commands.AddCommand(new MenuCommand((s, e) => ShowAbout(), new CommandID(CommandSet, CmdAbout)));
            }
        }

        private void ShowAbout()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(this,
                "SqlAssist " + Version + "\n\n" +
                "IntelliSense + Snippets for SSMS 22\n\n" +
                "Snippets: " + SnippetProvider.FilePath + "\n" +
                "Log: " + Log.FilePath,
                "About SqlAssist",
                Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        private static void OpenFile(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (path == SnippetProvider.FilePath) SnippetProvider.EnsureFileExists();
                if (!System.IO.File.Exists(path)) System.IO.File.WriteAllText(path, "");
                VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, path);
            }
            catch (Exception ex)
            {
                Log.Error("Open " + path, ex);
            }
        }
    }
}
