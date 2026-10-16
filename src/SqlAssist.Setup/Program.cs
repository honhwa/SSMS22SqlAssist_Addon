using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SqlAssist.Setup
{
    /// <summary>Installs or removes SqlAssist in SSMS 22.
    /// Double-click: asks Install / Uninstall. Command line: /install, /uninstall, /quiet (no prompts).</summary>
    internal static class Program
    {
        private const string VsixId = "SqlAssist.9cc4aed8-f2ad-4120-ba6f-222b08aa04a0";
        private const string DefaultSsmsIde = @"C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE";

        private static bool _quiet;

        /// <summary>From Directory.Build.props — the same number the embedded .vsix carries.</summary>
        private static readonly string SetupVersion = typeof(Program).Assembly.GetName().Version.ToString(3);

        [STAThread] // required for MessageBox
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "SqlAssist Setup";
            _quiet = args.Any(a => Is(a, "quiet"));
            try
            {
                Console.WriteLine($"SqlAssist {SetupVersion} for SSMS 22 - setup");
                Console.WriteLine("==================================");
                Console.WriteLine();

                string ide = FindSsmsIde();
                if (ide == null)
                    return Fail("SSMS 22 was not found on this machine.\nไม่พบ SSMS 22 ในเครื่องนี้");
                Console.WriteLine("SSMS 22: " + ide);

                string installed = FindInstalledVersion(ide);
                Console.WriteLine("Installed / เวอร์ชันที่ติดตั้งอยู่ : " + (installed ?? "(not installed / ยังไม่ได้ติดตั้ง)"));
                Console.WriteLine("This setup / เวอร์ชันในตัวติดตั้งนี้ : " + SetupVersion);
                if (installed != null && System.Version.TryParse(installed, out var iv) && System.Version.TryParse(SetupVersion, out var sv))
                {
                    Console.ForegroundColor = iv < sv ? ConsoleColor.Cyan : ConsoleColor.DarkGray;
                    Console.WriteLine(iv < sv ? "  -> Update available / มีเวอร์ชันใหม่กว่า"
                                    : iv == sv ? "  -> Already up to date / เป็นเวอร์ชันล่าสุดแล้ว (ติดตั้งซ้ำได้)"
                                    : "  -> Installed version is newer / เวอร์ชันที่ติดตั้งใหม่กว่าตัวติดตั้งนี้");
                    Console.ResetColor();
                }
                Console.WriteLine();

                // Checked up front so nobody picks an option only to be told to close SSMS afterwards.
                if (!WaitForSsmsToClose()) return Fail("Cancelled: SSMS is still running. / ยกเลิก: SSMS ยังเปิดอยู่");

                bool uninstall = args.Any(a => Is(a, "uninstall"));
                bool install = args.Any(a => Is(a, "install"));
                if (!install && !uninstall)
                {
                    Console.WriteLine("  [1] Install / ติดตั้ง");
                    Console.WriteLine("  [2] Uninstall / ถอนการติดตั้ง");
                    Console.Write("\nChoose 1 or 2 / เลือก 1 หรือ 2: ");
                    string choice = Console.ReadLine()?.Trim();
                    if (choice == "1") install = true;
                    else if (choice == "2") uninstall = true;
                    else return Fail("Cancelled. / ยกเลิก");
                    Console.WriteLine();
                }

                // Again: SSMS may have been reopened while the menu was waiting.
                if (!WaitForSsmsToClose()) return Fail("Cancelled: SSMS is still running. / ยกเลิก: SSMS ยังเปิดอยู่");

                int code = uninstall ? Uninstall(ide) : Install(ide);
                if (code != 0) return code;

                ClearComponentCache();
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(uninstall
                    ? "SqlAssist removed. / ถอนการติดตั้งเรียบร้อย"
                    : $"SqlAssist {SetupVersion} installed. Check it in SSMS: Tools > SqlAssist: About\n" +
                      $"ติดตั้ง SqlAssist {SetupVersion} เรียบร้อย ตรวจสอบได้ใน SSMS: Tools > SqlAssist: About");
                Console.ResetColor();
                if (!uninstall) PrintReminders(ide);
                Pause();
                return 0;
            }
            catch (Exception ex)
            {
                return Fail("Unexpected error / เกิดข้อผิดพลาด:\n" + ex);
            }
        }

        private static int Install(string ide)
        {
            string vsix = Path.Combine(Path.GetTempPath(), "SqlAssist-" + Guid.NewGuid().ToString("N") + ".vsix");
            try
            {
                using (var res = typeof(Program).Assembly.GetManifestResourceStream("SqlAssist.vsix"))
                using (var file = File.Create(vsix))
                    res.CopyTo(file);

                Console.WriteLine("Installing... / กำลังติดตั้ง...");
                // Removing first makes reinstall/upgrade of the same version work instead of failing as "already installed".
                RunInstaller(ide, "/quiet /uninstall:" + VsixId, allowFailure: true);
                return RunInstaller(ide, "/quiet \"" + vsix + "\"", allowFailure: false);
            }
            finally
            {
                try { File.Delete(vsix); } catch { /* temp file; Windows cleans it up eventually */ }
            }
        }

        private static int Uninstall(string ide)
        {
            Console.WriteLine("Uninstalling... / กำลังถอนการติดตั้ง...");
            return RunInstaller(ide, "/quiet /uninstall:" + VsixId, allowFailure: false);
        }

        // SSMS's own VSIXInstaller: it targets the SSMS instance, where Visual Studio's would not.
        private static int RunInstaller(string ide, string arguments, bool allowFailure)
        {
            var psi = new ProcessStartInfo(Path.Combine(ide, "VSIXInstaller.exe"), arguments) { UseShellExecute = false };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                if (p.ExitCode == 0 || allowFailure) return 0;
                return Fail($"VSIXInstaller failed (exit code {p.ExitCode}). Its log is in %TEMP% (dd_VSIXInstaller_*.log).\n" +
                            "ติดตั้งไม่สำเร็จ ดู log ได้ที่ %TEMP%\\dd_VSIXInstaller_*.log");
            }
        }

        private static string FindSsmsIde()
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

        /// <summary>SSMS keeps the extension files and its component cache locked while running, so the install
        /// cannot be done under it. Pops a warning (Retry / Cancel) until every SSMS window is closed.</summary>
        /// <summary>Reads the version from the installed copy's extension.vsixmanifest: per-user installs live under
        /// %LOCALAPPDATA%\Microsoft\SSMS\22.0_*\Extensions, machine-wide ones under the SSMS folder.</summary>
        private static string FindInstalledVersion(string ide)
        {
            var roots = new System.Collections.Generic.List<string> { Path.Combine(ide, "Extensions") };
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\SSMS");
            if (Directory.Exists(local))
                roots.AddRange(Directory.GetDirectories(local, "22.0_*").Select(d => Path.Combine(d, "Extensions")));

            foreach (var root in roots.Where(Directory.Exists))
            {
                foreach (var manifest in SafeFiles(root))
                {
                    try
                    {
                        var doc = System.Xml.Linq.XDocument.Load(manifest);
                        var identity = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity");
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

        private static System.Collections.Generic.IEnumerable<string> SafeFiles(string root)
        {
            try { return Directory.GetFiles(root, "extension.vsixmanifest", SearchOption.AllDirectories); }
            catch { return Enumerable.Empty<string>(); }
        }

        private static bool WaitForSsmsToClose()
        {
            while (true)
            {
                int running = Process.GetProcessesByName("Ssms").Length;
                if (running == 0) return true;
                if (_quiet)
                {
                    Console.WriteLine("SSMS is running; close it and run again. / SSMS เปิดอยู่ ปิดก่อนแล้วรันใหม่");
                    return false;
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"SSMS is open ({running} window(s)). Waiting for it to close... / SSMS เปิดอยู่ {running} หน้าต่าง");
                Console.ResetColor();

                var answer = MessageBox.Show(
                    "SQL Server Management Studio (SSMS) เปิดอยู่ " + running + " หน้าต่าง\n" +
                    "กรุณาบันทึกงานแล้วปิด SSMS ทุกหน้าต่างก่อน จากนั้นกด Retry\n\n" +
                    "SSMS is running (" + running + " window(s)).\n" +
                    "Save your work, close every SSMS window, then click Retry.",
                    "SqlAssist Setup - กรุณาปิด SSMS",
                    MessageBoxButtons.RetryCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.DefaultDesktopOnly); // shows on top of the console window
                if (answer != DialogResult.Retry) return false;
            }
        }

        // SSMS caches its MEF parts; without clearing, a new or removed extension may not be noticed.
        private static void ClearComponentCache()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\SSMS");
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root, "22.0_*"))
            {
                string cache = Path.Combine(dir, "ComponentModelCache");
                if (!Directory.Exists(cache)) continue;
                foreach (var f in Directory.GetFiles(cache)) TryDelete(() => File.Delete(f));
                foreach (var d in Directory.GetDirectories(cache)) TryDelete(() => Directory.Delete(d, true));
            }
        }

        private static void PrintReminders(string ide)
        {
            Console.WriteLine();
            Console.WriteLine("Before first use / ก่อนใช้งานครั้งแรก:");
            Console.WriteLine("  - Turn off SSMS IntelliSense: Tools > Options > Text Editor > Transact-SQL > IntelliSense");
            Console.WriteLine("    ปิด IntelliSense ของ SSMS: Tools > Options > Text Editor > Transact-SQL > IntelliSense");
            if (Directory.Exists(Path.Combine(ide, @"Extensions\Devart")))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  - dbForge SQL Complete is installed; uninstall or disable it, it also takes the Tab key.");
                Console.WriteLine("    พบ dbForge SQL Complete ให้ถอนหรือปิดก่อน เพราะแย่งปุ่ม Tab กัน");
                Console.ResetColor();
            }
        }

        private static int Fail(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine(message);
            Console.ResetColor();
            Pause();
            return 1;
        }

        private static void Pause()
        {
            if (_quiet) return;
            Console.WriteLine();
            Console.Write("Press Enter to close. / กด Enter เพื่อปิด");
            Console.ReadLine();
        }

        private static bool Is(string arg, string name) =>
            Regex.IsMatch(arg, "^[-/]{1,2}" + name + "$", RegexOptions.IgnoreCase);

        private static void TryDelete(Action delete)
        {
            try { delete(); } catch { /* locked or already gone */ }
        }
    }
}
