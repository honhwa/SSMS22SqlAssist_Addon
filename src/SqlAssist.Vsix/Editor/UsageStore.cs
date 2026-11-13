using System;
using System.IO;
using System.Text;
using System.Threading;
using SqlAssist.Core.Completion;
using SqlAssist.Settings;

namespace SqlAssist.Editor
{
    /// <summary>The session's <see cref="UsageStats"/>, kept in %LOCALAPPDATA%\SqlAssist\usage.txt. Read once, on first use; written
    /// a few seconds after the last commit, on a pool thread. Two SSMS windows each write what they hold, so the last one to save
    /// wins: the history only orders lists, so losing some of it is harmless. Nothing here may throw into the editor, and the
    /// names it keeps (tables, columns) never go to the log.</summary>
    internal static class UsageStore
    {
        public static readonly string FilePath = Path.Combine(Log.Directory, "usage.txt");
        private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);
        private static readonly object Gate = new object();
        private static UsageStats _stats;
        private static Timer _saveTimer;

        /// <summary>Off in Options means no bonus and no recording; the file is left as it was.</summary>
        public static bool Enabled
        {
            get
            {
                try { return SettingsProvider.LearnFromUsage; }
                catch (Exception ex) { Log.Error("Usage setting", ex); return false; }
            }
        }

        public static int Bonus(string key)
        {
            if (key == null) return 0;
            try
            {
                lock (Gate) return Stats().Bonus(key, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                Log.Error("Usage bonus", ex);
                return 0;
            }
        }

        public static void Record(string key)
        {
            if (key == null || !Enabled) return;
            try
            {
                lock (Gate)
                {
                    Stats().Record(key, DateTime.UtcNow);
                    if (_saveTimer == null) _saveTimer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
                    _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Usage record", ex);
            }
        }

        /// <summary>Options › Clear: forgets everything, here and in the file.</summary>
        public static void Clear()
        {
            lock (Gate)
            {
                _stats = new UsageStats();
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
        }

        private static UsageStats Stats()
        {
            if (_stats != null) return _stats;
            try
            {
                _stats = UsageStats.Parse(File.Exists(FilePath) ? File.ReadAllText(FilePath) : null);
            }
            catch (Exception ex)
            {
                // Unreadable (another SSMS writing it): start empty; this session's saves replace it later.
                Log.Error("Reading usage history", ex);
                _stats = new UsageStats();
            }
            return _stats;
        }

        private static void Save()
        {
            try
            {
                string text;
                lock (Gate)
                {
                    if (_stats == null || !_stats.IsDirty) return;
                    text = _stats.Serialize();
                }
                Directory.CreateDirectory(Log.Directory);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);
            }
            catch (Exception ex)
            {
                Log.Error("Saving usage history", ex);
            }
        }
    }
}
