using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlAssist.Core.Assistant
{
    public sealed class AssistantMarkdownBlock
    {
        public string Text { get; internal set; }
        public string Language { get; internal set; }
        public bool IsCode { get; internal set; }
        public bool IsSql => IsCode && (string.Equals(Language, "sql", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Language, "tsql", StringComparison.OrdinalIgnoreCase) || string.Equals(Language, "t-sql", StringComparison.OrdinalIgnoreCase));
    }
    /// <summary>Splits fenced blocks only. UI renders everything as native text; no HTML/XAML/URL evaluation.</summary>
    public static class AssistantMarkdown
    {
        public static IReadOnlyList<AssistantMarkdownBlock> Parse(string markdown)
        {
            var result = new List<AssistantMarkdownBlock>();
            var buffer = new StringBuilder();
            bool code = false;
            string language = "", fence = "";
            Action flush = () =>
            {
                if (buffer.Length > 0 || code)
                    result.Add(new AssistantMarkdownBlock { Text = buffer.ToString().TrimEnd('\n'), Language = language, IsCode = code });
                buffer.Clear();
            };
            foreach (string line in (markdown ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                var match = Regex.Match(line, @"^\s{0,3}(`{3,}|~{3,})([^\s`]*)\s*$");
                if (!code && match.Success)
                {
                    flush(); code = true; fence = match.Groups[1].Value; language = match.Groups[2].Value;
                }
                else if (code && Regex.IsMatch(line, @"^\s{0,3}" + Regex.Escape(fence[0].ToString()) + "{" + fence.Length + @",}\s*$"))
                {
                    flush(); code = false; language = "";
                }
                else buffer.Append(line).Append('\n');
            }
            flush();
            return result.AsReadOnly();
        }
    }
}
