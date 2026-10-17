using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SqlAssist.Setup
{
    /// <summary>Everything the installer does, without any UI: shared by the window and by /quiet command-line runs.</summary>
    internal static class SetupEngine
    {
        public const string VsixId = "SqlAssist.9cc4aed8-f2ad-4120-ba6f-222b08aa04a0";
        private const string DefaultSsmsIde = @"C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE";

        /// <summary>From Directory.Build.props — the same number the embedded .vsix carries.</summary>
        public static readonly string SetupVersion = typeof(SetupEngine).Assembly.GetName().Version.ToString(3);

        public static int RunningSsmsCount() => Process.GetProcessesByName("Ssms").Length;

        public static string FindSsmsIde()
        {
            // vswhere lists SSMS 22 too, since it installs through the Visual Studio Installer.
            string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft Visual Studio\Installer\vswhere.exe");
            if (File.Exists(vswhere))
            {
                try
                {
                    var psi = new ProcessStartInfo(vswhere, "-all -prerelease -products * -property installationPath")
                    {
                        UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
                    };
                    using (var p = Process.Start(psi))
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string ide = Path.Combine(line.Trim(), @"Common7\IDE");
                            if (line.IndexOf("Management Studio 22", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                File.Exists(Path.Combine(ide, "Ssms.exe")))
                                return ide;
                        }
                    }
                }
                catch
                {
                    // Fall back to the default location.
                }
            }
            return File.Exists(Path.Combine(DefaultSsmsIde, "Ssms.exe")) ? DefaultSsmsIde : null;
        }

        /// <summary>Reads the version from the installed copy's extension.vsixmanifest: per-user installs live under
        /// %LOCALAPPDATA%\Microsoft\SSMS\22.0_*\Extensions, machine-wide ones under the SSMS folder.</summary>
        public static string FindInstalledVersion(string ide)
        {
            var roots = new List<string> { Path.Combine(ide, "Extensions") };
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\SSMS");
            if (Directory.Exists(local))
                roots.AddRange(Directory.GetDirectories(local, "22.0_*").Select(d => Path.Combine(d, "Extensions")));

            foreach (var root in roots.Where(Directory.Exists))
            {
                foreach (var manifest in SafeFiles(root))
                {
                    try
                    {
                        var identity = XDocument.Load(manifest).Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity");
                        if ((string)identity?.Attribute("Id") == VsixId) return (string)identity.Attribute("Version");
                    }
                    catch
                    {
                        // Unreadable manifest of some other extension — skip it.
                    }
                }
            }
            return null;
        }

        /// <summary>-1 installed is older, 0 same, 1 installed is newer; null when either is unknown.</summary>
        public static int? CompareInstalled(string installed)
        {
            if (installed == null || !Version.TryParse(installed, out var i) || !Version.TryParse(SetupVersion, out var s)) return null;
            return i.CompareTo(s) < 0 ? -1 : i.CompareTo(s) > 0 ? 1 : 0;
        }

        public static bool DbForgeInstalled(string ide) => Directory.Exists(Path.Combine(ide, @"Extensions\Devart"));

        /// <summary>One step of an install or uninstall, for the window to show while it runs.</summary>
        public struct Step
        {
            public Step(int number, int total, string text) { Number = number; Total = total; Text = text; }
            public int Number { get; }
            public int Total { get; }
            public string Text { get; }
        }

        /// <summary>Installs (or updates over) the embedded .vsix. Null on success, otherwise the error text.</summary>
        public static string Install(string ide, IProgress<Step> progress = null)
        {
            const int total = 4;
            string vsix = Path.Combine(Path.GetTempPath(), "SqlAssist-" + Guid.NewGuid().ToString("N") + ".vsix");
            try
            {
                progress?.Report(new Step(1, total, "เตรียมไฟล์ add-on"));
                using (var res = typeof(SetupEngine).Assembly.GetManifestResourceStream("SqlAssist.vsix"))
                using (var file = File.Create(vsix))
                    res.CopyTo(file);

                // Removing the old copy first makes updates and same-version reinstalls work,
                // instead of VSIXInstaller refusing with "already installed".
                progress?.Report(new Step(2, total, "ถอนเวอร์ชันเดิมออก (ถ้ามี)"));
                RunInstaller(ide, "/quiet /uninstall:" + VsixId);
                progress?.Report(new Step(3, total, "ติดตั้ง SqlAssist " + SetupVersion + " ลงใน SSMS"));
                int code = RunInstaller(ide, "/quiet \"" + vsix + "\"");
                if (code != 0) return InstallerError(code);
                progress?.Report(new Step(4, total, "ล้าง cache ของ SSMS"));
                ClearComponentCache();
                return null;
            }
            finally
            {
                try { File.Delete(vsix); } catch { /* temp file; Windows cleans it up eventually */ }
            }
        }

        public static string Uninstall(string ide, IProgress<Step> progress = null)
        {
            const int total = 2;
            progress?.Report(new Step(1, total, "ถอน SqlAssist ออกจาก SSMS"));
            int code = RunInstaller(ide, "/quiet /uninstall:" + VsixId);
            if (code != 0) return InstallerError(code);
            progress?.Report(new Step(2, total, "ล้าง cache ของ SSMS"));
            ClearComponentCache();
            return null;
        }

        private static string InstallerError(int code) =>
            $"VSIXInstaller exited with code {code}.\nดูรายละเอียดได้ที่ %TEMP%\\dd_VSIXInstaller_*.log";

        // SSMS's own VSIXInstaller: it targets the SSMS instance, where Visual Studio's would not.
        private static int RunInstaller(string ide, string arguments)
        {
            var psi = new ProcessStartInfo(Path.Combine(ide, "VSIXInstaller.exe"), arguments) { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        // SSMS caches its MEF parts; without clearing, a new or removed extension may not be noticed.
        // It also caches every extension's .imagemanifest in ImageLibrary.cache, so a changed toolbar icon
        // would keep showing the old image until that file is rebuilt.
        private static void ClearComponentCache()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\SSMS");
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root, "22.0_*"))
            {
                string images = Path.Combine(dir, @"ImageLibrary\ImageLibrary.cache");
                if (File.Exists(images)) TryDelete(() => File.Delete(images));

                string cache = Path.Combine(dir, "ComponentModelCache");
                if (!Directory.Exists(cache)) continue;
                foreach (var f in Directory.GetFiles(cache)) TryDelete(() => File.Delete(f));
                foreach (var d in Directory.GetDirectories(cache)) TryDelete(() => Directory.Delete(d, true));
            }
        }

        private static IEnumerable<string> SafeFiles(string root)
        {
            try { return Directory.GetFiles(root, "extension.vsixmanifest", SearchOption.AllDirectories); }
            catch { return Enumerable.Empty<string>(); }
        }

        private static void TryDelete(Action delete)
        {
            try { delete(); } catch { /* locked or already gone */ }
        }
    }
}
