using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SqlAssist.Assistant
{
    [Guid("717a2216-00dc-4325-87d3-3a555df4eca8")]
    public sealed class AssistantToolWindow : ToolWindowPane
    {
        private readonly AssistantControl _control = new AssistantControl();
        public AssistantToolWindow() : base(null) { Caption = "SSMS AI Assistant"; Content = _control; }
        protected override void Dispose(bool disposing) { ThreadHelper.ThrowIfNotOnUIThread(); if (disposing) _control.Dispose(); base.Dispose(disposing); }
    }
}
