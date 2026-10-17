using System;
using System.IO;
using SqlAssist.Core.Snippets;

namespace SqlAssist.Snippets
{
    /// <summary>The user's snippets.xml under %APPDATA%\SqlAssist, re-read whenever the file changes.</summary>
    internal static class SnippetProvider
    {
        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SqlAssist", "snippets.xml");

        private static readonly object Gate = new object();
        private static SnippetStore _store;
        private static DateTime _loadedWriteTime;
        private static DateTime _lastCheckUtc;

        public static SnippetStore Current
        {
            get
            {
                lock (Gate)
                {
                    // A stat per keystroke is cheap, but there is no need for more than one every two seconds.
                    if (_store != null && DateTime.UtcNow - _lastCheckUtc < TimeSpan.FromSeconds(2)) return _store;
                    _lastCheckUtc = DateTime.UtcNow;
                    try
                    {
                        EnsureFileExists();
                        var writeTime = File.GetLastWriteTimeUtc(FilePath);
                        if (_store == null || writeTime != _loadedWriteTime)
                        {
                            _store = SnippetStore.Parse(File.ReadAllText(FilePath));
                            _loadedWriteTime = writeTime;
                            Log.Info($"Loaded {_store.All.Count} snippets from {FilePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // A typo in the user's file keeps the previous snippets rather than losing them all.
                        Log.Error("Reading " + FilePath, ex);
                        if (_store == null) _store = SnippetStore.Parse(SnippetStore.DefaultXml);
                        _loadedWriteTime = SafeWriteTime();
                    }
                    return _store;
                }
            }
        }

        /// <summary>Writes the snippets (from the Snippets Manager) and makes them current straight away,
        /// without waiting for the two-second file check.</summary>
        public static void Save(System.Collections.Generic.IEnumerable<Snippet> snippets)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string xml = SnippetStore.ToXml(snippets);
                File.WriteAllText(FilePath, xml, new System.Text.UTF8Encoding(false));
                _store = SnippetStore.Parse(xml);
                _loadedWriteTime = File.GetLastWriteTimeUtc(FilePath);
                _lastCheckUtc = DateTime.UtcNow;
                Log.Info($"Saved {_store.All.Count} snippets to {FilePath}");
            }
        }

        public static void EnsureFileExists()
        {
            if (File.Exists(FilePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, SnippetStore.DefaultXml);
        }

        private static DateTime SafeWriteTime()
        {
            try { return File.GetLastWriteTimeUtc(FilePath); } catch { return DateTime.MinValue; }
        }
    }
}
