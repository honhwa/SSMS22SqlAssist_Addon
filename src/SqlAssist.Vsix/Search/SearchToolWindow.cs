using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SqlAssist.Search
{
    /// <summary>The dockable "SqlAssist Search" window (Tools > SqlAssist > Search Objects...).</summary>
    [Guid("4f6b2a8e-3c1d-4e7a-9b25-8d0c6f1e2a73")]
    public sealed class SearchToolWindow : ToolWindowPane
    {
        private readonly SearchControl _control = new SearchControl();

        public SearchToolWindow() : base(null)
        {
            Caption = "SqlAssist Search";
            Content = _control;
        }

        /// <summary>Called each time the window is shown from the menu: put the caret in the search box.</summary>
        public void FocusSearch()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _control.FocusSearch();
        }
    }
}
