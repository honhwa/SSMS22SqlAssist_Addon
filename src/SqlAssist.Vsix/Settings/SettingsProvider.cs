using System;
using System.IO;
using SqlAssist.Core.Settings;

namespace SqlAssist.Settings
{
    /// <summary>The user's settings.xml under %APPDATA%\SqlAssist, re-read whenever the file changes (so editing it by hand works).</summary>
    internal static class SettingsProvider
    {
        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SqlAssist", SqlAssistSettings.FileName);

        private static readonly object Gate = new object();
        private static SqlAssistSettings _current;
        private static DateTime _loadedWriteTime;

        /// <summary>A copy: changing it does nothing until it is passed to <see cref="Save"/>.</summary>
        public static SqlAssistSettings Load()
        {
            lock (Gate)
            {
                try
                {
                    DateTime writeTime = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
                    if (_current == null || writeTime != _loadedWriteTime)
                    {
                        _current = SqlAssistSettings.Parse(writeTime == DateTime.MinValue ? null : File.ReadAllText(FilePath));
                        _loadedWriteTime = writeTime;
                    }
                }
                catch (Exception ex)
                {
                    // A file that cannot be read means the defaults, not a failing command.
                    Log.Error("Reading " + FilePath, ex);
                    if (_current == null) _current = new SqlAssistSettings();
                }
                return SqlAssistSettings.Parse(_current.ToXml());
            }
        }

        public static void Save(SqlAssistSettings settings)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string xml = settings.ToXml();
                File.WriteAllText(FilePath, xml, new System.Text.UTF8Encoding(false));
                _current = SqlAssistSettings.Parse(xml);
                _loadedWriteTime = File.GetLastWriteTimeUtc(FilePath);
            }
        }
    }
}
