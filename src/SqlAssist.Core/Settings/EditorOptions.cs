namespace SqlAssist.Core.Settings
{
    /// <summary>What SqlAssist does by itself while the user is typing or moving the caret in a query window.</summary>
    public sealed class EditorOptions
    {
        /// <summary>SqlAssist's own IntelliSense: the completion list (while typing and on Ctrl+Space) and the Tab
        /// expansions (<c>*</c> to column names, INSERT / UPDATE / EXEC / ALTER to a full statement). Off leaves the editor
        /// as if SqlAssist were not installed, except for snippets, the hover panel, the INSERT highlight and F12, which
        /// have their own switches or none.</summary>
        public bool IntelliSenseEnabled { get; set; } = true;

        /// <summary>In "INSERT INTO t (a, b) VALUES (1, 2)", mark the column and the value that sit in the same position as the caret.</summary>
        public bool HighlightInsertColumns { get; set; } = true;
    }
}
