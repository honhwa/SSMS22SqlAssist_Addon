using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace SqlAssist.Connection
{
    /// <summary>TEMPORARY (Entra ID plan, Task 0): writes to the log what SSMS's RenewableToken looks like in this build, once per session,
    /// so Task 3 can be written against real member names. It runs from completion's path, so nothing here may block the UI thread:
    /// on the UI thread it only reads type and member names by reflection (no getter is run). Off the UI thread, with a time limit, it
    /// calls only the exact member names a token is expected under and logs the length of what they return (never the value), plus
    /// the values of expiry-like members whose type cannot hold a token (dates, numbers, Guids). Exception messages are not logged
    /// (MSAL's carry the user's UPN); only the exception type and HResult.
    /// Remove once the result is recorded in docs/superpowers/plans/2026-11-14-entra-id-auth.md.</summary>
    internal static class EntraProbe
    {
        private const BindingFlags Public = BindingFlags.Instance | BindingFlags.Public;
        private static readonly TimeSpan CallLimit = TimeSpan.FromSeconds(5);
        /// <summary>The only members that are ever invoked: names a plain "give me the current token" accessor would have.</summary>
        private static readonly string[] TokenMembers = { "GetAccessToken", "AccessToken", "Token" };
        private static bool _done;

        public static void Describe(object token)
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
                Log.Info(sb.ToString());

                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await TaskScheduler.Default;
                    try
                    {
                        foreach (var p in type.GetProperties(Public).Where(IsHarmlessValue))
                            Log.Info("Entra probe: " + p.Name + " = " + await LimitedAsync(() => p.GetValue(token)?.ToString() ?? "null"));
                        foreach (var call in TokenCalls(token))
                            Log.Info("Entra probe: " + call.name + " on thread pool -> " + await LimitedAsync(() => DescribeValue(call.invoke())));
                    }
                    catch (Exception ex)
                    {
                        Log.Info("Entra probe failed: " + ex.GetType().FullName);
                    }
                }).FileAndForget("SqlAssist/EntraProbe");
            }
            catch (Exception ex)
            {
                Log.Info("Entra probe failed: " + ex.GetType().FullName);
            }
        }

        /// <summary>Expiry-like members whose type cannot carry a token, so their value is safe to log.</summary>
        private static bool IsHarmlessValue(PropertyInfo p)
        {
            if (p.GetIndexParameters().Length != 0 || p.Name.IndexOf("Token", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (p.Name.IndexOf("Expir", StringComparison.OrdinalIgnoreCase) < 0) return false;
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            return t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(bool)
                || t == typeof(int) || t == typeof(long) || t == typeof(double);
        }

        private static (string name, Func<object> invoke)[] TokenCalls(object token)
        {
            var type = token.GetType();
            var methods = type.GetMethods(Public)
                .Where(m => m.GetParameters().Length == 0 && m.ReturnType == typeof(string) && TokenMembers.Contains(m.Name))
                .Select(m => (m.Name + "()", (Func<object>)(() => m.Invoke(token, null))));
            var properties = type.GetProperties(Public)
                .Where(p => p.GetIndexParameters().Length == 0 && p.PropertyType == typeof(string) && TokenMembers.Contains(p.Name))
                .Select(p => (p.Name, (Func<object>)(() => p.GetValue(token))));
            return methods.Concat(properties).ToArray();
        }

        private static string DescribeValue(object value)
        {
            var s = value as string;
            return s == null ? "null" : "string of length " + s.Length + (s.StartsWith("eyJ", StringComparison.Ordinal) ? " (JWT)" : "");
        }

        /// <summary>Runs <paramref name="read"/> on the thread pool; a call still running after <see cref="CallLimit"/> is reported and left.</summary>
        private static async Task<string> LimitedAsync(Func<string> read)
        {
            var work = Task.Run(() =>
            {
                try { return read(); }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                    return inner.GetType().FullName + " (HResult 0x" + inner.HResult.ToString("X8") + ")";
                }
            });
            var first = await Task.WhenAny(work, Task.Delay(CallLimit)).ConfigureAwait(false);
            return first == work ? await work.ConfigureAwait(false) : "timed out after " + CallLimit.TotalSeconds + " s";
        }
    }
}
