using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlAssist.Core.Sql
{
    public enum StatementKind
    {
        Other, Select, Insert, Update, Delete, Merge, Declare, Set, Exec, Create, Alter, Drop, Truncate,
        If, Else, While, Begin, End, Print, Return,
    }

    /// <summary>One statement: a range of <see cref="ScanResult.Tokens"/>, from its leading comments to its last token or
    /// trailing comment. Control-flow headers (IF, ELSE, WHILE) and block markers (BEGIN, END) are statements of their own,
    /// so the body of an IF is simply the statement that follows it.</summary>
    public sealed class SqlStatement
    {
        public StatementKind Kind { get; internal set; }
        public int BatchIndex { get; internal set; }
        /// <summary>First token, leading comments included.</summary>
        public int FirstToken { get; internal set; }
        public int LastToken { get; internal set; }
        /// <summary>The keyword that began it; -1 for a run of comments with no statement.</summary>
        public int FirstSignificant { get; internal set; }
        /// <summary>Starts with WITH name AS (...); <see cref="Kind"/> is that of the statement the CTEs belong to.</summary>
        public bool HasCte { get; internal set; }
    }

    /// <summary>The statements between two GO lines.</summary>
    public sealed class SqlBatch
    {
        public List<SqlStatement> Statements { get; } = new List<SqlStatement>();
        /// <summary>First and last token before the GO line; the range is empty when FirstToken &gt; LastToken.</summary>
        public int FirstToken { get; internal set; }
        public int LastToken { get; internal set; }
        /// <summary>The GO token, or -1 for the last batch of a text that does not end in GO.</summary>
        public int GoToken { get; internal set; } = -1;
        /// <summary>The repeat count after GO, or -1.</summary>
        public int GoCountToken { get; internal set; } = -1;
        /// <summary>The last token on the GO line (a trailing comment, for instance).</summary>
        public int GoLastToken { get; internal set; } = -1;
        public bool HasGo => GoToken >= 0;
    }

    public sealed class ScanProblem
    {
        public ScanProblem(int offset, string message) { Offset = offset; Message = message; }
        public int Offset { get; }
        public string Message { get; }
    }

    public sealed class ScanResult
    {
        internal ScanResult(string text, List<SqlToken> tokens)
        {
            Text = text; Tokens = tokens;
        }

        public string Text { get; }
        /// <summary>Operators written as one thing (&lt;=, +=, ::), 1.5E-3 and $5 are single tokens here.</summary>
        public IReadOnlyList<SqlToken> Tokens { get; }
        public List<SqlBatch> Batches { get; } = new List<SqlBatch>();
        /// <summary>What stops the text being read reliably: strings, comments and brackets left open, unbalanced parentheses.</summary>
        public List<ScanProblem> Problems { get; } = new List<ScanProblem>();

        public string TextOf(SqlStatement statement) =>
            Text.Substring(Tokens[statement.FirstToken].Start, Tokens[statement.LastToken].End - Tokens[statement.FirstToken].Start);

        private int[] _lineStarts;

        /// <summary>1-based line and column of an offset; CRLF counts as one line break.</summary>
        public (int line, int column) LineAndColumn(int offset)
        {
            if (_lineStarts == null)
            {
                var starts = new List<int> { 0 };
                for (int i = 0; i < Text.Length; i++)
                {
                    if (Text[i] == '\r' && i + 1 < Text.Length && Text[i + 1] == '\n') continue;
                    if (Text[i] == '\n' || Text[i] == '\r') starts.Add(i + 1);
                }
                _lineStarts = starts.ToArray();
            }
            offset = Math.Max(0, Math.Min(offset, Text.Length));
            int found = Array.BinarySearch(_lineStarts, offset);
            int index = found >= 0 ? found : ~found - 1;
            return (index + 1, offset - _lineStarts[index] + 1);
        }
    }

    /// <summary>Reads T-SQL text as batches (split at GO lines) of statements, with no grammar: a statement starts at a
    /// statement keyword unless the word before it makes that keyword part of the current statement (UNION SELECT, ON DELETE,
    /// INSERT … SELECT …). Good enough to lay statements out and to look at their top-level clauses; wrong guesses cost
    /// a line break, never a different meaning, and each caller keeps that in mind.
    /// Used by the SQL formatter and the risky-statement check.</summary>
    public static class SqlStatementScanner
    {
        private static readonly HashSet<string> PairOperators = new HashSet<string>
            { "<>", "!=", "<=", ">=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "!<", "!>", "::" };

        /// <summary>Words that always begin a statement (when nothing in the current one claims them).</summary>
        private static readonly HashSet<string> AlwaysStart = new HashSet<string>
        {
            "DECLARE", "ELSE", "WHILE", "BEGIN", "END", "RETURN", "PRINT", "USE", "BREAK", "CONTINUE", "GOTO", "THROW", "RAISERROR",
            "WAITFOR", "COMMIT", "ROLLBACK", "SAVE", "CLOSE", "DEALLOCATE", "OPEN", "KILL", "DBCC", "BACKUP", "RESTORE", "TRUNCATE",
            "SHUTDOWN", "CHECKPOINT", "RECONFIGURE", "SETUSER", "READTEXT", "WRITETEXT", "UPDATETEXT", "CREATE",
        };

        private static readonly HashSet<string> Permission = new HashSet<string> { "GRANT", "DENY", "REVOKE" };

        public static ScanResult Scan(string text)
        {
            text = text ?? "";
            var tokens = Merge(text, SqlTokenizer.Tokenize(text));
            var result = new ScanResult(text, tokens);
            FindLexicalProblems(result);
            SplitBatches(result);
            for (int b = 0; b < result.Batches.Count; b++)
            {
                CheckParentheses(result, result.Batches[b]);
                ScanStatements(result, result.Batches[b], b);
            }
            return result;
        }

        // ---- tokens ----

        private static List<SqlToken> Merge(string text, List<SqlToken> raw)
        {
            var merged = new List<SqlToken>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                var t = raw[i];
                bool hasNext = i + 1 < raw.Count && raw[i + 1].Start == t.End;

                if (t.Kind == TokenKind.Symbol && hasNext && raw[i + 1].Kind == TokenKind.Symbol && PairOperators.Contains(t.Text + raw[i + 1].Text))
                {
                    merged.Add(Combine(text, t, raw[i + 1], TokenKind.Symbol));
                    i += 1;
                }
                else if (t.Kind == TokenKind.Number && IsExponentStem(t.Text) && hasNext && raw[i + 1].Kind == TokenKind.Symbol &&
                         (raw[i + 1].Text == "+" || raw[i + 1].Text == "-") && i + 2 < raw.Count && raw[i + 2].Start == raw[i + 1].End &&
                         raw[i + 2].Kind == TokenKind.Number)
                {
                    merged.Add(Combine(text, t, raw[i + 2], TokenKind.Number));
                    i += 2;
                }
                else if (t.Kind == TokenKind.Symbol && t.Text == "$" && hasNext && (raw[i + 1].Kind == TokenKind.Number || raw[i + 1].Kind == TokenKind.Word))
                {
                    merged.Add(Combine(text, t, raw[i + 1], raw[i + 1].Kind));
                    i += 1;
                }
                else merged.Add(t);
            }
            return merged;
        }

        private static SqlToken Combine(string text, SqlToken first, SqlToken last, TokenKind kind) =>
            new SqlToken(kind, first.Start, last.End - first.Start, text.Substring(first.Start, last.End - first.Start), true);

        /// <summary>"1.5E" and "2e": the front of a number like 1.5E-3, which the tokenizer cuts at the sign.</summary>
        private static bool IsExponentStem(string text)
        {
            if (text.Length < 2 || (text[text.Length - 1] != 'e' && text[text.Length - 1] != 'E')) return false;
            bool digit = false;
            for (int i = 0; i < text.Length - 1; i++)
            {
                if (char.IsDigit(text[i])) digit = true;
                else if (text[i] != '.') return false;
            }
            return digit;
        }

        private static void FindLexicalProblems(ScanResult result)
        {
            foreach (var t in result.Tokens)
            {
                if (t.Terminated) continue;
                if (t.Kind == TokenKind.String) result.Problems.Add(new ScanProblem(t.Start, "string literal ('...') ที่เปิดไว้ไม่มีเครื่องหมาย ' ปิด"));
                else if (t.Kind == TokenKind.QuotedIdentifier) result.Problems.Add(new ScanProblem(t.Start, "ชื่อที่เปิดด้วย " + t.Text[0] + " ไม่มี " + (t.Text[0] == '[' ? "]" : "\"") + " ปิดในบรรทัดเดียวกัน"));
                else if (t.Kind == TokenKind.Comment && t.Text.StartsWith("/*", StringComparison.Ordinal)) result.Problems.Add(new ScanProblem(t.Start, "block comment (/* ... */) ที่เปิดไว้ไม่มี */ ปิด"));
            }
        }

        // ---- batches ----

        private static void SplitBatches(ScanResult result)
        {
            var tokens = result.Tokens;
            int first = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (!IsGoLine(result, i, out int count, out int last)) continue;
                result.Batches.Add(new SqlBatch { FirstToken = first, LastToken = i - 1, GoToken = i, GoCountToken = count, GoLastToken = last });
                first = last + 1;
                i = last;
            }
            if (first < tokens.Count || result.Batches.Count == 0)
                result.Batches.Add(new SqlBatch { FirstToken = first, LastToken = tokens.Count - 1 });
        }

        /// <summary>GO alone on its line: nothing before it on the line, then at most a repeat count and comments.</summary>
        private static bool IsGoLine(ScanResult result, int i, out int countToken, out int lastToken)
        {
            countToken = -1;
            lastToken = i;
            var tokens = result.Tokens;
            if (!tokens[i].IsWord("GO")) return false;
            if (i > 0 && !HasLineBreak(result.Text, tokens[i - 1].End, tokens[i].Start)) return false;

            int j = i + 1;
            if (j < tokens.Count && tokens[j].Kind == TokenKind.Number && !HasLineBreak(result.Text, tokens[j - 1].End, tokens[j].Start))
            {
                countToken = j;
                lastToken = j;
                j++;
            }
            while (j < tokens.Count && !HasLineBreak(result.Text, tokens[j - 1].End, tokens[j].Start))
            {
                if (tokens[j].Kind != TokenKind.Comment) { countToken = -1; lastToken = i; return false; }
                lastToken = j;
                j++;
            }
            return true;
        }

        private static bool HasLineBreak(string text, int from, int to)
        {
            for (int i = from; i < to && i < text.Length; i++)
                if (text[i] == '\n' || text[i] == '\r') return true;
            return false;
        }

        private static void CheckParentheses(ScanResult result, SqlBatch batch)
        {
            var open = new Stack<int>();
            for (int i = batch.FirstToken; i <= batch.LastToken; i++)
            {
                var t = result.Tokens[i];
                if (t.Kind != TokenKind.Symbol) continue;
                if (t.Text == "(") open.Push(t.Start);
                else if (t.Text == ")")
                {
                    if (open.Count > 0) open.Pop();
                    else result.Problems.Add(new ScanProblem(t.Start, "มีวงเล็บ ) ปิดเกินโดยไม่มี ( เปิด"));
                }
            }
            foreach (int offset in open.Reverse())
                result.Problems.Add(new ScanProblem(offset, "วงเล็บ ( ที่เปิดไว้ไม่มี ) ปิด"));
        }

        // ---- statements ----

        private sealed class Start
        {
            public int Index;
            public StatementKind Kind;
            public bool HasCte;
        }

        private static void ScanStatements(ScanResult result, SqlBatch batch, int batchIndex)
        {
            var tokens = result.Tokens;
            var starts = new List<Start>();
            Start current = null;
            int parenDepth = 0, caseDepth = 0, prev = -1;
            bool afterSemicolon = false, insertHasSource = false, permission = false, ctePending = false;

            for (int i = batch.FirstToken; i <= batch.LastToken; i++)
            {
                var t = tokens[i];
                if (t.Kind == TokenKind.Comment) continue;
                string w = t.Kind == TokenKind.Word ? t.Text.ToUpperInvariant() : null;
                string prevWord = prev >= 0 && tokens[prev].Kind == TokenKind.Word ? tokens[prev].Text.ToUpperInvariant() : null;
                bool prevComma = prev >= 0 && tokens[prev].IsSymbol(',');
                bool prevClose = prev >= 0 && tokens[prev].IsSymbol(')');
                bool top = parenDepth == 0 && caseDepth == 0;

                if (w == "END" && caseDepth > 0)
                {
                    caseDepth--;
                }
                else if (top)
                {
                    if (current == null || afterSemicolon)
                    {
                        current = Begin(result, starts, i, w);
                        insertHasSource = false;
                        permission = w != null && Permission.Contains(w);
                        ctePending = current.HasCte;
                    }
                    else if (w != null && ctePending && prevClose && IsMainKeyword(w))
                    {
                        current.Kind = KindOf(result, i, w);
                        ctePending = false;
                    }
                    else if (w != null && StartsNew(result, i, w, prevWord, prevComma, current.Kind, insertHasSource, permission, ctePending && prevClose))
                    {
                        current = Begin(result, starts, i, w);
                        insertHasSource = false;
                        permission = Permission.Contains(w);
                        ctePending = current.HasCte;
                    }
                    afterSemicolon = false;
                    if (current.Kind == StatementKind.Insert && (w == "VALUES" || w == "SELECT" || w == "EXEC" || w == "EXECUTE" || w == "DEFAULT")) insertHasSource = true;
                }

                if (t.Kind == TokenKind.Symbol)
                {
                    if (t.Text == "(") parenDepth++;
                    else if (t.Text == ")" && parenDepth > 0) parenDepth--;
                    else if (t.Text == ";" && parenDepth == 0 && caseDepth == 0) afterSemicolon = true;
                }
                else if (w == "CASE") caseDepth++;
                prev = i;
            }

            Build(result, batch, batchIndex, starts);
        }

        private static Start Begin(ScanResult result, List<Start> starts, int index, string word)
        {
            var start = new Start { Index = index, Kind = KindOf(result, index, word) };
            if (word == "WITH" && IsCteStart(result, index)) start.HasCte = true;
            if (start.HasCte) start.Kind = StatementKind.Select;
            starts.Add(start);
            return start;
        }

        private static bool IsMainKeyword(string w) => w == "SELECT" || w == "INSERT" || w == "UPDATE" || w == "DELETE" || w == "MERGE";

        private static StatementKind KindOf(ScanResult result, int index, string w)
        {
            switch (w)
            {
                case "SELECT": return StatementKind.Select;
                case "INSERT": return StatementKind.Insert;
                case "UPDATE": return NextWord(result, index) == "STATISTICS" ? StatementKind.Other : StatementKind.Update;
                case "DELETE": return StatementKind.Delete;
                case "MERGE": return StatementKind.Merge;
                case "DECLARE": return StatementKind.Declare;
                case "SET": return StatementKind.Set;
                case "EXEC":
                case "EXECUTE": return StatementKind.Exec;
                case "CREATE": return StatementKind.Create;
                case "ALTER": return StatementKind.Alter;
                case "DROP": return StatementKind.Drop;
                case "TRUNCATE": return StatementKind.Truncate;
                case "IF": return StatementKind.If;
                case "ELSE": return StatementKind.Else;
                case "WHILE": return StatementKind.While;
                case "PRINT": return StatementKind.Print;
                case "RETURN": return StatementKind.Return;
                case "END": return StatementKind.End;
                case "BEGIN":
                    string next = NextWord(result, index);
                    return next == "TRAN" || next == "TRANSACTION" || next == "DISTRIBUTED" || next == "DIALOG" || next == "CONVERSATION"
                        ? StatementKind.Other : StatementKind.Begin;
                default: return StatementKind.Other;
            }
        }

        /// <summary>True when the word at <paramref name="i"/> begins a new statement rather than continuing the current one.</summary>
        private static bool StartsNew(ScanResult result, int i, string w, string prevWord, bool prevComma, StatementKind kind,
            bool insertHasSource, bool permission, bool afterCte)
        {
            // GRANT SELECT, INSERT ON t TO u — the verbs belong to the GRANT.
            if (permission && (prevComma || (prevWord != null && Permission.Contains(prevWord) || prevWord == "FOR"))) return false;
            switch (w)
            {
                case "SELECT":
                    return !(prevWord == "UNION" || prevWord == "ALL" || prevWord == "EXCEPT" || prevWord == "INTERSECT" ||
                             (kind == StatementKind.Insert && !insertHasSource));
                case "INSERT":
                case "UPDATE":
                case "DELETE":
                    return !(prevWord == "ON" || prevWord == "FOR" || prevWord == "AFTER" || prevWord == "OF" || prevWord == "THEN" || prevWord == "IF" || prevComma);
                case "SET":
                    return !(kind == StatementKind.Update || kind == StatementKind.Merge || kind == StatementKind.Alter);
                case "EXEC":
                case "EXECUTE":
                    return !((kind == StatementKind.Insert && !insertHasSource) || prevWord == "WITH" || prevComma);
                case "IF":
                    return prevWord != "ELSE";
                case "ALTER":
                    return kind != StatementKind.Alter;
                case "DROP":
                    return kind != StatementKind.Alter;
                case "FETCH":
                    return !(prevWord == "ROWS" || prevWord == "ROW");
                case "MERGE":
                    return !(prevWord == "INNER" || prevWord == "LEFT" || prevWord == "RIGHT" || prevWord == "FULL" || prevWord == "OUTER" || prevWord == "CROSS");
                case "WITH":
                    return IsCteStart(result, i);
                case "GRANT":
                case "DENY":
                case "REVOKE":
                    return !(prevWord == "WITH" || (permission && prevWord == "REVOKE"));
                default:
                    return AlwaysStart.Contains(w);
            }
        }

        /// <summary>WITH name [(columns)] AS ( — a CTE, as opposed to WITH (NOLOCK), WITH TIES, WITH CHECK OPTION and friends.</summary>
        private static bool IsCteStart(ScanResult result, int with)
        {
            var tokens = result.Tokens;
            int i = NextSignificant(result, with);
            if (i < 0 || !(tokens[i].Kind == TokenKind.Word || tokens[i].Kind == TokenKind.QuotedIdentifier)) return false;
            i = NextSignificant(result, i);
            if (i >= 0 && tokens[i].IsSymbol('('))
            {
                int depth = 0;
                for (; i < tokens.Count; i++)
                {
                    if (tokens[i].IsSymbol('(')) depth++;
                    else if (tokens[i].IsSymbol(')') && --depth == 0) break;
                }
                if (i >= tokens.Count) return false;
                i = NextSignificant(result, i);
            }
            if (i < 0 || !tokens[i].IsWord("AS")) return false;
            i = NextSignificant(result, i);
            return i >= 0 && tokens[i].IsSymbol('(');
        }

        private static int NextSignificant(ScanResult result, int from)
        {
            for (int i = from + 1; i < result.Tokens.Count; i++)
                if (result.Tokens[i].Kind != TokenKind.Comment) return i;
            return -1;
        }

        private static string NextWord(ScanResult result, int from)
        {
            int i = NextSignificant(result, from);
            return i >= 0 && result.Tokens[i].Kind == TokenKind.Word ? result.Tokens[i].Text.ToUpperInvariant() : null;
        }

        /// <summary>Turns the start points into token ranges, handing each comment to the statement it sits next to: one on
        /// the same line as the end of a statement belongs to it, the others lead the statement that follows.</summary>
        private static void Build(ScanResult result, SqlBatch batch, int batchIndex, List<Start> starts)
        {
            var tokens = result.Tokens;
            if (batch.FirstToken > batch.LastToken) return;

            if (starts.Count == 0)
            {
                batch.Statements.Add(new SqlStatement
                {
                    Kind = StatementKind.Other, BatchIndex = batchIndex, FirstToken = batch.FirstToken, LastToken = batch.LastToken, FirstSignificant = -1,
                });
                return;
            }

            int first = batch.FirstToken;
            for (int k = 0; k < starts.Count; k++)
            {
                int last;
                if (k + 1 < starts.Count)
                {
                    int nextStart = starts[k + 1].Index;
                    int lastSignificant = nextStart - 1;
                    while (lastSignificant > starts[k].Index && tokens[lastSignificant].Kind == TokenKind.Comment) lastSignificant--;
                    last = lastSignificant;
                    while (last + 1 < nextStart && !HasLineBreak(result.Text, tokens[last].End, tokens[last + 1].Start)) last++;
                }
                else last = batch.LastToken;

                batch.Statements.Add(new SqlStatement
                {
                    Kind = starts[k].Kind, BatchIndex = batchIndex, FirstToken = first, LastToken = last,
                    FirstSignificant = starts[k].Index, HasCte = starts[k].HasCte,
                });
                first = last + 1;
            }
        }
    }
}
