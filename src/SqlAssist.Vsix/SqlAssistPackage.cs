using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using SqlAssist.Schema;
using SqlAssist.Search;
using SqlAssist.Snippets;
using Task = System.Threading.Tasks.Task;

namespace SqlAssist
{
    /// <summary>Hosts the Tools-menu commands. The editor features are MEF parts and load without it.</summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    // Bump the number whenever the .vsct changes, or SSMS keeps showing the old cached menu.
    [ProvideMenuResource("Menus.ctmenu", 7)]
    // Puts the extension folder on SSMS's assembly probing path. The image service resolves the toolbar logo's
    // pack URIs (/SqlAssist;component/Resources/...) by loading "SqlAssist" by name, often before this package
    // has loaded; without the binding path that load fails and the button shows no icon.
    [ProvideBindingPath]
    [ProvideToolWindow(typeof(SearchToolWindow), Style = VsDockStyle.Tabbed, Window = OutputWindowGuid)]
    public sealed class SqlAssistPackage : AsyncPackage
    {
        public const string PackageGuidString = "9cc4aed8-f2ad-4120-ba6f-222b08aa04a0";
        public static readonly Guid CommandSet = new Guid("545a3b92-0849-4527-8c0a-effa65b4e0c1");
        private const int CmdRefreshSchema = 0x0100;
        private const int CmdEditSnippets = 0x0101;
        private const int CmdOpenLog = 0x0102;
        private const int CmdAbout = 0x0103;
        private const int CmdSearch = 0x0104;
        private const int CmdSearchToolbar = 0x0105;
        /// <summary>The Output window: the Search window docks as a tab beside it on first open.</summary>
        private const string OutputWindowGuid = "34e76e81-ee4a-11d0-ae2e-00a0c90fffc3";

        /// <summary>From Directory.Build.props via the assembly version.</summary>
        public static string Version { get; } = typeof(SqlAssistPackage).Assembly.GetName().Version.ToString(3);

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
            {
                commands.AddCommand(new MenuCommand((s, e) => { SchemaCache.Instance.Clear(); SearchIndexCache.Instance.Clear(); }, new CommandID(CommandSet, CmdRefreshSchema)));
                foreach (int id in new[] { CmdSearch, CmdSearchToolbar })
                    commands.AddCommand(new MenuCommand((s, e) => JoinableTaskFactory.RunAsync(ShowSearchAsync).FileAndForget("SqlAssist/ShowSearch"), new CommandID(CommandSet, id)));
                commands.AddCommand(new MenuCommand((s, e) => OpenFile(SnippetProvider.FilePath), new CommandID(CommandSet, CmdEditSnippets)));
                commands.AddCommand(new MenuCommand((s, e) => OpenFile(Log.FilePath), new CommandID(CommandSet, CmdOpenLog)));
                commands.AddCommand(new MenuCommand((s, e) => ShowAbout(), new CommandID(CommandSet, CmdAbout)));
            }
        }

        private async Task ShowSearchAsync()
        {
            try
            {
                var window = await ShowToolWindowAsync(typeof(SearchToolWindow), 0, true, DisposalToken);
                await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                (window as SearchToolWindow)?.FocusSearch();
            }
            catch (Exception ex)
            {
                Log.Error("Show search window", ex);
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
