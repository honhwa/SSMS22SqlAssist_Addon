using System;
using System.Windows;
using System.Windows.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SqlAssist
{
    /// <summary>Shows SqlAssist's WPF windows the way SSMS wants: owned by its main window (so they stay on top of it and
    /// are centred over it) and, for a modal one, with the shell told to stop routing input to its other windows.</summary>
    internal static class DialogHost
    {
        /// <summary>Makes SSMS's main window the owner of a window that is about to be shown (modeless or not).</summary>
        public static void SetOwner(Window window)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell shell && shell.GetDialogOwnerHwnd(out IntPtr owner) == 0)
                new WindowInteropHelper(window).Owner = owner;
        }

        public static bool? ShowDialog(Window window)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var shell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
            SetOwner(window);
            shell?.EnableModeless(0);
            try
            {
                return window.ShowDialog();
            }
            finally
            {
                shell?.EnableModeless(1);
            }
        }
    }
}
