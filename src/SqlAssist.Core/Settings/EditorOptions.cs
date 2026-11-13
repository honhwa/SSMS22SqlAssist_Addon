namespace SqlAssist.Core.Settings
{
    /// <summary>What SqlAssist does by itself while the user is typing or moving the caret in a query window.</summary>
    public sealed class EditorOptions
    {
        /// <summary>In "INSERT INTO t (a, b) VALUES (1, 2)", mark the column and the value that sit in the same position as the caret.</summary>
        public bool HighlightInsertColumns { get; set; } = true;

        /// <summary>Inside "SUBSTRING(a, |)" show the function's syntax near the caret with the current parameter in bold.</summary>
        public bool ShowParameterHints { get; set; } = true;

        /// <summary>Rank completion items the user commits often a little higher (see Completion.UsageStats).</summary>
        public bool LearnFromUsage { get; set; } = true;
    }
}
