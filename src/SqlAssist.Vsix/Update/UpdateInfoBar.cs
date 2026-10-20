using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlAssist.Core.Update;

namespace SqlAssist.Update
{
    /// <summary>The yellow bar across the top of the SSMS main window that says a new SqlAssist is out.</summary>
    internal sealed class UpdateInfoBar : IVsInfoBarUIEvents
    {
        private const string Install = "install", InstallOnExit = "onexit", Notes = "notes", Skip = "skip", Never = "never";
        private static UpdateInfoBar _current;

        private readonly AsyncPackage _package;
        private readonly ReleaseInfo _release;
        private IVsInfoBarUIElement _element;
        private uint _cookie;

        private UpdateInfoBar(AsyncPackage package, ReleaseInfo release)
        {
            _package = package;
            _release = release;
        }

        /// <summary>False when SSMS has no InfoBar host to put it in.</summary>
        public static bool Show(AsyncPackage package, ReleaseInfo release)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                _current?.Close(); // a manual check while the bar is up replaces it rather than stacking a second one
                if (!(Package.GetGlobalService(typeof(SVsShell)) is IVsShell shell) ||
                    shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out object hostObject) != 0 ||
                    !(hostObject is IVsInfoBarHost host) ||
                    !(Package.GetGlobalService(typeof(SVsInfoBarUIFactory)) is IVsInfoBarUIFactory factory))
                    return false;

                var actions = new List<IVsInfoBarActionItem>();
                if (release.SetupUrl != null)
                {
                    // Download now, install by itself once SSMS is closed: no setup window to come back to.
                    actions.Add(new InfoBarButton("Install when SSMS closes", InstallOnExit));
                    actions.Add(new InfoBarButton("Install now", Install));
                }
                else actions.Add(new InfoBarButton("Open download page", Install));
                actions.Add(new InfoBarHyperlink("What's new", Notes));
                actions.Add(new InfoBarHyperlink("Skip this version", Skip));
                actions.Add(new InfoBarHyperlink("Don't check automatically", Never));

                var model = new InfoBarModel(
                    new[] { new InfoBarTextSpan("SqlAssist " + release.Version + " is available (installed: " + SqlAssistPackage.Version + ").  ") },
                    actions,
                    KnownMonikers.StatusInformation,
                    isCloseButtonVisible: true);

                var bar = new UpdateInfoBar(package, release) { _element = factory.CreateInfoBar(model) };
                bar._element.Advise(bar, out bar._cookie);
                host.AddInfoBar(bar._element);
                _current = bar;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Update InfoBar", ex);
                return false;
            }
        }

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                switch (actionItem.ActionContext as string)
                {
                    case Install:
                        Close();
                        _package.JoinableTaskFactory.RunAsync(() => UpdateService.DownloadAndRunAsync(_package, _release))
                            .FileAndForget("SqlAssist/DownloadUpdate");
                        break;
                    case InstallOnExit:
                        Close();
                        _package.JoinableTaskFactory.RunAsync(() => UpdateService.DownloadAndInstallOnExitAsync(_package, _release))
                            .FileAndForget("SqlAssist/DownloadUpdateOnExit");
                        break;
                    case Notes:
                        UpdateService.OpenUrl(_release.PageUrl);
                        break;
                    case Skip:
                        UpdateService.SkipVersion(_release.Version);
                        Close();
                        break;
                    case Never:
                        UpdateService.DisableAutomaticCheck();
                        Close();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Update InfoBar action", ex);
            }
        }

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_element != null && _cookie != 0) _element.Unadvise(_cookie);
            _cookie = 0;
            _element = null;
            if (_current == this) _current = null;
        }

        private void Close()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _element?.Close(); // raises OnClosed
        }
    }
}
