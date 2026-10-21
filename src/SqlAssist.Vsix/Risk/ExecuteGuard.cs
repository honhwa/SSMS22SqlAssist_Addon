using System;
using System.Collections.Generic;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlAssist.Connection;
using SqlAssist.Core.Risk;
using SqlAssist.Editor;

namespace SqlAssist.Risk
{
    /// <summary>EXPERIMENTAL, and off until chosen in Options. Asks before SSMS's Execute runs a script that has findings.
    /// It is a priority command target: the shell offers it every command before anything else, so it sees Execute however it
    /// was started (F5, Ctrl+E, the toolbar button, the Query menu) — in theory. Which commands count as Execute is looked up
    /// by name at run time and written to the log; nothing here has been confirmed against every way of running a query, so it
    /// is not a guarantee that no risky statement runs. Cancel Query is never touched.</summary>
    internal sealed class ExecuteGuard : IOleCommandTarget
    {
        /// <summary>The names SSMS's commands carry in Tools › Options › Keyboard; those that exist are hooked.</summary>
        private static readonly string[] CommandNames = { "Query.Execute", "Query.ExecuteQuery", "Query.ExecuteSelection" };
        private const int MaxChars = 5000000;
        /// <summary>"Not handled here": the shell goes on to route the command as if this target were not there.</summary>
        private const int NotHandled = unchecked((int)0x80040104); // OLECMDERR_E_UNKNOWNGROUP

        private static ExecuteGuard _active;
        private static uint _cookie;
        private static string _status = "ปิดอยู่";

        private readonly HashSet<(Guid group, uint id)> _commands;
        private readonly RiskOptions _options;

        private ExecuteGuard(HashSet<(Guid, uint)> commands, RiskOptions options)
        {
            _commands = commands;
            _options = options;
        }

        /// <summary>What the guard is doing now, for the Options window: off, or which commands it is hooked to, or why it is not.</summary>
        public static string Status => _status;
        public static bool IsHooked => _active != null;

        /// <summary>Starts or stops the guard to match the settings. Nothing is registered while it is off, so it costs nothing then.</summary>
        public static void Apply(RiskOptions options)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                Stop();
                if (!options.WarnBeforeExecute)
                {
                    _status = "ปิดอยู่";
                    return;
                }
                var commands = Resolve();
                if (commands.Count == 0)
                {
                    _status = "เปิดไว้ แต่หาคำสั่ง Execute ของ SSMS ไม่พบ (ลองชื่อ " + string.Join(", ", CommandNames) + ") จึงยังไม่ได้ดัก — ใช้ Check SQL Risks ตรวจเองแทน";
                    Log.Info("Execute guard: " + _status);
                    return;
                }
                if (!(Package.GetGlobalService(typeof(SVsRegisterPriorityCommandTarget)) is IVsRegisterPriorityCommandTarget register))
                {
                    _status = "เปิดไว้ แต่ SSMS นี้ไม่มีบริการที่ใช้ดักคำสั่ง จึงยังไม่ได้ดัก — ใช้ Check SQL Risks ตรวจเองแทน";
                    Log.Info("Execute guard: " + _status);
                    return;
                }
                var guard = new ExecuteGuard(commands.Select(c => (c.group, c.id)).ToHashSet(), Copy(options));
                if (register.RegisterPriorityCommandTarget(0, guard, out _cookie) != VSConstants.S_OK)
                {
                    _status = "เปิดไว้ แต่ลงทะเบียนตัวดักคำสั่งไม่สำเร็จ จึงยังไม่ได้ดัก — ใช้ Check SQL Risks ตรวจเองแทน";
                    Log.Info("Execute guard: " + _status);
                    return;
                }
                _active = guard;
                _status = "ดักคำสั่ง: " + string.Join(", ", commands.Select(c => c.name)) + " (ทดลอง ยังไม่ได้ยืนยันทุกช่องทางที่รัน query)";
                Log.Info("Execute guard hooked: " + string.Join("; ", commands.Select(c => c.name + " " + c.group + ":" + c.id)));
            }
            catch (Exception ex)
            {
                Log.Error("Execute guard", ex);
                _status = "เปิดไว้ แต่เกิดข้อผิดพลาดตอนตั้งค่า (" + ex.GetType().Name + ") จึงยังไม่ได้ดัก";
            }
        }

        private static void Stop()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_active == null) return;
            if (Package.GetGlobalService(typeof(SVsRegisterPriorityCommandTarget)) is IVsRegisterPriorityCommandTarget register)
                register.UnregisterPriorityCommandTarget(_cookie);
            _active = null;
        }

        private static List<(string name, Guid group, uint id)> Resolve()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var found = new List<(string, Guid, uint)>();
            if (!(Package.GetGlobalService(typeof(DTE)) is DTE dte)) return found;
            foreach (string name in CommandNames)
            {
                try
                {
                    var command = dte.Commands.Item(name, -1);
                    if (command != null) found.Add((name, new Guid(command.Guid), (uint)command.ID));
                }
                catch
                {
                    // No command by that name in this SSMS build.
                }
            }
            return found;
        }

        private static RiskOptions Copy(RiskOptions o) => new RiskOptions
        {
            UpdateWithoutWhere = o.UpdateWithoutWhere, DeleteWithoutWhere = o.DeleteWithoutWhere, TruncateTable = o.TruncateTable,
            DropTable = o.DropTable, SkipTempTables = o.SkipTempTables, WarnBeforeExecute = o.WarnBeforeExecute,
        };

        int IOleCommandTarget.QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText) => NotHandled;

        int IOleCommandTarget.Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            if (!_commands.Contains((pguidCmdGroup, nCmdID))) return NotHandled;
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                // S_OK: handled, and nothing more happens, so the query does not run. Anything else lets Execute go ahead.
                return ShouldStop() ? VSConstants.S_OK : NotHandled;
            }
            catch (Exception ex)
            {
                Log.Error("Execute guard", ex);
                return NotHandled;
            }
        }

        /// <summary>True when the script about to run has findings and the user chose to cancel.</summary>
        private bool ShouldStop()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var view = ActiveSqlEditor.Find();
            if (view == null) return false;
            var scope = ActiveSqlEditor.RunScope(view, out bool isSelection);
            if (scope.Length > MaxChars)
            {
                Log.Info("Execute guard: script too large to check (" + scope.Length + " characters); letting it run");
                return false;
            }
            var report = RiskAnalyzer.Analyze(scope.GetText(), _options);
            if (report.Findings.Count == 0) return false;

            var snapshot = scope.Snapshot;
            var items = report.Findings.Select(f => new ExecuteWarningWindow.Item
            {
                Rule = RiskWindow.RuleName(f.Rule),
                Line = snapshot.GetLineFromPosition(scope.Start.Position + f.Offset).LineNumber + 1,
                Statement = f.Statement,
                Message = f.Message,
            }).ToList();
            var target = ActiveConnection.TryGet();
            var window = new ExecuteWarningWindow(target?.Server ?? "(ยังไม่ได้เชื่อมต่อ)", target?.Database ?? "(ไม่ทราบ)", isSelection, items, report.Notes);
            return DialogHost.ShowDialog(window) != true;
        }
    }
}
