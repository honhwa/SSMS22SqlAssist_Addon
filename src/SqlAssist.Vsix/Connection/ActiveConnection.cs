using System;
using System.Data.SqlClient;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.VisualStudio.Shell;

namespace SqlAssist.Connection
{
    internal sealed class ConnectionTarget
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string ConnectionString { get; set; }
        /// <summary>Identifies one schema: same server, database and login share a cache entry.</summary>
        public string Key { get; set; }
        public override string ToString() => Server + "/" + (Database ?? "(default)");
    }

    /// <summary>Reads the active query window's connection from SSMS. There is no public API for this, so it goes
    /// through SQLEditors' ScriptFactory by reflection — the same route other SSMS 22 extensions use — and every
    /// step tolerates the member being gone in a later SSMS build.</summary>
    internal static class ActiveConnection
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static string _lastProblem;

        /// <summary>Null when there is no connected query window (or it uses Entra ID, which v1 does not support).</summary>
        public static ConnectionTarget TryGet()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var asm = FindAssembly("SQLEditors");
                var factoryType = asm?.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptFactory");
                if (factoryType == null) return Problem("ScriptFactory type not found in SQLEditors");
                object factory = factoryType.GetProperty("Instance", Any)?.GetValue(null);
                object wnd = factory == null ? null : factoryType.GetProperty("CurrentlyActiveWndConnectionInfo", Any)?.GetValue(factory);
                object info = Get(wnd, "UIConnectionInfo");
                if (info == null) return null; // not connected — normal, not worth logging

                string server = Get(info, "ServerName") as string;
                if (string.IsNullOrEmpty(server)) return null;
                if (Get(info, "RenewableToken") != null)
                    return Problem("Connection to " + server + " uses Entra ID authentication, which SqlAssist does not support yet");

                string database = GetAdvancedOption(info, "DATABASE");
                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = server,
                    ApplicationName = "SqlAssist",
                    ConnectTimeout = 10,
                    Encrypt = true,
                    TrustServerCertificate = true,
                };
                if (!string.IsNullOrEmpty(database)) builder.InitialCatalog = database;

                string user = Get(info, "UserName") as string;
                string password = Get(info, "Password") as string;
                if (string.IsNullOrEmpty(password)) password = Read(Get(info, "InMemoryPassword") as SecureString);
                if (!string.IsNullOrEmpty(password))
                {
                    builder.UserID = user ?? "";
                    builder.Password = password;
                }
                else
                {
                    builder.IntegratedSecurity = true;
                }

                _lastProblem = null;
                return new ConnectionTarget
                {
                    Server = server,
                    Database = database,
                    ConnectionString = builder.ConnectionString,
                    Key = (server + "|" + database + "|" + (builder.IntegratedSecurity ? "windows" : user)).ToLowerInvariant(),
                };
            }
            catch (Exception ex)
            {
                return Problem("Reading the active connection failed: " + ex.Message);
            }
        }

        private static ConnectionTarget Problem(string message)
        {
            if (message != _lastProblem) Log.Info(message);
            _lastProblem = message;
            return null;
        }

        private static Assembly FindAssembly(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(asm.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) return asm;
            return null;
        }

        private static object Get(object target, string property)
        {
            if (target == null) return null;
            try { return target.GetType().GetProperty(property, Any)?.GetValue(target); }
            catch { return null; }
        }

        // UIConnectionInfo.AdvancedOptions is a NameValueCollection-like bag; "DATABASE" follows the editor's database.
        private static string GetAdvancedOption(object info, string key)
        {
            object options = Get(info, "AdvancedOptions");
            if (options == null) return null;
            try
            {
                var indexer = options.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public, null, null, new[] { typeof(string) }, null);
                return indexer?.GetValue(options, new object[] { key }) as string;
            }
            catch { return null; }
        }

        private static string Read(SecureString secure)
        {
            if (secure == null || secure.Length == 0) return null;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(secure);
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                if (ptr != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }
    }
}
