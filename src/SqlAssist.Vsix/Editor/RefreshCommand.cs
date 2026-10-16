using System;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace SqlAssist.Editor
{
    /// <summary>SSMS's "Refresh Local Cache" (Ctrl+Shift+R). Its GUID/ID are SSMS internals, so they are looked up
    /// by command name once; when it fires in a SQL editor our schema cache is cleared too.</summary>
    internal static class RefreshCommand
    {
        private static readonly string[] Names = { "Edit.IntelliSense.RefreshLocalCache", "Edit.RefreshLocalCache" };
        private static bool _resolved;
        private static Guid _guid;
        private static uint _id;

        public static bool Matches(Guid group, uint id)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!_resolved) Resolve();
            return _guid != Guid.Empty && group == _guid && id == _id;
        }

        private static void Resolve()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _resolved = true;
            if (!(ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is DTE dte)) return;
            foreach (var name in Names)
            {
                try
                {
                    var command = dte.Commands.Item(name, -1);
                    if (command == null) continue;
                    _guid = new Guid(command.Guid);
                    _id = (uint)command.ID;
                    Log.Info($"Ctrl+Shift+R hooked via {name}");
                    return;
                }
                catch
                {
                    // Not this name in this SSMS build — try the next.
                }
            }
            Log.Info("SSMS Refresh Local Cache command not found; use Tools > SqlAssist: Refresh Schema Cache");
        }
    }
}
