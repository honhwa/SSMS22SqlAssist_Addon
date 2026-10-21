using System;
using System.IO;

namespace SqlAssist
{
    /// <summary>Plain-text log under %LOCALAPPDATA%\SqlAssist. Every failure inside SSMS has to be swallowed,
    /// so this is the only place they become visible (Tools > SqlAssist > Options... > the Open Log button).</summary>
    internal static class Log
    {
        private const long MaxBytes = 1024 * 1024;
        private static readonly object Gate = new object();

        public static readonly string Directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlAssist");
        public static readonly string FilePath = Path.Combine(Directory, "SqlAssist.log");

        public static void Info(string message) => Write("INFO ", message);

        public static void Error(string message, Exception ex) => Write("ERROR", message + ": " + ex);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    var file = new FileInfo(FilePath);
                    if (file.Exists && file.Length > MaxBytes)
                    {
                        File.Copy(FilePath, FilePath + ".old", overwrite: true);
                        File.Delete(FilePath);
                    }
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging must never take the editor down with it.
            }
        }
    }
}
