namespace SqlAssist.Core.Schema
{
    public enum AuthKind { Windows, Sql, Entra }

    /// <summary>The keys the schema, search and usage caches are filed under: one per server + login (+ database). Windows and SQL
    /// logins keep the keys they always had, so caches and saved usage stay valid; an Entra ID login gets its own "entra:" prefix,
    /// so it never shares an entry with Windows authentication on the same server or with another Entra account.</summary>
    public static class ConnectionKey
    {
        public static string ForDatabase(string server, string database, AuthKind auth, string user) =>
            (server + "|" + database + "|" + Login(auth, user)).ToLowerInvariant();

        public static string ForServer(string server, AuthKind auth, string user) =>
            (server + "|" + Login(auth, user)).ToLowerInvariant();

        private static string Login(AuthKind auth, string user)
        {
            switch (auth)
            {
                case AuthKind.Windows: return "windows";
                case AuthKind.Entra: return "entra:" + user;
                default: return user;
            }
        }
    }
}
