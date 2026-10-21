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
                return Fail("ข้อมูลใหญ่เกิน " + MaxChars.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) +
                            " ตัวอักษร (วางมา " + text.Length.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + ") — แบ่งวางทีละส่วน");

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
                        return Fail("แถวที่ " + rowNumber + ": เปิด quote (\") แล้วไม่ปิด — ตรวจว่า cell ที่มี quote ใช้ \"\" แทน \" หรือไม่");
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
                    warnings.Add("แถวที่ " + (rows.Count + 1) + ": มีข้อความต่อท้ายหลัง quote ปิด (เช่น \"abc\"def) — เก็บรวมเป็นข้อความใน cell เดียวกัน");
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
                    return Fail("มีคอลัมน์มากเกิน " + MaxColumns + " คอลัมน์ต่อแถว");
                for (int r = 1; r < rows.Count; r++)
                {
                    if (rows[r].Length == width) continue;
                    problems++;
                    if (errors.Count < MaxReportedProblems)
                        errors.Add("แถวที่ " + (r + 1) + " มี " + rows[r].Length + " คอลัมน์ แต่แถวแรกมี " + width + " คอลัมน์ (ไม่เติมช่องให้เอง — แก้ข้อมูลให้ตรงกันก่อน)");
                }
                if (problems > errors.Count)
                    errors.Add("พบปัญหาทั้งหมด " + problems + " ข้อ (แสดง " + errors.Count + " ข้อแรก)");
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
            Fail("มีแถวมากเกิน " + MaxRows.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " แถว — แบ่งวางทีละส่วน");

        private static DelimitedTable Fail(string error) =>
            new DelimitedTable(new string[0][], new[] { error }, new string[0]);
    }
}
