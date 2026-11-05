using System;
using System.Collections.Generic;

namespace SqlAssist.Core.Sql
{
    /// <summary>One item of a select list, as indexes into the significant (comment-free) token list. <see cref="Last"/> is
    /// -1 for an empty item (a doubled or trailing comma).</summary>
    internal sealed class SelectListItem
    {
        public int First = -1, Last = -1;
        /// <summary>The comma before / after the item; -1 for the first / last item.</summary>
        public int SeparatorBefore = -1, SeparatorAfter = -1;
    }

    internal sealed class SelectList
    {
        public int SelectToken;
        /// <summary>Text offset just past SELECT [ALL | DISTINCT] [TOP …].</summary>
        public int HeadEnd;
        public List<SelectListItem> Items = new List<SelectListItem>();
        /// <summary>The token that ended the list; the token count when the text ended first.</summary>
        public int EndToken;
        /// <summary>The list ended at a clause of the same statement (FROM, WHERE, …) rather than at a statement boundary or the end of the text.</summary>
        public bool ClauseFollows;
    }

    /// <summary>Reads the item list of one SELECT from the tokens, with the parenthesis depth, so commas inside strings, functions,
    /// CASE, subqueries and comments never split an item. No grammar: it knows where a list starts and which words end it.
    /// Shared by the INSERT … SELECT highlight and the GROUP BY expansion.</summary>
    internal static class SelectListReader
    {
        // Words that end the select list (and are not a statement of their own).
        private static readonly HashSet<string> ListEnd = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FROM", "WHERE", "GROUP", "HAVING", "ORDER", "UNION", "EXCEPT", "INTERSECT", "OPTION", "FOR", "INTO",
        };
        internal static readonly HashSet<string> SetOperators = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UNION", "EXCEPT", "INTERSECT" };
        // "WITH ROLLUP" / "WITH CUBE" continue a GROUP BY; any other WITH followed by a name starts a CTE, i.e. the next statement.
        private static readonly HashSet<string> WithContinues = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ROLLUP", "CUBE", "TIES" };
        private static readonly HashSet<string> JoinWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS" };

        /// <summary>The list of the SELECT at token <paramref name="select"/>; null when it is not understood (an unfinished
        /// head, a stray ")" , nothing typed after SELECT yet).</summary>
        public static SelectList Read(List<SqlToken> sig, int select)
        {
            int n = sig.Count, p = select + 1;
            var list = new SelectList { SelectToken = select, HeadEnd = sig[select].End };

            // SELECT [ALL | DISTINCT] [TOP (n) | TOP n [PERCENT] [WITH TIES]]: not items.
            if (p < n && (sig[p].IsWord("ALL") || sig[p].IsWord("DISTINCT"))) { list.HeadEnd = sig[p].End; p++; }
            if (p < n && sig[p].IsWord("TOP"))
            {
                list.HeadEnd = sig[p].End;
                p++;
                if (p >= n) return null;
                if (sig[p].IsSymbol('('))
                {
                    int close = MatchingParenthesis(sig, p);
                    if (close < 0) return null;
                    list.HeadEnd = sig[close].End;
                    p = close + 1;
                }
                else if (sig[p].Kind == TokenKind.Number || sig[p].Kind == TokenKind.Variable) { list.HeadEnd = sig[p].End; p++; }
                else return null;
                if (p < n && sig[p].IsWord("PERCENT")) { list.HeadEnd = sig[p].End; p++; }
                if (p + 1 < n && sig[p].IsWord("WITH") && sig[p + 1].IsWord("TIES")) { list.HeadEnd = sig[p + 1].End; p += 2; }
            }
            if (p >= n) return null;   // nothing typed after SELECT / TOP yet

            var item = new SelectListItem();
            int depth = 0;
            while (true)
            {
                if (p >= n) { list.Items.Add(item); list.EndToken = n; return list; }
                var t = sig[p];
                if (t.Kind == TokenKind.Symbol)
                {
                    char c = t.Text[0];
                    if (c == ',' && depth == 0)
                    {
                        item.SeparatorAfter = p;
                        list.Items.Add(item);
                        item = new SelectListItem { SeparatorBefore = p };
                        p++;
                        continue;
                    }
                    if (c == ';') { list.Items.Add(item); list.EndToken = p; return list; }
                    if (c == '(') depth++;
                    else if (c == ')')
                    {
                        if (depth == 0) return null;   // a SELECT does not end on a ")" it did not open: not understood
                        depth--;
                    }
                }
                else if (t.Kind == TokenKind.Word && EndsStatement(sig, p, depth)) { list.Items.Add(item); list.EndToken = p; return list; }
                else if (t.Kind == TokenKind.Word && depth == 0 && ListEnd.Contains(t.Text) && !(t.IsWord("GROUP") && p > 0 && sig[p - 1].IsWord("WITHIN")))
                {   // (STRING_AGG(…) WITHIN GROUP (…) is part of an item, not the GROUP BY clause)
                    list.Items.Add(item); list.EndToken = p; list.ClauseFollows = true;
                    return list;
                }
                if (item.First < 0) item.First = p;
                item.Last = p;
                p++;
            }
        }

