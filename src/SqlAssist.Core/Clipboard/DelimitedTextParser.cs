using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SqlAssist.Core.Clipboard
{
    public sealed class DelimitedTable
    {
        public DelimitedTable(IReadOnlyList<string[]> rows, IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
        {
            Rows = rows; Errors = errors; Warnings = warnings;
        }

        /// <summary>Every row has <see cref="ColumnCount"/> cells when <see cref="IsValid"/>.</summary>
        public IReadOnlyList<string[]> Rows { get; }
        public int ColumnCount => Rows.Count > 0 ? Rows[0].Length : 0;
        /// <summary>Why the text cannot be used as a table, in Thai.</summary>
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>Reads what Excel (or any grid) puts on the clipboard: cells separated by tabs, rows by line breaks, and
    /// a cell holding a tab, line break or quote wrapped in double quotes with the quotes doubled. Strict where a
    /// guess would change the data: rows of different widths are errors, never padded.</summary>
    public static class DelimitedTextParser
    {
        /// <summary>Bigger input would make the dialog and the SSMS editor crawl.</summary>
        public const int MaxChars = 2000000;
        public const int MaxRows = 100000;
        public const int MaxColumns = 256;
        /// <summary>Problems listed one by one; the rest are only counted.</summary>
        public const int MaxReportedProblems = 20;

        public static DelimitedTable Parse(string text, char delimiter = '\t')
        {
            text = text ?? "";
            if (text.Length > MaxChars)
                return Fail("数据过大，超过 " + MaxChars.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) +
                            " 个字符（已粘贴 " + text.Length.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + "）—— 请分批粘贴");

            var rows = new List<string[]>();
            var warnings = new List<string>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quotedCell = false, warnedAfterQuote = false;
            int i = 0, n = text.Length;

            while (i < n)
            {
                char c = text[i];
                if (c == '"' && cell.Length == 0 && !quotedCell)
                {
                    int rowNumber = rows.Count + 1;
                    if (!ReadQuoted(text, ref i, cell))
                        return Fail("第 " + rowNumber + " 行：引号 (\") 已打开但未闭合 —— 请检查含引号的单元格是否用 \"\" 代替 \"");
                    quotedCell = true;
                    continue;
                }
                if (c == delimiter)
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    quotedCell = warnedAfterQuote = false;
                    i++;
                    continue;
                }
                if (c == '\r' || c == '\n')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    quotedCell = warnedAfterQuote = false;
                    rows.Add(row.ToArray());
                    row.Clear();
                    if (rows.Count > MaxRows) return TooManyRows();
                    i += c == '\r' && i + 1 < n && text[i + 1] == '\n' ? 2 : 1;
                    continue;
                }
                if (quotedCell && !warnedAfterQuote)
                {
                    warnedAfterQuote = true;
                    warnings.Add("第 " + (rows.Count + 1) + " 行：闭合引号后还有多余字符（例如 \"abc\"def）—— 已合并为同一单元格的文本");
                }
                cell.Append(c);
                i++;
            }
            if (row.Count > 0 || cell.Length > 0 || quotedCell)
            {
                row.Add(cell.ToString());
                rows.Add(row.ToArray());
                if (rows.Count > MaxRows) return TooManyRows();
            }

            // A trailing blank line is how the last row ends, not another row.
            while (rows.Count > 0 && rows[rows.Count - 1].Length == 1 && rows[rows.Count - 1][0].Length == 0)
                rows.RemoveAt(rows.Count - 1);

            var errors = new List<string>();
            int problems = 0;
            if (rows.Count > 0)
            {
                int width = rows[0].Length;
                if (rows.Any(r => r.Length > MaxColumns))
                    return Fail("列数过多，超过 " + MaxColumns + " 列/行");
                for (int r = 1; r < rows.Count; r++)
                {
                    if (rows[r].Length == width) continue;
                    problems++;
                    if (errors.Count < MaxReportedProblems)
                        errors.Add("第 " + (r + 1) + " 行有 " + rows[r].Length + " 列，但首行有 " + width + " 列（不会自动补齐 —— 请先修正数据使其一致）");
                }
                if (problems > errors.Count)
                    errors.Add("共发现 " + problems + " 个问题（显示前 " + errors.Count + " 个）");
            }
            return new DelimitedTable(rows, errors, warnings);
        }

        // i points at the opening quote; on success it is just past the closing one and cell holds the unquoted text.
        private static bool ReadQuoted(string text, ref int i, StringBuilder cell)
        {
            i++;
            while (i < text.Length)
            {
                if (text[i] == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i += 2; continue; }
                    i++;
                    return true;
                }
                cell.Append(text[i]);
                i++;
            }
            return false;
        }

        private static DelimitedTable TooManyRows() =>
            Fail("行数过多，超过 " + MaxRows.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " 行 —— 请分批粘贴");

        private static DelimitedTable Fail(string error) =>
            new DelimitedTable(new string[0][], new[] { error }, new string[0]);
    }
}
