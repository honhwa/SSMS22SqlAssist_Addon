using System.Collections.Generic;
using System.Text;

namespace SqlAssist.Core.Sql
{
    /// <summary>Reads a table name typed by the user ("dbo.Orders", "[Sales].[Order Line]", "#tmp") into the form a
    /// script can use: every part is bracketed only when it has to be, so what lands in the script is always one
    /// object name and never extra SQL.</summary>
    public static class SqlObjectName
    {
        private const int MaxPartLength = 128;
        private const int MaxParts = 3;

        /// <param name="formatted">The script-ready name; null when the text is not a valid name.</param>
        /// <param name="error">Why the text was refused, for the user; null on success.</param>
        public static bool TryParse(string text, out string formatted, out string error)
        {
            formatted = null;
            var parts = new List<string>();
            if (!TryReadParts(text, parts, out error)) return false;

            if (parts.Count > MaxParts)
            {
                error = "最多只能有 3 个部分 (database.schema.table)";
                return false;
            }
            for (int i = 0; i < parts.Count; i++)
            {
                string part = parts[i];
                if (part.Trim().Length == 0) { error = "某个名称为空"; return false; }
                if (part.Length > MaxPartLength) { error = "名称 \"" + Shorten(part) + "\" 长度超过 " + MaxPartLength + " 个字符"; return false; }
                if (parts.Count > 1 && (part[0] == '#' || part[0] == '@'))
                {
                    error = "以 # 或 @ 开头的名称（临时表 / 表变量）不能加 schema 或 database 前缀";
                    return false;
                }
            }

            var sb = new StringBuilder();
            foreach (string part in parts)
            {
                if (sb.Length > 0) sb.Append('.');
                sb.Append(SqlIdentifier.QuoteIfNeeded(part));
            }
            formatted = sb.ToString();
            return true;
        }

        private static bool TryReadParts(string text, List<string> parts, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(text)) { error = "请输入目标表名称"; return false; }

            int i = 0;
            while (true)
            {
                SkipSpaces(text, ref i);
                if (i >= text.Length) { error = "名称不完整：点号后必须还有一段名称"; return false; }

                string part;
                char c = text[i];
                if (c == '[' || c == '"')
                {
                    if (!TryReadQuoted(text, ref i, c == '[' ? ']' : '"', out part, out error)) return false;
                }
                else
                {
                    int start = i;
                    while (i < text.Length && IsBareChar(text[i])) i++;
                    if (i == start)
                    {
                        error = "不能在名称中使用字符 '" + c + "'（除非用 [ ] 括起来）";
                        return false;
                    }
                    part = text.Substring(start, i - start);
                }
                parts.Add(part);

                SkipSpaces(text, ref i);
                if (i >= text.Length) return true;
                if (text[i] != '.')
                {
                    error = "不能在名称中使用字符 '" + text[i] + "'（含空格或特殊字符的名称必须放在 [ ] 中）";
                    return false;
                }
                i++;
            }
        }

        // i points at the opening quote; on success it is just past the closing one.
        private static bool TryReadQuoted(string text, ref int i, char close, out string part, out string error)
        {
            part = null;
            error = null;
            var sb = new StringBuilder();
            i++;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == close)
                {
                    if (i + 1 < text.Length && text[i + 1] == close) { sb.Append(close); i += 2; continue; }
                    i++;
                    part = sb.ToString();
                    return true;
                }
                if (char.IsControl(c)) { error = "名称中不能包含控制字符（例如换行）"; return false; }
                sb.Append(c);
                i++;
            }
            error = "名称以 " + (close == ']' ? "[" : "\"") + " 开始，但缺少闭合符号 " + close;
            return false;
        }

        private static bool IsBareChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '#' || c == '@';

        private static void SkipSpaces(string text, ref int i)
        {
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
        }

        private static string Shorten(string s) => s.Length <= 20 ? s : s.Substring(0, 20) + "…";
    }
}
