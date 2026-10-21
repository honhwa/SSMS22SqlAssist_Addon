using System;

namespace SqlAssist.Core.Formatting
{
    public enum KeywordCase
    {
        Upper,
        Lower,
        /// <summary>Keywords keep whatever case they were typed in.</summary>
        Preserve,
    }

    public enum CommaStyle
    {
        /// <summary>One item per line, the comma at the front of each line after the first.</summary>
        Leading,
        /// <summary>One item per line, the comma at the end of every line but the last.</summary>
        Trailing,
    }

    /// <summary>What the user can choose about how SQL is laid out. Everything else about the layout is fixed, so the
    /// same text and the same options always give the same result.</summary>
    public sealed class FormatOptions
    {
        public const int MinIndentSize = 2;
        public const int MaxIndentSize = 8;

        public KeywordCase KeywordCase { get; set; } = KeywordCase.Upper;
        /// <summary>Spaces per indent level, kept between <see cref="MinIndentSize"/> and <see cref="MaxIndentSize"/>.</summary>
        public int IndentSize { get; set; } = 4;
        public CommaStyle CommaStyle { get; set; } = CommaStyle.Leading;
        /// <summary>Line break to write ("\r\n" or "\n"). Null follows the text, and "\n" when it has none.</summary>
        public string Newline { get; set; }

        internal int EffectiveIndentSize => Math.Max(MinIndentSize, Math.Min(MaxIndentSize, IndentSize));
    }

    public sealed class FormatResult
    {
        public FormatResult(string text, string error, bool changed)
        {
            Text = text; Error = error; Changed = changed;
        }

        /// <summary>The formatted text; the original text when formatting did not succeed.</summary>
        public string Text { get; }
        /// <summary>Why the text was left alone, in Thai; null on success.</summary>
        public string Error { get; }
        public bool Changed { get; }
        public bool IsSuccess => Error == null;
    }
}
