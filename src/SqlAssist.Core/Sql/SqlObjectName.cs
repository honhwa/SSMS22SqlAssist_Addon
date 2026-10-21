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
                error = "ใส่ได้สูงสุด 3 ส่วน (database.schema.table)";
                return false;
            }
            for (int i = 0; i < parts.Count; i++)
            {
                string part = parts[i];
                if (part.Trim().Length == 0) { error = "ชื่อบางส่วนว่างอยู่"; return false; }
                if (part.Length > MaxPartLength) { error = "ชื่อ \"" + Shorten(part) + "\" ยาวเกิน " + MaxPartLength + " ตัวอักษร"; return false; }
                if (parts.Count > 1 && (part[0] == '#' || part[0] == '@'))
                {
                    error = "ชื่อที่ขึ้นต้นด้วย # หรือ @ (temp table / table variable) ใส่ schema หรือ database นำหน้าไม่ได้";
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
            if (string.IsNullOrWhiteSpace(text)) { error = "ใส่ชื่อตารางปลายทาง"; return false; }

            int i = 0;
            while (true)
            {
                SkipSpaces(text, ref i);
                if (i >= text.Length) { error = "ชื่อไม่ครบ: หลังจุดต้องมีชื่ออีกส่วน"; return false; }

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
                        error = "ใช้อักขระ '" + c + "' ในชื่อโดยไม่ครอบด้วย [ ] ไม่ได้";
                        return false;
                    }
                    part = text.Substring(start, i - start);
                }
                parts.Add(part);

                SkipSpaces(text, ref i);
                if (i >= text.Length) return true;
                if (text[i] != '.')
                {
                    error = "ใช้อักขระ '" + text[i] + "' ในชื่อโดยไม่ครอบด้วย [ ] ไม่ได้ (ชื่อที่มีช่องว่างหรืออักขระพิเศษต้องอยู่ใน [ ])";
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
                if (char.IsControl(c)) { error = "ชื่อมีอักขระควบคุม (เช่น ขึ้นบรรทัดใหม่) ไม่ได้"; return false; }
                sb.Append(c);
                i++;
            }
            error = "ชื่อเปิดด้วย " + (close == ']' ? "[" : "\"") + " แต่ไม่ได้ปิดด้วย " + close;
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
