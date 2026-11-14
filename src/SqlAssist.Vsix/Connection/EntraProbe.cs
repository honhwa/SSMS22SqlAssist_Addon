using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SqlAssist.Connection
{
    /// <summary>TEMPORARY (Entra ID plan, Task 0): writes to the log what SSMS's RenewableToken looks like in this build, once per session,
    /// so Task 3 can be written against real member names. Logs type and member names, the length of what token-looking members return
    /// (never the value), whether they can be called off the UI thread, and expiry / resource / tenant values, which are not secrets.
    /// Remove once the result is recorded in docs/superpowers/plans/2026-11-14-entra-id-auth.md.</summary>
    internal static class EntraProbe
    {
        private const BindingFlags Public = BindingFlags.Instance | BindingFlags.Public;
        private static bool _done;

        public static void Describe(object token, object uiConnectionInfo)
        {
            if (_done || token == null) return;
            _done = true;
            try
            {
                var type = token.GetType();
                var sb = new StringBuilder("Entra probe: RenewableToken is ").Append(type.FullName);
                sb.Append("; interfaces: ").Append(string.Join(", ", type.GetInterfaces().Select(i => i.FullName)));
                sb.Append("; properties: ").Append(string.Join(", ", type.GetProperties(Public).Select(p => p.Name + ":" + p.PropertyType.Name)));
                sb.Append("; methods: ").Append(string.Join(", ", type.GetMethods(Public).Where(m => m.DeclaringType != typeof(object) && !m.IsSpecialName)
                    .Select(m => m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + "):" + m.ReturnType.Name)));
                foreach (var p in type.GetProperties(Public))
                {
                    if (p.GetIndexParameters().Length != 0) continue;
                    string n = p.Name;
                    if (n.IndexOf("Expir", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Resource", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Tenant", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Authority", StringComparison.OrdinalIgnoreCase) >= 0)
                        sb.Append("; ").Append(n).Append('=').Append(Safe(() => p.GetValue(token)?.ToString()));
                }
                string user = Safe(() => uiConnectionInfo?.GetType().GetProperty("UserName", Public)?.GetValue(uiConnectionInfo) as string);
                sb.Append("; UIConnectionInfo.UserName: length ").Append(user?.Length ?? -1).Append(user != null && user.Contains("@") ? ", has @" : "");
                Log.Info(sb.ToString());

                foreach (var call in TokenCalls(token))
                {
                    Log.Info("Entra probe: " + call.name + " on UI thread -> " + Measure(call.invoke));
                    var invoke = call.invoke;
                    string name = call.name;
                    Task.Run(() => Log.Info("Entra probe: " + name + " on thread pool -> " + Measure(invoke)));
                }
            }
            catch (Exception ex)
            {
                Log.Error("Entra probe", ex);
            }
        }

        /// <summary>Parameterless members whose name has "Token" in it and that return a string.</summary>
        private static (string name, Func<object> invoke)[] TokenCalls(object token)
        {
            var type = token.GetType();
            var methods = type.GetMethods(Public)
                .Where(m => m.GetParameters().Length == 0 && m.ReturnType == typeof(string) && m.Name.IndexOf("Token", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(m => (m.Name + "()", (Func<object>)(() => m.Invoke(token, null))));
            var properties = type.GetProperties(Public)
                .Where(p => p.GetIndexParameters().Length == 0 && p.PropertyType == typeof(string) && p.Name.IndexOf("Token", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => (p.Name, (Func<object>)(() => p.GetValue(token))));
            return methods.Concat(properties).ToArray();
        }

        private static string Measure(Func<object> invoke)
        {
            try
            {
                var value = invoke() as string;
                return value == null ? "null" : "string of length " + value.Length + (value.StartsWith("eyJ", StringComparison.Ordinal) ? " (JWT)" : "");
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                return inner.GetType().FullName + ": " + inner.Message;
            }
        }

        private static string Safe(Func<string> read)
        {
            try { return read(); }
            catch (Exception ex) { return "(" + ex.GetType().Name + ")"; }
        }
    }
}
