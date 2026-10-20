using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using SqlAssist.Core.Update;
using Task = System.Threading.Tasks.Task;

namespace SqlAssist.Update
{
    /// <summary>Checks GitHub Releases for a newer SqlAssist, shows an InfoBar about it, and on request downloads the
    /// setup exe and starts it. Installing is left to the setup window: VSIXInstaller can't run while SSMS is open,
    /// and the window waits for the user to close SSMS before its Install button works.</summary>
    internal static class UpdateService
    {
        private static readonly string StatePath = Path.Combine(Log.Directory, "update.txt");
        private static readonly string DownloadDirectory = Path.Combine(Log.Directory, "updates");
        private static readonly object StateGate = new object();
        private static readonly Lazy<HttpClient> Http = new Lazy<HttpClient>(CreateClient);
        private static int _busy;

        public static Version CurrentVersion => new Version(SqlAssistPackage.Version);

        /// <summary>Startup check: at most once a day, silent unless there is something new.</summary>
        public static async Task CheckAutomaticallyAsync(AsyncPackage package, CancellationToken cancellationToken)
        {
            try
            {
                if (!UpdatePolicy.ShouldAutoCheck(LoadState(), DateTime.UtcNow)) return;
                // Let SSMS finish starting before going to the network.
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
                await CheckAsync(package, manual: false, cancellationToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Error("Automatic update check", ex); }
        }

        /// <summary>Tools > SqlAssist > Check for Updates: always answers, even when there is nothing new.</summary>
        public static Task CheckManuallyAsync(AsyncPackage package, CancellationToken cancellationToken) =>
            CheckAsync(package, manual: true, cancellationToken);

        private static async Task CheckAsync(AsyncPackage package, bool manual, CancellationToken cancellationToken)
        {
            ReleaseInfo release;
            try
            {
                await TaskScheduler.Default;
                release = await FetchLatestAsync(cancellationToken);
                ChangeState(s => s.LastCheckUtc = DateTime.UtcNow);
                Log.Info("Update check: latest release " + (release?.Tag ?? "(none)") + ", installed " + SqlAssistPackage.Version);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Log.Error("Update check", ex);
                if (manual) await ShowMessageAsync(package, "Could not check for updates:\n" + ex.Message, OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            if (UpdatePolicy.ShouldNotify(CurrentVersion, release, LoadState(), manual))
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                if (!UpdateInfoBar.Show(package, release) && manual)
                    OpenUrl(release.PageUrl); // no InfoBar host: the release page is the next best thing
            }
            else if (manual)
            {
                await ShowMessageAsync(package, "SqlAssist " + SqlAssistPackage.Version + " is the latest version.", OLEMSGICON.OLEMSGICON_INFO);
            }
        }

        /// <summary>Null when the repository has no published release yet.</summary>
        private static async Task<ReleaseInfo> FetchLatestAsync(CancellationToken cancellationToken)
        {
            using (var response = await Http.Value.GetAsync(ReleaseInfo.LatestApiUrl, cancellationToken))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("GitHub answered " + (int)response.StatusCode + " " + response.ReasonPhrase);
                return ReleaseInfo.Parse(await response.Content.ReadAsStringAsync());
            }
        }

        /// <summary>Downloads the release's setup exe, checks its SHA-256 against GitHub's digest and starts it.
        /// Falls back to the release page when there is no exe or no digest to check it against.</summary>
        public static async Task DownloadAndRunAsync(AsyncPackage package, ReleaseInfo release)
        {
            if (release.SetupUrl == null || release.SetupFileName == null || release.SetupSha256 == null)
            {
                Log.Info("Update " + release.Tag + ": no verifiable setup exe, opening the release page");
                OpenUrl(release.PageUrl);
                return;
            }
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                await SetStatusAsync(package, "SqlAssist: downloading " + release.SetupFileName + "...");
                await TaskScheduler.Default;
                string exe = await DownloadAsync(release, package.DisposalToken);
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = DownloadDirectory });
                Log.Info("Update " + release.Tag + ": started " + exe);
                await SetStatusAsync(package, "");
                await ShowMessageAsync(package,
                    "SqlAssist Setup " + release.Version + " is open.\n\nClose SSMS, then click Install / Update in the setup window.",
                    OLEMSGICON.OLEMSGICON_INFO);
            }
            catch (Exception ex)
            {
                Log.Error("Update download", ex);
                await SetStatusAsync(package, "");
                await ShowMessageAsync(package,
                    "Could not download the update:\n" + ex.Message + "\n\nThe release page will open so you can download it yourself.",
                    OLEMSGICON.OLEMSGICON_WARNING);
                OpenUrl(release.PageUrl);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private static async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(DownloadDirectory);
            string target = Path.Combine(DownloadDirectory, release.SetupFileName);
            string part = target + ".part";
            try
            {
                using (var response = await Http.Value.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    using (var source = await response.Content.ReadAsStreamAsync())
                    using (var file = File.Create(part))
                        await source.CopyToAsync(file, 81920, cancellationToken);
                }

                string actual = Sha256(part);
                if (!string.Equals(actual, release.SetupSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded file's SHA-256 does not match the release (" + actual + ").");

                if (File.Exists(target)) File.Delete(target);
                File.Move(part, target);
                return target;
            }
            finally
            {
                try { if (File.Exists(part)) File.Delete(part); } catch { }
            }
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static void SkipVersion(Version version) => ChangeState(s => s.SkippedVersion = version);

        public static void DisableAutomaticCheck() => ChangeState(s => s.AutoCheck = false);

        private static UpdateState LoadState()
        {
            lock (StateGate)
            {
                try { return File.Exists(StatePath) ? UpdateState.Parse(File.ReadAllText(StatePath)) : new UpdateState(); }
                catch (Exception ex) { Log.Error("Read " + StatePath, ex); return new UpdateState(); }
            }
        }

        private static void ChangeState(Action<UpdateState> change)
        {
            lock (StateGate)
            {
                try
                {
                    var state = LoadState();
                    change(state);
                    Directory.CreateDirectory(Log.Directory);
                    File.WriteAllText(StatePath, state.ToString());
                }
                catch (Exception ex) { Log.Error("Write " + StatePath, ex); }
            }
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Open " + url, ex); }
        }

        private static HttpClient CreateClient()
        {
            // SSMS runs on .NET Framework; make sure TLS 1.2 is on whatever the machine's defaults are.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SqlAssist/" + SqlAssistPackage.Version); // GitHub rejects requests without one
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        private static async Task ShowMessageAsync(AsyncPackage package, string text, OLEMSGICON icon)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            VsShellUtilities.ShowMessageBox(package, text, "SqlAssist Update", icon,
                OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        private static async Task SetStatusAsync(AsyncPackage package, string text)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                if (await package.GetServiceAsync(typeof(SVsStatusbar)) is IVsStatusbar status)
                {
                    await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                    status.SetText(text);
                }
            }
            catch (Exception ex) { Log.Error("Status bar", ex); }
        }
    }
}
