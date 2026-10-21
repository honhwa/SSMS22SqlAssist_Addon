using System;
using System.IO;

namespace SqlAssist.Setup
{
    /// <summary>Appends to the extension's own %LOCALAPPDATA%\SqlAssist\SqlAssist.log (same line format), so what the
    /// windowless /installonexit waiter does after SSMS has closed shows up in the log between two SSMS sessions.</summary>
    internal static class SetupLog
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlAssist", "SqlAssist.log");

        public static void Info(string message) => Write("INFO ", message);

        public static void Error(string message) => Write("ERROR", message);

        private static void Write(string level, string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never stop an install.
            }
        }
    }
}
