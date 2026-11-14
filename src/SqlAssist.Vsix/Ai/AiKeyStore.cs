using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SqlAssist.Ai
{
    /// <summary>The Explain SQL API key, in %LOCALAPPDATA%\SqlAssist\ai.key, encrypted with DPAPI for the current Windows user: another
    /// user, or the file copied to another machine, cannot read it. Not in settings.xml, and never written to the log.</summary>
    internal static class AiKeyStore
    {
        public static readonly string FilePath = Path.Combine(Log.Directory, "ai.key");
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SqlAssist.Ai.Key");

        public static bool HasKey
        {
            get
            {
                try { return File.Exists(FilePath); }
                catch (Exception ex) { Log.Error("AI key: check", ex); return false; }
            }
        }

        /// <summary>Null when there is no key or it cannot be decrypted (another user's file, a damaged file).</summary>
        public static string Read()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
                string key = Encoding.UTF8.GetString(plain);
                return string.IsNullOrWhiteSpace(key) ? null : key;
            }
            catch (Exception ex)
            {
                Log.Error("AI key: read", ex);
                return null;
            }
        }

        /// <summary>An empty key deletes the file. Throws when the file cannot be written, so the Options window can say so.</summary>
        public static void Save(string key)
        {
            key = key?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                return;
            }
            Directory.CreateDirectory(Log.Directory);
            byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, cipher);
        }
    }
}
