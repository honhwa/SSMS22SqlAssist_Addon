using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using SqlAssist.ClipboardSql;
using SqlAssist.Formatting;
using SqlAssist.Results;
using SqlAssist.Risk;
using SqlAssist.Settings;
using SqlAssist.Schema;
using SqlAssist.Search;
using SqlAssist.Snippets;
using SqlAssist.Update;
using Task = System.Threading.Tasks.Task;

namespace SqlAssist
{
    /// <summary>Hosts the Tools-menu and results-grid commands. The editor features are MEF parts and load without it.</summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    // Bump the number whenever the .vsct changes, or SSMS keeps showing the old cached menu.
    [ProvideMenuResource("Menus.ctmenu", 12)]
    // Loads in the background once SSMS is up, so the daily update check runs without a menu click first.
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
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
        private const int CmdAbout = 0x0103;
        private const int CmdSearch = 0x0104;
        private const int CmdSearchToolbar = 0x0105;
        private const int CmdCheckUpdates = 0x0106;
        private const int CmdClipboardToSql = 0x0107;
        private const int CmdFormatSql = 0x0108;
        private const int CmdOptions = 0x0109;
        private const int CmdCheckRisks = 0x010A;
        private const int CmdFormatSqlContext = 0x010B;
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
                commands.AddCommand(new MenuCommand((s, e) => ShowSnippetManager(), new CommandID(CommandSet, CmdEditSnippets)));
                commands.AddCommand(new MenuCommand((s, e) => ShowCsvToSql(), new CommandID(CommandSet, CmdClipboardToSql)));
                // The Tools menu item and the query editor's right-click item run the same command.
                foreach (int id in new[] { CmdFormatSql, CmdFormatSqlContext })
                {
                    var format = new OleMenuCommand((s, e) => FormatSqlCommand.Run(), new CommandID(CommandSet, id));
                    // Greyed out unless a T-SQL query window is the one being worked in, so the shortcut does nothing anywhere else.
                    format.BeforeQueryStatus += (s, e) => ((OleMenuCommand)s).Enabled = FormatSqlCommand.CanRun();
                    commands.AddCommand(format);
                }
                commands.AddCommand(new MenuCommand((s, e) => ShowOptions(), new CommandID(CommandSet, CmdOptions)));
                var risks = new OleMenuCommand((s, e) => RiskCheckCommand.Run(), new CommandID(CommandSet, CmdCheckRisks));
                risks.BeforeQueryStatus += (s, e) => ((OleMenuCommand)s).Enabled = RiskCheckCommand.CanRun();
                commands.AddCommand(risks);
                ExecuteGuard.Apply(SettingsProvider.Load().Risk);   // off unless chosen in Options
                commands.AddCommand(new MenuCommand((s, e) => ShowAbout(), new CommandID(CommandSet, CmdAbout)));
                commands.AddCommand(new MenuCommand((s, e) => JoinableTaskFactory.RunAsync(() => UpdateService.CheckManuallyAsync(this, DisposalToken)).FileAndForget("SqlAssist/CheckUpdates"), new CommandID(CommandSet, CmdCheckUpdates)));
                GenerateScriptCommands.Register(commands);
            }
            JoinableTaskFactory.RunAsync(() => UpdateService.CheckAutomaticallyAsync(this, DisposalToken)).FileAndForget("SqlAssist/AutoCheckUpdates");
        }

        private static void ShowSnippetManager()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var window = new SnippetManagerWindow();
                // Owned by the SSMS main window: stays on top of it and is centred over it.
                if (GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsUIShell)) is Microsoft.VisualStudio.Shell.Interop.IVsUIShell shell &&
                    shell.GetDialogOwnerHwnd(out IntPtr owner) == 0)
                    new System.Windows.Interop.WindowInteropHelper(window).Owner = owner;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error("Snippets Manager", ex);
                OpenFile(SnippetProvider.FilePath); // the old behaviour, so snippets stay editable either way
            }
        }

        private static void ShowOptions()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                DialogHost.ShowDialog(new OptionsWindow());
            }
            catch (Exception ex)
            {
                Log.Error("Options", ex);
                ScriptDelivery.Show("เปิดหน้าต่าง Options ไม่สำเร็จ: " + ex.GetType().Name + " (ดู log ที่ " + Log.FilePath + ")", "SqlAssist: Options");
            }
        }

        private static void ShowCsvToSql()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            const string title = "SqlAssist: CSV to SQL";
            try
            {
                var window = new ClipboardSqlWindow();
                if (DialogHost.ShowDialog(window) == true)
                    ScriptDelivery.Send(window.Script, window.Target, window.What, title);
            }
            catch (Exception ex)
            {
                Log.Error("CSV to SQL", ex);
                ScriptDelivery.Show("CSV to SQL failed: " + ex.GetType().Name + " (see the log: " + Log.FilePath + ")", title);
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
