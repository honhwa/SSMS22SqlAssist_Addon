using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlAssist.Core.Formatting;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Completion
{
    /// <summary>What Tab does after "GROUP BY ": the grouping expressions taken from the SELECT of the same query block, and
    /// where they go. <see cref="Count"/> is what the editor's hint shows, so hint and Tab share one answer.</summary>
    public sealed class GroupByExpansion
    {
        private readonly bool _breakAfter;

        internal GroupByExpansion(IReadOnlyList<string> expressions, int groupStart, int byEnd, int replaceStart, int replaceEnd, string indent, bool breakAfter)
        {
            Expressions = expressions; GroupStart = groupStart; ByEnd = byEnd;
            ReplaceStart = replaceStart; ReplaceEnd = replaceEnd; Indent = indent; _breakAfter = breakAfter;
        }

        /// <summary>The expressions in the order of the select list, aliases and duplicates removed.</summary>
        public IReadOnlyList<string> Expressions { get; }
        public int Count => Expressions.Count;
        /// <summary>Offset of the GROUP keyword, and just past BY (where the hint is anchored).</summary>
        public int GroupStart { get; }
        public int ByEnd { get; }
        /// <summary>The text from here to <see cref="ReplaceEnd"/> (the white space after BY) is replaced by <see cref="Build"/>'s text: one edit.</summary>
        public int ReplaceStart { get; }
        public int ReplaceEnd { get; }
        /// <summary>Leading white space of the line GROUP BY is on; the list is indented one step further.</summary>
        public string Indent { get; }

        /// <summary>One expression per line, commas where the format settings put them (same layout as the formatter's GROUP BY).
        /// <see cref="Expansion.CaretOffset"/> is the end of the last expression. A keyword that followed the caret with nothing
        /// in between (GROUP BY |HAVING) is moved to its own line so the two do not touch.</summary>
        public Expansion Build(FormatOptions options, string newLine = "\n")
        {
            options = options ?? new FormatOptions();
            newLine = string.IsNullOrEmpty(newLine) ? "\n" : newLine;
            int step = options.EffectiveIndentSize;
            var sb = new StringBuilder();
            for (int i = 0; i < Expressions.Count; i++)
            {
                sb.Append(newLine).Append(Indent);
                if (options.CommaStyle == CommaStyle.Leading && i > 0) sb.Append(' ', step - 2).Append(", ");
                else sb.Append(' ', step);
                sb.Append(Expressions[i]);
                if (options.CommaStyle == CommaStyle.Trailing && i < Expressions.Count - 1) sb.Append(',');
            }
            int caret = sb.Length;
            if (_breakAfter) sb.Append(newLine).Append(Indent);
            return new Expansion(sb.ToString(), caret);
        }
    }

    /// <summary>"GROUP BY |" with nothing after it yet: the grouping expressions are the select items of the query block that owns
    /// the GROUP BY, without their aliases, leaving out constants, variables and ordinary aggregates. Read from the tokens only
    /// (no schema, no database). Whatever the first version does not understand — SELECT *, window functions, UNION, subqueries in
    /// the select list, functions that may be scalar or aggregate, columns mixed with aggregates — gives null, so Tab stays Tab
    /// and no hint is shown.</summary>
    public static class GroupByExpander
    {
        private static readonly HashSet<string> Aggregates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP", "STRING_AGG", "CHECKSUM_AGG",
            "APPROX_COUNT_DISTINCT", "APPROX_PERCENTILE_CONT", "APPROX_PERCENTILE_DISC", "JSON_ARRAYAGG", "JSON_OBJECTAGG",
            "GROUPING", "GROUPING_ID",
        };

        // Built-in scalar functions, by name. A function that is in neither list is not guessed at.
        private static readonly HashSet<string> Scalars = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "YEAR", "MONTH", "DAY", "DATEPART", "DATENAME", "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATETRUNC", "DATE_BUCKET", "EOMONTH",
            "DATEFROMPARTS", "DATETIMEFROMPARTS", "DATETIME2FROMPARTS", "SMALLDATETIMEFROMPARTS", "TIMEFROMPARTS", "DATETIMEOFFSETFROMPARTS",
            "SWITCHOFFSET", "TODATETIMEOFFSET", "GETDATE", "GETUTCDATE", "SYSDATETIME", "SYSUTCDATETIME", "SYSDATETIMEOFFSET",
            "ISNULL", "COALESCE", "NULLIF", "IIF", "CHOOSE", "GREATEST", "LEAST", "CAST", "TRY_CAST", "CONVERT", "TRY_CONVERT", "PARSE", "TRY_PARSE", "FORMAT",
            "LEFT", "RIGHT", "SUBSTRING", "LEN", "DATALENGTH", "LTRIM", "RTRIM", "TRIM", "UPPER", "LOWER", "REPLACE", "CONCAT", "CONCAT_WS",
            "STUFF", "REVERSE", "CHARINDEX", "PATINDEX", "REPLICATE", "SPACE", "STR", "CHAR", "NCHAR", "ASCII", "UNICODE", "QUOTENAME",
            "TRANSLATE", "SOUNDEX", "DIFFERENCE", "STRING_ESCAPE",
            "ABS", "CEILING", "FLOOR", "ROUND", "SIGN", "POWER", "SQRT", "SQUARE", "EXP", "LOG", "LOG10", "PI", "RAND", "SIN", "COS", "TAN",
            "ASIN", "ACOS", "ATAN", "ATN2", "DEGREES", "RADIANS",
            "ISNUMERIC", "ISDATE", "ISJSON", "JSON_VALUE", "JSON_QUERY", "NEWID", "HASHBYTES", "CHECKSUM", "BINARY_CHECKSUM",
            "DB_NAME", "DB_ID", "OBJECT_NAME", "OBJECT_ID", "SCHEMA_NAME", "USER_NAME", "SUSER_SNAME", "HOST_NAME", "APP_NAME",
        };

        // CONVERT(type, …): the first argument is a type. DATEADD(part, …) and friends: the first argument is a date part.
        private static readonly HashSet<string> FirstArgumentIsNotAColumn = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CONVERT", "TRY_CONVERT", "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATEPART", "DATENAME", "DATETRUNC", "DATE_BUCKET",
        };

        // A word right before the last token means that token is an operand, not an alias ("x COLLATE y", "a AT TIME ZONE tz").
        private static readonly HashSet<string> NoAliasAfter = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AND", "OR", "NOT", "IS", "IN", "LIKE", "BETWEEN", "THEN", "ELSE", "WHEN", "CASE", "COLLATE", "ESCAPE", "ZONE", "ALL", "ANY", "SOME", "AS",
        };

        /// <summary>The expansion Tab would do with the caret at <paramref name="caret"/>, or null when there is none.</summary>
        public static GroupByExpansion Find(string text, int caret)
        {
            if (string.IsNullOrEmpty(text) || caret <= 0 || caret > text.Length || !EndsWithGroupBy(text, caret)) return null;

            var tokens = SqlTokenizer.Tokenize(text);
            if (SqlTokenizer.IsInStringOrComment(tokens, caret)) return null;
            var sig = tokens.Where(t => t.Kind != TokenKind.Comment).ToList();

            // The caret is right after BY, or after white space only.
            int by = -1;
            for (int i = 0; i < sig.Count && sig[i].End <= caret; i++) by = i;
            if (by < 1 || !sig[by].IsWord("BY") || !sig[by - 1].IsWord("GROUP")) return null;
            for (int i = sig[by].End; i < caret; i++)
                if (!char.IsWhiteSpace(text[i])) return null;   // a comment between BY and the caret
            int group = by - 1;

            // Nothing written after it yet: what follows is the end of the statement or the next clause, not an expression.
            if (by + 1 < sig.Count && !EndsTheClause(sig, by + 1)) return null;

            // The SELECT that owns this GROUP BY: the nearest one before it at the same parenthesis level, in the same statement.
            int owner = OwnerSelect(sig, group);
            if (owner < 0 || PartOfQueryExpressionBefore(sig, owner)) return null;
            var list = SelectListReader.Read(sig, owner);
            if (list == null || !list.ClauseFollows || list.EndToken > group) return null;
            if (!SelectListReader.TryScanStatementEnd(sig, list.EndToken, text.Length, out _, out int endToken)) return null;
            if (endToken < sig.Count && sig[endToken].IsSymbol(')') && endToken + 1 < sig.Count &&
                sig[endToken + 1].Kind == TokenKind.Word && SelectListReader.SetOperators.Contains(sig[endToken + 1].Text)) return null;   // (SELECT …) UNION …

            var expressions = new List<string>();
            var seen = new HashSet<string>();
            foreach (var item in list.Items)
            {
                if (item.First < 0) return null;                                            // an empty item: not typed yet
                if (SelectListReader.IsStarItem(sig, item.First, item.Last)) return null;  // * is not expanded
                if (!StripAlias(sig, item.First, item.Last, out int e1, out int e2) || e2 < e1) return null;
                var verdict = Classify(sig, e1, e2);
                if (verdict == Verdict.Reject) return null;
                if (verdict == Verdict.Skip) continue;
                string expression = ExpressionText(text, sig, e1, e2, out string key);
                if (seen.Add(key)) expressions.Add(expression);
            }
            if (expressions.Count == 0) return null;

            int replaceEnd = caret, w = caret;
            while (w < text.Length && (text[w] == ' ' || text[w] == '\t')) w++;
            if (w == text.Length || text[w] == '\r' || text[w] == '\n') replaceEnd = w;   // spaces left at the end of the line go with it
            bool breakAfter = replaceEnd < text.Length && !char.IsWhiteSpace(text[replaceEnd]) && text[replaceEnd] != ';' && text[replaceEnd] != ')';

            int lineStart = sig[group].Start;
            while (lineStart > 0 && text[lineStart - 1] != '\n' && text[lineStart - 1] != '\r') lineStart--;
            int indentEnd = lineStart;
            while (indentEnd < sig[group].Start && (text[indentEnd] == ' ' || text[indentEnd] == '\t')) indentEnd++;

            return new GroupByExpansion(expressions, sig[group].Start, sig[by].End, sig[by].End, replaceEnd,
                text.Substring(lineStart, indentEnd - lineStart), breakAfter);
        }

        // A cheap look at the text before the caret, so the hint can ask on every caret move without tokenizing.
        private static bool EndsWithGroupBy(string text, int caret)
        {
            int i = caret;
            while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
            if (i < 2 || (text[i - 1] != 'y' && text[i - 1] != 'Y') || (text[i - 2] != 'b' && text[i - 2] != 'B')) return false;
            i -= 2;
            if (i > 0 && SqlIdentifier.IsWordChar(text[i - 1])) return false;
            while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
            return i >= 5 && string.Compare(text, i - 5, "GROUP", 0, 5, StringComparison.OrdinalIgnoreCase) == 0 &&
                   (i == 5 || !SqlIdentifier.IsWordChar(text[i - 6]));
        }

        private static bool EndsTheClause(List<SqlToken> sig, int p)
        {
            var t = sig[p];
            if (t.Kind == TokenKind.Symbol) return t.Text[0] == ';' || t.Text[0] == ')';
            if (t.Kind != TokenKind.Word) return false;
            if (t.IsWord("HAVING") || t.IsWord("ORDER") || t.IsWord("OPTION") || t.IsWord("FOR") || SelectListReader.SetOperators.Contains(t.Text)) return true;
            return SelectListReader.EndsStatement(sig, p, 0);
        }

        // Walks back from GROUP, skipping whole parenthesized groups (a subquery or a CTE body is not this block).
        private static int OwnerSelect(List<SqlToken> sig, int group)
        {
            int depth = 0;
            for (int i = group - 1; i >= 0; i--)
            {
                var t = sig[i];
                if (t.Kind == TokenKind.Symbol)
                {
                    char c = t.Text[0];
                    if (c == ')') depth++;
                    else if (c == '(') { if (depth == 0) return -1; depth--; }
                    else if (c == ';' && depth == 0) return -1;
                    continue;
                }
                if (depth > 0 || t.Kind != TokenKind.Word) continue;
                if (t.IsWord("SELECT")) return i;
                if (SelectListReader.EndsStatement(sig, i, 0)) return -1;   // another statement starts first
            }
            return -1;
        }

        // "… UNION SELECT", "… UNION ALL SELECT", "… UNION (SELECT": the block is one part of a query expression.
        private static bool PartOfQueryExpressionBefore(List<SqlToken> sig, int owner)
        {
            int p = owner - 1;
            if (p >= 0 && sig[p].IsSymbol('(')) p--;
            else if (p >= 0 && sig[p].IsWord("ALL")) p--;
            return p >= 0 && sig[p].Kind == TokenKind.Word && SelectListReader.SetOperators.Contains(sig[p].Text);
        }

        /// <summary>The select item without its alias: <c>expr AS a</c>, <c>expr a</c> and <c>a = expr</c>. False when the item has
        /// a shape that cannot be told apart from those with certainty.</summary>
        private static bool StripAlias(List<SqlToken> sig, int first, int last, out int e1, out int e2)
        {
            e1 = first; e2 = last;
            // alias = expression
            if (last > first + 1 && sig[first + 1].IsSymbol('=') &&
                (sig[first].Kind == TokenKind.QuotedIdentifier || (sig[first].Kind == TokenKind.Word && !SqlKeywords.IsReserved(sig[first].Text))))
            {
                e1 = first + 2;
                return true;
            }

            int depth = 0, asAt = -1;
            for (int i = first; i <= last; i++)
            {
                if (sig[i].IsSymbol('(')) depth++;
                else if (sig[i].IsSymbol(')')) depth--;
                else if (depth == 0 && sig[i].IsWord("AS"))
                {
                    if (asAt >= 0) return false;
                    asAt = i;
                }
            }
            if (asAt >= 0)
            {
                // The alias is exactly one token: a name or a string.
                if (asAt == first || asAt != last - 1) return false;
                var alias = sig[last];
                if (alias.Kind != TokenKind.Word && alias.Kind != TokenKind.QuotedIdentifier && alias.Kind != TokenKind.String) return false;
                e2 = asAt - 1;
                return true;
            }

            // expr alias: a name after something that completes an expression.
            if (last > first)
            {
                var l = sig[last];
                var p = sig[last - 1];
                bool aliasable = l.Kind == TokenKind.QuotedIdentifier || (l.Kind == TokenKind.Word && SqlKeywords.CanBeAlias(l.Text));
                bool completes = p.IsSymbol(')') || (p.Kind != TokenKind.Symbol && !(p.Kind == TokenKind.Word && NoAliasAfter.Contains(p.Text)));
                if (aliasable && completes) e2 = last - 1;
            }
            return true;
        }

        private enum Verdict { Include, Skip, Reject }

        // Include: a scalar expression with a column in it. Skip: constants / variables, or aggregates only. Reject: not understood.
        private static Verdict Classify(List<SqlToken> sig, int e1, int e2)
        {
            for (int i = e1; i <= e2; i++)
                if (sig[i].IsWord("SELECT") || sig[i].IsWord("OVER")) return Verdict.Reject;   // subquery, window function

            bool aggregate = false, column = false;
            var notColumns = new HashSet<int>();   // types and date parts: words that look like columns but are not
            int p = e1;
            while (p <= e2)
            {
                var t = sig[p];
                if (notColumns.Contains(p) || t.Kind == TokenKind.Variable || !InsertValueMap.IsName(t)) { p++; continue; }
                if (t.IsWord("AS")) { MarkType(sig, p + 1, e2, notColumns); p++; continue; }
                if (t.IsWord("COLLATE") && p + 1 <= e2) { notColumns.Add(p + 1); p++; continue; }
                if (t.IsWord("AT") && p + 2 <= e2 && sig[p + 1].IsWord("TIME") && sig[p + 2].IsWord("ZONE")) { p += 3; continue; }

                // name(.name)*
                int j = p;
                while (j + 2 <= e2 && sig[j + 1].IsSymbol('.') && sig[j + 2].Kind != TokenKind.Variable && InsertValueMap.IsName(sig[j + 2])) j += 2;
                if (j + 1 <= e2 && sig[j + 1].IsSymbol('('))
                {
                    int close = SelectListReader.MatchingParenthesis(sig, j + 1);
                    if (close < 0 || close > e2) return Verdict.Reject;
                    if (j != p || t.Kind != TokenKind.Word) return Verdict.Reject;   // dbo.f(…), [f](…), x.value(…): scalar or aggregate?
                    string name = t.Text;
                    if (Aggregates.Contains(name))
                    {
                        if (name.StartsWith("GROUPING", StringComparison.OrdinalIgnoreCase)) return Verdict.Reject;   // only meaningful with ROLLUP / CUBE / GROUPING SETS
                        aggregate = true;
                        int end = close;
                        if (end + 3 <= e2 && sig[end + 1].IsWord("WITHIN") && sig[end + 2].IsWord("GROUP") && sig[end + 3].IsSymbol('('))
                        {
                            end = SelectListReader.MatchingParenthesis(sig, end + 3);
                            if (end < 0 || end > e2) return Verdict.Reject;
                        }
                        p = end + 1;   // what is inside an aggregate is not a column the query is grouped by
                        continue;
                    }
                    if (Scalars.Contains(name))
                    {
                        if (FirstArgumentIsNotAColumn.Contains(name)) MarkFirstArgument(sig, j + 2, close, notColumns);
                        if (name.Equals("TRIM", StringComparison.OrdinalIgnoreCase) && j + 2 < close &&
                            (sig[j + 2].IsWord("LEADING") || sig[j + 2].IsWord("TRAILING") || sig[j + 2].IsWord("BOTH"))) notColumns.Add(j + 2);
                        p = j + 1;
                        continue;
                    }
                    if (SqlKeywords.IsReserved(name)) { p++; continue; }   // IN (…), EXISTS (…), AND (…): an operator
                    return Verdict.Reject;                                  // a function that is not known to be scalar
                }

                if (j == p && t.Kind == TokenKind.Word && SqlKeywords.IsReserved(t.Text)) { p++; continue; }   // CASE, WHEN, NULL, CURRENT_TIMESTAMP …
                column = true;
                p = j + 1;
            }
            if (aggregate) return column ? Verdict.Reject : Verdict.Skip;   // o.Price * SUM(o.Qty) cannot be grouped by
            return column ? Verdict.Include : Verdict.Skip;
        }

        // "AS int", "AS decimal(10, 2)", "AS sys.sysname [USING 'culture']": the type is not a column.
        private static void MarkType(List<SqlToken> sig, int p, int e2, HashSet<int> notColumns)
        {
            if (p > e2) return;
            notColumns.Add(p);
            while (p + 2 <= e2 && sig[p + 1].IsSymbol('.')) { notColumns.Add(p + 1); notColumns.Add(p + 2); p += 2; }
            if (p + 1 <= e2 && sig[p + 1].IsWord("USING")) notColumns.Add(p + 1);
        }

        // Tokens from p up to the first comma outside parentheses, before the call's closing parenthesis.
        private static void MarkFirstArgument(List<SqlToken> sig, int p, int close, HashSet<int> notColumns)
        {
            int depth = 0;
            for (; p < close; p++)
            {
                if (sig[p].IsSymbol('(')) depth++;
                else if (sig[p].IsSymbol(')')) depth--;
                else if (depth == 0 && sig[p].IsSymbol(',')) return;
                notColumns.Add(p);
            }
        }

        // The expression as written. A line break between its tokens becomes one space (the list puts one expression on one line);
        // line breaks inside a string literal are the literal's own and stay. With a comment inside, the text is copied untouched.
        private static string ExpressionText(string text, List<SqlToken> sig, int e1, int e2, out string key)
        {
            var sb = new StringBuilder();
            var k = new StringBuilder();
            bool comment = false;
            for (int i = e1; i <= e2; i++)
            {
                k.Append((int)sig[i].Kind).Append(':').Append(sig[i].Text).Append('\u0001');   // exact tokens: no case folding
                if (i > e1)
                {
                    string gap = text.Substring(sig[i - 1].End, sig[i].Start - sig[i - 1].End);
                    if (gap.Any(c => !char.IsWhiteSpace(c))) comment = true;
                    else if (gap.IndexOf('\n') >= 0 || gap.IndexOf('\r') >= 0) gap = " ";
                    sb.Append(gap);
                }
                sb.Append(sig[i].Text);
            }
            key = k.ToString();
            return comment ? text.Substring(sig[e1].Start, sig[e2].End - sig[e1].Start) : sb.ToString();
        }
    }
}
