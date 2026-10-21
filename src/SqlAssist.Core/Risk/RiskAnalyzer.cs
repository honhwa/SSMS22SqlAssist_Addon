using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Risk
{
    public enum RiskRule
    {
        UpdateWithoutWhere,
        DeleteWithoutWhere,
        TruncateTable,
        DropTable,
    }

    /// <summary>Which rules are on. <see cref="WarnBeforeExecute"/> is for the Execute guard and means nothing to the analysis.</summary>
    public sealed class RiskOptions
    {
        public bool UpdateWithoutWhere { get; set; } = true;
        public bool DeleteWithoutWhere { get; set; } = true;
        public bool TruncateTable { get; set; } = true;
        public bool DropTable { get; set; } = true;
        /// <summary>Leave out statements whose target is a #temp table or a @table variable.</summary>
        public bool SkipTempTables { get; set; }
        /// <summary>Ask before Execute runs a script that has findings. Off until someone chooses it.</summary>
        public bool WarnBeforeExecute { get; set; }

        public bool IsEnabled(RiskRule rule)
        {
            switch (rule)
            {
                case RiskRule.UpdateWithoutWhere: return UpdateWithoutWhere;
                case RiskRule.DeleteWithoutWhere: return DeleteWithoutWhere;
                case RiskRule.TruncateTable: return TruncateTable;
                case RiskRule.DropTable: return DropTable;
                default: return false;
            }
        }
    }

    public sealed class RiskFinding
    {
        public RiskRule Rule { get; internal set; }
        /// <summary>Where the statement's first keyword is, and how far the statement runs, in the analysed text.</summary>
        public int Offset { get; internal set; }
        public int Length { get; internal set; }
        public int Line { get; internal set; }
        public int Column { get; internal set; }
        /// <summary>The statement on one line, comments left out, cut short when it is long.</summary>
        public string Statement { get; internal set; }
        public string Message { get; internal set; }
    }

    public sealed class RiskReport
    {
        public List<RiskFinding> Findings { get; } = new List<RiskFinding>();
        /// <summary>What kept part of the text from being read, in Thai.</summary>
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>Finds statements that touch every row of a table, or the table itself: UPDATE or DELETE with no WHERE, TRUNCATE
    /// TABLE, DROP TABLE. It reads statements, not keywords: a WHERE in a subquery, a CTE, a string or a comment is not the
    /// statement's own. It cannot see into dynamic SQL, called procedures, triggers or views, and it never judges whether a
    /// WHERE is the right one, so "no findings" is not "safe".</summary>
    public static class RiskAnalyzer
    {
        public const int MaxStatementLength = 100;

        public const string Disclaimer =
            "ผลตรวจนี้ไม่ใช่การรับประกันความปลอดภัย: การไม่พบรายการไม่ได้แปลว่าปลอดภัย และการมี WHERE ก็ไม่ได้รับประกันว่าคำสั่งปลอดภัย (WHERE อาจกว้างเกินไปหรือผิดเงื่อนไข) " +
            "ตรวจเฉพาะคำสั่งที่เขียนไว้ตรง ๆ ไม่ตรวจ SQL ที่ประกอบเป็นข้อความใน EXEC / sp_executesql, stored procedure ที่ถูกเรียก, trigger หรือ view";

        public static RiskReport Analyze(string text, RiskOptions options)
        {
            var report = new RiskReport();
            if (string.IsNullOrEmpty(text)) return report;
            options = options ?? new RiskOptions();
            var scan = SqlStatementScanner.Scan(text);

            foreach (var problem in scan.Problems)
            {
                var (line, column) = scan.LineAndColumn(problem.Offset);
                report.Notes.Add("บรรทัด " + line + " คอลัมน์ " + column + ": " + problem.Message + " — ตรวจได้ถึงจุดที่อ่านออกเท่านั้น");
            }

            foreach (var batch in scan.Batches)
                foreach (var statement in batch.Statements)
                {
                    if (statement.FirstSignificant < 0) continue;
                    var finding = Judge(scan, statement, options);
                    if (finding != null) report.Findings.Add(finding);
                }
            return report;
        }

        private static RiskFinding Judge(ScanResult scan, SqlStatement statement, RiskOptions options)
        {
            var tokens = scan.Tokens;
            int first = statement.FirstSignificant;
            int last = LastSignificant(tokens, statement);
            RiskRule rule;
            string message;
            string target;

            switch (statement.Kind)
            {
                case StatementKind.Update:
                case StatementKind.Delete:
                {
                    bool isUpdate = statement.Kind == StatementKind.Update;
                    rule = isUpdate ? RiskRule.UpdateWithoutWhere : RiskRule.DeleteWithoutWhere;
                    if (!options.IsEnabled(rule) || HasOwnWhere(tokens, first, last)) return null;

                    int i = NextSignificant(tokens, first, last);
                    bool top = i >= 0 && tokens[i].IsWord("TOP");
                    if (top) i = SkipTop(tokens, i, last);
                    if (!isUpdate && i >= 0 && tokens[i].IsWord("FROM")) i = NextSignificant(tokens, i, last);
                    target = ReadName(scan, i, last);
                    if (options.SkipTempTables && IsTemporary(target)) return null;

                    string what = target.Length > 0 ? target : "ตารางที่ระบุ";
                    message = isUpdate
                        ? "UPDATE ที่ไม่มี WHERE: จะแก้ทุกแถวของ " + what
                        : "DELETE ที่ไม่มี WHERE: จะลบทุกแถวของ " + what;
                    if (top) message += " (มี TOP จำกัดจำนวนแถว แต่ยังไม่มี WHERE)";
                    break;
                }
                case StatementKind.Truncate:
                {
                    rule = RiskRule.TruncateTable;
                    int i = NextSignificant(tokens, first, last);
                    if (!options.IsEnabled(rule) || i < 0 || !tokens[i].IsWord("TABLE")) return null;
                    target = ReadName(scan, NextSignificant(tokens, i, last), last);
                    if (options.SkipTempTables && IsTemporary(target)) return null;
                    message = "TRUNCATE TABLE " + (target.Length > 0 ? target : "ตารางที่ระบุ") + ": ลบข้อมูลทุกแถวของตาราง (ไม่มี WHERE ให้เลือกแถว)";
                    break;
                }
                case StatementKind.Drop:
                {
                    rule = RiskRule.DropTable;
                    int i = NextSignificant(tokens, first, last);
                    if (!options.IsEnabled(rule) || i < 0 || !tokens[i].IsWord("TABLE")) return null;
                    i = NextSignificant(tokens, i, last);
                    if (i >= 0 && tokens[i].IsWord("IF"))
                    {
                        i = NextSignificant(tokens, i, last);                 // EXISTS
                        i = i >= 0 ? NextSignificant(tokens, i, last) : -1;
                    }
                    target = ReadNameList(scan, i, last);
                    if (options.SkipTempTables && target.Length > 0 && target.Split(',').All(t => IsTemporary(t.Trim()))) return null;
                    message = "DROP TABLE " + (target.Length > 0 ? target : "ตารางที่ระบุ") + ": ลบตารางทั้งตัว (โครงสร้างและข้อมูล)";
                    break;
                }
                default:
                    return null;
            }

            var (line, column) = scan.LineAndColumn(tokens[first].Start);
            return new RiskFinding
            {
                Rule = rule,
                Offset = tokens[first].Start,
                Length = tokens[last].End - tokens[first].Start,
                Line = line,
                Column = column,
                Statement = OneLine(scan, first, last),
                Message = message,
            };
        }

        /// <summary>A WHERE of the statement itself: outside every pair of parentheses, so not in a subquery, a CTE or a function call.</summary>
        private static bool HasOwnWhere(IReadOnlyList<SqlToken> tokens, int first, int last)
        {
            int depth = 0;
            for (int i = first; i <= last; i++)
            {
                var t = tokens[i];
                if (t.IsSymbol('(')) depth++;
                else if (t.IsSymbol(')')) depth = Math.Max(0, depth - 1);
                else if (depth == 0 && t.IsWord("WHERE")) return true;
            }
            return false;
        }

        private static int LastSignificant(IReadOnlyList<SqlToken> tokens, SqlStatement statement)
        {
            int last = statement.LastToken;
            while (last > statement.FirstSignificant && tokens[last].Kind == TokenKind.Comment) last--;
            return last;
        }

        private static int NextSignificant(IReadOnlyList<SqlToken> tokens, int from, int last)
        {
            for (int i = from + 1; i <= last; i++)
                if (tokens[i].Kind != TokenKind.Comment) return i;
            return -1;
        }

        /// <summary>TOP (n) [PERCENT] or TOP n: the index of the first token after it, or -1.</summary>
        private static int SkipTop(IReadOnlyList<SqlToken> tokens, int top, int last)
        {
            int i = NextSignificant(tokens, top, last);
            if (i < 0) return -1;
            if (tokens[i].IsSymbol('('))
            {
                int depth = 0;
                for (; i <= last; i++)
                {
                    if (tokens[i].IsSymbol('(')) depth++;
                    else if (tokens[i].IsSymbol(')') && --depth == 0) break;
                }
            }
            i = NextSignificant(tokens, i, last);
            if (i >= 0 && tokens[i].IsWord("PERCENT")) i = NextSignificant(tokens, i, last);
            return i;
        }

        /// <summary>A possibly dotted name starting at <paramref name="i"/> ("dbo.Orders", "[a b].c", "#t", "@v"); "" when there is none.</summary>
        private static string ReadName(ScanResult scan, int i, int last)
        {
            var tokens = scan.Tokens;
            var sb = new StringBuilder();
            while (i >= 0 && i <= last)
            {
                var t = tokens[i];
                bool name = t.Kind == TokenKind.QuotedIdentifier || t.Kind == TokenKind.Variable || (t.Kind == TokenKind.Word && !SqlKeywords.IsReserved(t.Text));
                if (!name) break;
                sb.Append(t.Text);
                int next = NextSignificant(tokens, i, last);
                if (next >= 0 && tokens[next].IsSymbol('.'))
                {
                    sb.Append('.');
                    i = NextSignificant(tokens, next, last);
                    // dbo..t leaves a gap: keep reading after the second dot
                    while (i >= 0 && i <= last && tokens[i].IsSymbol('.')) { sb.Append('.'); i = NextSignificant(tokens, i, last); }
                    continue;
                }
                break;
            }
            return sb.ToString();
        }

        /// <summary>Comma-separated names, as DROP TABLE takes them: "dbo.A, #b".</summary>
        private static string ReadNameList(ScanResult scan, int i, int last)
        {
            var names = new List<string>();
            while (i >= 0 && i <= last)
            {
                string name = ReadName(scan, i, last);
                if (name.Length == 0) break;
                names.Add(name);
                // move past the tokens of this name, then a comma if there is one
                int j = i;
                int consumed = 0;
                while (j >= 0 && j <= last && consumed < name.Length)
                {
                    consumed += scan.Tokens[j].Length;
                    int n = NextSignificant(scan.Tokens, j, last);
                    if (n >= 0 && scan.Tokens[n].IsSymbol('.')) { consumed += scan.Tokens[n].Length; j = NextSignificant(scan.Tokens, n, last); }
                    else { j = n; break; }
                }
                if (j >= 0 && j <= last && scan.Tokens[j].IsSymbol(',')) i = NextSignificant(scan.Tokens, j, last);
                else break;
            }
            return string.Join(", ", names);
        }

        private static bool IsTemporary(string name) => name.Length > 0 && (name[0] == '#' || name[0] == '@');

        /// <summary>The statement on one line: comments left out, whitespace between tokens a single space (none where there was none).</summary>
        private static string OneLine(ScanResult scan, int first, int last)
        {
            var tokens = scan.Tokens;
            var sb = new StringBuilder();
            int previousEnd = -1;
            for (int i = first; i <= last; i++)
            {
                var t = tokens[i];
                if (t.Kind == TokenKind.Comment) { previousEnd = -2; continue; }
                if (previousEnd != -1 && (previousEnd == -2 || previousEnd != t.Start)) sb.Append(' ');
                sb.Append(t.Text.Replace("\r", " ").Replace("\n", " "));
                previousEnd = t.End;
                if (sb.Length > MaxStatementLength + 20) break;
            }
            string text = sb.ToString();
            return text.Length <= MaxStatementLength ? text : text.Substring(0, MaxStatementLength).TrimEnd() + "…";
        }
    }
}
