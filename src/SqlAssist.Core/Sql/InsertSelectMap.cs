using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlAssist.Core.Sql
{
    /// <summary>What the INSERT highlight needs from a parsed INSERT statement, whatever feeds it (VALUES rows, a SELECT).</summary>
    public interface IInsertMap
    {
        /// <summary>Offset of the INSERT keyword.</summary>
        int StatementStart { get; }
        /// <summary>Offset where the statement ends (or the end of the text read, for one still open).</summary>
        int StatementEnd { get; }
        /// <summary>The columns and values to highlight for the caret (offsets in the text the map was read from); null when the
        /// caret is not on a column name or in a value, or when there is nothing at that position to pair it with.</summary>
        InsertHighlight At(int caret);
    }

    /// <summary>Entry points that read an INSERT in any form the highlight understands (VALUES first, then SELECT), tokenizing
    /// the text once.</summary>
    public static class InsertMaps
    {
        // Same bound as InsertValueMap.Find: the caret is in the nearest INSERT before it, or in none.
        private const int MaxCandidates = 8;

        /// <summary>The INSERT statement the caret is in, or null when it is in none or the statement is not understood.</summary>
        public static IInsertMap Find(string text, int caret)
        {
            if (string.IsNullOrEmpty(text) || caret < 0 || caret > text.Length) return null;
            var sig = InsertValueMap.Significant(text, out var comments);

            int tried = 0;
            for (int k = sig.Count - 1; k >= 0 && tried < MaxCandidates; k--)
            {
                if (sig[k].Start > caret || !sig[k].IsWord("INSERT")) continue;
                tried++;
                var map = TryParse(sig, k, text, comments);
                if (map != null && caret >= map.StatementStart && caret <= map.StatementEnd) return map;
            }
            return null;
        }

        /// <summary>The INSERT whose keyword starts at <paramref name="insertStart"/>, whatever the caret; null when that is not an
        /// INSERT the parser understands. For callers that keep the result across caret moves.</summary>
        public static IInsertMap Parse(string text, int insertStart)
        {
            if (string.IsNullOrEmpty(text) || insertStart < 0 || insertStart >= text.Length) return null;
            var sig = InsertValueMap.Significant(text, out var comments);
            int k = sig.FindIndex(t => t.Start == insertStart && t.IsWord("INSERT"));
            return k < 0 ? null : TryParse(sig, k, text, comments);
        }

        private static IInsertMap TryParse(List<SqlToken> sig, int k, string text, List<SourceSpan> comments) =>
            (IInsertMap)InsertValueMap.TryParse(sig, k, text.Length, comments) ?? InsertSelectMap.TryParse(sig, k, text, comments);
    }

    /// <summary>"INSERT INTO t (a, b, c) SELECT x, y, z FROM …": which select item feeds which column, by position. Only the outer
    /// SELECT that feeds the INSERT is read — the item list is cut out by walking the tokens with the parenthesis depth, so commas
    /// in strings, functions, CASE, subqueries and comments never split an item, and a SELECT inside a CTE, a derived table or
    /// an item is never mistaken for it. An item is the whole select item, alias included (<c>expr AS a</c>, <c>a = expr</c>,
    /// <c>expr a</c>).
    /// Not understood, so no map at all: UNION / EXCEPT / INTERSECT, OUTPUT before the SELECT, a parenthesized SELECT, INSERT … EXEC,
    /// no column list, a column that is not a plain name. A <c>*</c> or <c>alias.*</c> item is not expanded: items before the first
    /// one are still paired, the star and everything after it (whose position is unknown) are not.</summary>
    public sealed class InsertSelectMap : IInsertMap
    {
        private readonly List<SourceSpan> _comments;
        private readonly int _headEnd;
        private readonly int _starIndex;

        private InsertSelectMap(int start, int end, IReadOnlyList<SourceSpan> columns, IReadOnlyList<InsertValueSlot> items,
                                int headEnd, int starIndex, List<SourceSpan> comments)
        {
            StatementStart = start; StatementEnd = end; Columns = columns; Items = items;
            _headEnd = headEnd; _starIndex = starIndex; _comments = comments;
        }

        public int StatementStart { get; }
        /// <summary>Offset just past the whole statement (the FROM / WHERE … part included), so a reader that cut the text short can tell.</summary>
        public int StatementEnd { get; }
        public IReadOnlyList<SourceSpan> Columns { get; }
        /// <summary>The select items; <see cref="InsertValueSlot.Slot"/> is what the caret may touch, <see cref="InsertValueSlot.Expression"/> the item's tokens.</summary>
        public IReadOnlyList<InsertValueSlot> Items { get; }
        /// <summary>Index of the first <c>*</c> / <c>alias.*</c> item, or -1.</summary>
        public int StarIndex => _starIndex;

        public InsertHighlight At(int caret)
        {
            if (caret < StatementStart || caret > StatementEnd) return null;
            // A comment between the items belongs to nothing.
            foreach (var c in _comments)
                if (caret > c.Start && caret <= c.End) return null;

            for (int i = 0; i < Columns.Count; i++)
            {
                if (!Columns[i].Touches(caret)) continue;
                if (!IsPaired(i) || !Items[i].Expression.HasValue) return null;
                return new InsertHighlight(i, Columns[i], new[] { Items[i].Expression.Value }, true);
            }

            // Right after the SELECT / DISTINCT / TOP keywords the caret is still on the keyword being typed, not on the first item.
            if (caret <= _headEnd) return null;
            for (int j = 0; j < Items.Count; j++)
            {
                if (!Items[j].Slot.Touches(caret)) continue;
                // A item past the last column, or after a star, has no column whose position is certain: no guess.
                if (j >= Columns.Count || !IsPaired(j)) return null;
                var expression = Items[j].Expression;
                return new InsertHighlight(j, Columns[j], expression.HasValue ? new[] { expression.Value } : new SourceSpan[0], false);
            }
            return null;
        }

        private bool IsPaired(int index) => index < Items.Count && (_starIndex < 0 || index < _starIndex);

        internal static InsertSelectMap TryParse(List<SqlToken> sig, int k, string text, List<SourceSpan> comments)
        {
            if (!InsertValueMap.TryReadTarget(sig, k, out var columns, out int p)) return null;
            if (p >= sig.Count || !sig[p].IsWord("SELECT")) return null;

            var list = SelectListReader.Read(sig, p);
            if (list == null) return null;

            // The rest of the statement is only scanned for what makes it unsupported (a UNION) and for where it ends.
            int limit = list.EndToken < sig.Count ? sig[list.EndToken].Start : text.Length;
            int end = limit;
            if (list.ClauseFollows && !SelectListReader.TryScanStatementEnd(sig, list.EndToken, text.Length, out end, out _)) return null;

            var items = new List<InsertValueSlot>();
            int starIndex = -1;
            foreach (var item in list.Items)
            {
                int slotStart = item.SeparatorBefore >= 0 ? sig[item.SeparatorBefore].End : list.HeadEnd;
                int slotEnd = item.SeparatorAfter >= 0
                    ? sig[item.SeparatorAfter].Start
                    : TrailingEnd(text, slotStart, item.Last >= 0 ? sig[item.Last].End : -1, limit);
                if (starIndex < 0 && SelectListReader.IsStarItem(sig, item.First, item.Last)) starIndex = items.Count;
                SourceSpan? expression = item.First < 0 ? (SourceSpan?)null : new SourceSpan(sig[item.First].Start, sig[item.Last].End);
                items.Add(new InsertValueSlot(new SourceSpan(slotStart, slotEnd), expression));
            }
            return new InsertSelectMap(sig[k].Start, end, columns, items, list.HeadEnd, starIndex, comments);
        }

        // Where the last item's slot ends: its tokens plus the spaces after them on the same line, never the line break and the
        // indentation of the clause below (a caret at the start of the FROM line is not on the last item).
        private static int TrailingEnd(string text, int slotStart, int lastTokenEnd, int limit)
        {
            int at = Math.Max(slotStart, lastTokenEnd);
            while (at < limit && (text[at] == ' ' || text[at] == '\t')) at++;
            return Math.Min(at, limit);
        }
    }
}