        /// <summary>Scans the rest of the statement from token <paramref name="from"/>: false when a UNION / EXCEPT / INTERSECT
        /// sits at the statement's own level (the SELECT is only one part of a query expression); otherwise
        /// <paramref name="end"/> is the text offset where the statement ends (the text length when nothing ends it).</summary>
        public static bool TryScanStatementEnd(List<SqlToken> sig, int from, int textLength, out int end, out int endToken)
        {
            end = textLength;
            endToken = sig.Count;
            int depth = 0;
            for (int p = from; p < sig.Count; p++)
            {
                var t = sig[p];
                if (t.Kind == TokenKind.Symbol)
                {
                    char c = t.Text[0];
                    if (c == ';') { end = t.Start; endToken = p; return true; }
                    if (c == '(') depth++;
                    else if (c == ')' && --depth < 0) { end = t.Start; endToken = p; return true; }
                }
                else if (t.Kind == TokenKind.Word)
                {
                    if (depth == 0 && SetOperators.Contains(t.Text)) return false;
                    if (EndsStatement(sig, p, depth)) { end = t.Start; endToken = p; return true; }
                }
            }
            return true;
        }

        /// <summary>Tokens first..last are "*", or "name.*" / "name.name.*".</summary>
        public static bool IsStarItem(List<SqlToken> sig, int first, int last)
        {
            if (first < 0 || last < first || !sig[last].IsSymbol('*')) return false;
            if (first == last) return true;
            if ((last - first) % 2 != 0) return false;
            for (int i = first; i < last; i++)
            {
                bool nameSlot = (i - first) % 2 == 0;
                if (nameSlot ? !InsertValueMap.IsName(sig[i]) : !sig[i].IsSymbol('.')) return false;
            }
            return true;
        }

        public static int MatchingParenthesis(List<SqlToken> sig, int open)
        {
            int depth = 0;
            for (int i = open; i < sig.Count; i++)
            {
                if (sig[i].IsSymbol('(')) depth++;
                else if (sig[i].IsSymbol(')') && --depth == 0) return i;
            }
            return -1;
        }

        /// <summary>A word that cannot belong to this statement: the next one starts there. (A ";" is handled by the callers.)</summary>
        public static bool EndsStatement(List<SqlToken> sig, int p, int depth)
        {
            var t = sig[p];
            if (InsertValueMap.StopAnywhere.Contains(t.Text))
            {
                // "INNER MERGE JOIN" is a join hint, not a MERGE statement.
                return !(t.IsWord("MERGE") && p > 0 && sig[p - 1].Kind == TokenKind.Word && JoinWords.Contains(sig[p - 1].Text));
            }
            if (depth > 0) return false;   // a SELECT / WITH inside parentheses is a subquery / a hint
            if (t.IsWord("SELECT")) return true;
            if (t.IsWord("WITH"))
            {
                // "FROM t WITH (NOLOCK)" and "GROUP BY x WITH ROLLUP" stay; "WITH cte AS (…)" starts the next statement.
                if (p + 1 >= sig.Count) return false;
                var next = sig[p + 1];
                return (next.Kind == TokenKind.Word && !WithContinues.Contains(next.Text)) || next.Kind == TokenKind.QuotedIdentifier;
            }
            return false;
        }
    }
}
