using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Formatting
{
    /// <summary>Lays statements out. Queries (SELECT, INSERT, UPDATE, DELETE, MERGE, with or without a CTE) are rebuilt clause by
    /// clause; IF / WHILE / BEGIN / END shape the indentation; every other statement keeps its own line breaks and only has
    /// its spacing, indentation and keyword case tidied. Nothing is ever added or removed but whitespace and, for keywords,
    /// case (and a comma may move from the end of one line to the start of the next).</summary>
    internal sealed class FormatLayout
    {
        /// <summary>A parenthesized group longer than this (written on one line) may be wrapped.</summary>
        private const int MaxInlineWidth = 80;

        private enum Wrap { None, Commas, Conditions }

        private enum ClauseKind
        {
            Header, Select, Into, From, Where, GroupBy, Having, OrderBy, Offset, SetOp, Set, Values, Output, Option, For, Exec,
            Using, MergeOn, MergeWhen,
        }

        private sealed class Clause
        {
            public ClauseKind Kind;
            public readonly List<TokItem> Keywords = new List<TokItem>();
            public readonly List<Item> Body = new List<Item>();
            public readonly List<TokItem> LeadingComments = new List<TokItem>();
        }

        private sealed class ListItem
        {
            public readonly List<TokItem> LeadingComments = new List<TokItem>();
            public readonly List<Item> Content = new List<Item>();
            public readonly List<TokItem> TrailingComments = new List<TokItem>();
        }

        private sealed class ExprOptions
        {
            public Wrap? GroupHint;
            public bool SpaceBeforeGroupAfterName;
        }

        private static readonly FToken Comma = FToken.Synthetic(",");

        private readonly ScanResult _scan;
        private readonly IReadOnlyList<FToken> _tok;
        private readonly FormatOptions _options;
        private readonly int _u;
        private readonly bool _leading;
        private readonly string _nl;
        private readonly SqlWriter _w;

        public FormatLayout(ScanResult scan, IReadOnlyList<FToken> tokens, FormatOptions options, string newline, int baseIndent)
        {
            _scan = scan;
            _tok = tokens;
            _options = options;
            _u = options.EffectiveIndentSize;
            _leading = options.CommaStyle == CommaStyle.Leading;
            _nl = newline;
            _w = new SqlWriter(newline, _u, baseIndent);
        }

        public string Run()
        {
            bool first = true;
            foreach (var batch in _scan.Batches)
            {
                var flow = new Flow();
                foreach (var statement in batch.Statements)
                {
                    bool elseIf = statement.Kind == StatementKind.Else && SecondWordIs(statement, "IF");
                    int level = flow.LevelFor(statement.Kind, elseIf);
                    Separate(statement.FirstToken, level * _u, ref first);
                    Statement(statement, level);
                }
                if (batch.HasGo)
                {
                    Separate(batch.GoToken, 0, ref first);
                    for (int i = batch.GoToken; i <= batch.GoLastToken; i++) _w.Token(_tok[i]);
                }
            }
            return _w.Result();
        }

        private bool SecondWordIs(SqlStatement s, string word)
        {
            for (int i = s.FirstSignificant + 1; i <= s.LastToken; i++)
            {
                if (_tok[i].IsComment) continue;
                return _tok[i].IsWord(word);
            }
            return false;
        }

        /// <summary>Starts a new line for the token at <paramref name="tokenIndex"/>, keeping one blank line if the text had one or more.</summary>
        private void Separate(int tokenIndex, int indent, ref bool first)
        {
            if (!first && _tok[tokenIndex].NewlinesBefore >= 2) _w.BlankLine();
            _w.NewLine(indent);
            first = false;
        }

        /// <summary>Where each statement goes: inside BEGIN … END blocks, under an IF / ELSE / WHILE header, and ELSE back under its IF.</summary>
        private sealed class Flow
        {
            private readonly Stack<int> _blocks = new Stack<int>();
            private readonly Stack<int> _ifs = new Stack<int>();
            private int _header = -1;

            public int LevelFor(StatementKind kind, bool elseIf)
            {
                int inBlock = _blocks.Count > 0 ? _blocks.Peek() + 1 : 0;
                int body = _header >= 0 ? _header + 1 : inBlock;
                int level;
                switch (kind)
                {
                    case StatementKind.End:
                        level = _blocks.Count > 0 ? _blocks.Pop() : inBlock;
                        while (_ifs.Count > 0 && _ifs.Peek() > level) _ifs.Pop();
                        _header = -1;
                        return level;
                    case StatementKind.Begin:
                        level = _header >= 0 ? _header : inBlock;
                        _blocks.Push(level);
                        _header = -1;
                        return level;
                    case StatementKind.If:
                        level = body;
                        _ifs.Push(level);
                        _header = level;
                        return level;
                    case StatementKind.Else:
                        level = _ifs.Count > 0 ? _ifs.Pop() : body;
                        if (elseIf) _ifs.Push(level);
                        _header = level;
                        return level;
                    case StatementKind.While:
                        level = body;
                        _header = level;
                        return level;
                    default:
                        _header = -1;
                        return body;
                }
            }
        }

        // ---- statements ----

        private void Statement(SqlStatement s, int level)
        {
            var items = ItemBuilder.Build(_tok, s.FirstToken, s.LastToken);
            // Comments in front of an END sit inside the block it closes.
            int commentLevel = s.Kind == StatementKind.End ? level + 1 : level;
            int start = 0;
            while (start < items.Count && items[start] is TokItem lead && lead.IsComment)
            {
                if (start > 0)
                {
                    if (lead.T.NewlinesBefore >= 2) _w.BlankLine();
                    _w.NewLine(commentLevel * _u);
                }
                else if (commentLevel != level) _w.NewLine(commentLevel * _u);
                _w.Token(lead.T);
                start++;
            }
            if (start == items.Count) return;
            if (start > 0) _w.NewLine(level * _u);
            var rest = start == 0 ? items : items.GetRange(start, items.Count - start);

            var kind = s.Kind;
            if (kind == StatementKind.Other && rest[0] is TokItem first && first.T.Kind == TokenKind.Word && QueryClauseWords.Contains(first.T.Upper))
                kind = StatementKind.Select;   // a selection that starts at FROM or WHERE is still part of a query
            switch (kind)
            {
                case StatementKind.Select:
                case StatementKind.Insert:
                case StatementKind.Update:
                case StatementKind.Delete:
                case StatementKind.Merge:
                    Query(rest, kind, level);
                    break;
                case StatementKind.If:
                case StatementKind.Else:
                case StatementKind.While:
                case StatementKind.Begin:
                case StatementKind.End:
                    Expr(rest);
                    break;
                default:
                    GenericItems(rest, level, 0, kind);
                    break;
            }
        }

        private static readonly HashSet<string> QueryClauseWords = new HashSet<string>
            { "FROM", "WHERE", "GROUP", "HAVING", "ORDER", "UNION", "EXCEPT", "INTERSECT", "INTO" };

        // ---- queries ----

        private void Query(List<Item> items, StatementKind kind, int level)
        {
            items = CtePrefix(items, level);
            if (items.Count == 0) return;
            foreach (var clause in SplitClauses(items, kind))
                RenderClause(clause, kind, level);
        }

        /// <summary>WITH a AS (…), b AS (…): writes it and returns what follows. Anything that does not look like a CTE list is
        /// left for the clause splitter, which will print it as an ordinary run of tokens.</summary>
        private List<Item> CtePrefix(List<Item> items, int level)
        {
            if (items.Count == 0 || !(items[0] is TokItem with) || !with.IsWord("WITH")) return items;

            var defs = new List<(List<TokItem> name, GroupItem columns, TokItem asWord, GroupItem body)>();
            int i = 1;
            while (true)
            {
                var name = new List<TokItem>();
                while (i < items.Count && items[i] is TokItem n && !n.IsWord("AS") && !n.IsComment && n.T.Kind != TokenKind.Symbol) { name.Add(n); i++; }
                GroupItem columns = null;
                if (i < items.Count && items[i] is GroupItem c) { columns = c; i++; }
                if (name.Count == 0 || i + 1 >= items.Count || !(items[i] is TokItem asWord) || !asWord.IsWord("AS") || !(items[i + 1] is GroupItem body))
                    return items;
                defs.Add((name, columns, asWord, body));
                i += 2;
                if (i < items.Count && items[i] is TokItem comma && comma.IsSym(",")) { i++; continue; }
                break;
            }

            _w.Token(with.T);
            for (int d = 0; d < defs.Count; d++)
            {
                if (d > 0)
                {
                    _w.NewLine(level * _u);
                    if (_leading) _w.Token(Comma);
                }
                foreach (var part in defs[d].name) _w.Token(part.T);
                if (defs[d].columns != null)
                {
                    _w.ForceSpace();
                    Group(defs[d].columns, Wrap.Commas);
                }
                _w.Token(defs[d].asWord.T);
                Group(defs[d].body, Wrap.None);
                if (!_leading && d < defs.Count - 1) _w.Token(Comma);
            }
            _w.NewLine(level * _u);
            return items.GetRange(i, items.Count - i);
        }

        private List<Clause> SplitClauses(List<Item> items, StatementKind kind)
        {
            var clauses = new List<Clause>();
            var current = new Clause { Kind = ClauseKind.Header };
            bool seenSet = false, seenUsing = false, seenMergeOn = false;

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is TokItem ti && ti.T.Kind == TokenKind.Word)
                {
                    int keywords;
                    var started = ClauseStart(items, i, kind, current, seenSet, seenUsing, seenMergeOn, out keywords);
                    if (started != null)
                    {
                        if (started == ClauseKind.Set) seenSet = true;
                        if (started == ClauseKind.Using) seenUsing = true;
                        if (started == ClauseKind.MergeOn) seenMergeOn = true;
                        var moved = TakeOwnLineCommentsAtEnd(current.Body);
                        if (current.Keywords.Count > 0 || current.Body.Count > 0 || current.LeadingComments.Count > 0) clauses.Add(current);
                        current = new Clause { Kind = started.Value };
                        current.LeadingComments.AddRange(moved);
                        for (int k = 0; k < keywords; k++) current.Keywords.Add((TokItem)items[i + k]);
                        i += keywords - 1;
                        continue;
                    }
                }
                current.Body.Add(items[i]);
            }
            clauses.Add(current);
            return clauses;
        }

        private ClauseKind? ClauseStart(List<Item> items, int i, StatementKind kind, Clause current, bool seenSet, bool seenUsing, bool seenMergeOn, out int keywords)
        {
            keywords = 1;
            string u = ((TokItem)items[i]).T.Upper;
            var next = NextSignificant(items, i);
            switch (u)
            {
                case "SELECT": return ClauseKind.Select;
                case "INTO": return current.Kind == ClauseKind.Select ? ClauseKind.Into : (ClauseKind?)null;
                case "FROM":
                    // DELETE FROM t (or DELETE TOP (n) FROM t) is one line; DELETE t FROM … has a FROM clause.
                    if (kind == StatementKind.Delete && current.Kind == ClauseKind.Header && IsBareDelete(current.Body)) return null;
                    return ClauseKind.From;
                case "WHERE": return ClauseKind.Where;
                case "HAVING": return ClauseKind.Having;
                case "GROUP":
                    if (next is TokItem g && g.IsWord("BY")) { keywords = NextIndex(items, i) - i + 1; return ClauseKind.GroupBy; }
                    return null;
                case "ORDER":
                    if (next is TokItem o && o.IsWord("BY")) { keywords = NextIndex(items, i) - i + 1; return ClauseKind.OrderBy; }
                    return null;
                case "UNION":
                case "EXCEPT":
                case "INTERSECT":
                    if (next is TokItem all && all.IsWord("ALL")) keywords = NextIndex(items, i) - i + 1;
                    return ClauseKind.SetOp;
                case "OFFSET":
                    return current.Kind == ClauseKind.OrderBy && (next is GroupItem || (next is TokItem n && (n.T.Kind == TokenKind.Number || n.T.Kind == TokenKind.Variable)))
                        ? ClauseKind.Offset : (ClauseKind?)null;
                case "SET": return kind == StatementKind.Update && !seenSet ? ClauseKind.Set : (ClauseKind?)null;
                case "VALUES":
                    return kind == StatementKind.Insert && !PreviousIsWord(items, i, "DEFAULT") ? ClauseKind.Values : (ClauseKind?)null;
                case "OUTPUT":
                    return kind == StatementKind.Insert || kind == StatementKind.Update || kind == StatementKind.Delete || kind == StatementKind.Merge
                        ? ClauseKind.Output : (ClauseKind?)null;
                case "OPTION": return next is GroupItem ? ClauseKind.Option : (ClauseKind?)null;
                case "FOR":
                    return next is TokItem f && (f.IsWord("XML") || f.IsWord("JSON") || f.IsWord("BROWSE")) ? ClauseKind.For : (ClauseKind?)null;
                case "EXEC":
                case "EXECUTE":
                    return kind == StatementKind.Insert && current.Kind == ClauseKind.Header ? ClauseKind.Exec : (ClauseKind?)null;
                case "USING": return kind == StatementKind.Merge && !seenUsing ? ClauseKind.Using : (ClauseKind?)null;
                case "ON": return kind == StatementKind.Merge && seenUsing && !seenMergeOn && current.Kind == ClauseKind.Using ? ClauseKind.MergeOn : (ClauseKind?)null;
                case "WHEN": return kind == StatementKind.Merge ? ClauseKind.MergeWhen : (ClauseKind?)null;
                default: return null;
            }
        }

        /// <summary>Only DELETE, optionally with TOP and its count, so far.</summary>
        private static bool IsBareDelete(List<Item> body)
        {
            var words = body.Where(i => !(i is TokItem t && t.IsComment)).ToList();
            if (words.Count == 0 || !IsWordItem(words[0], "DELETE")) return false;
            if (words.Count == 1) return true;
            return words.Count == 3 && IsWordItem(words[1], "TOP") &&
                   (words[2] is GroupItem || (words[2] is TokItem v && (v.T.Kind == TokenKind.Number || v.T.Kind == TokenKind.Variable)));
        }

        private void RenderClause(Clause clause, StatementKind kind, int level)
        {
            int indent = (clause.Kind == ClauseKind.MergeOn ? level + 1 : level) * _u;
            foreach (var c in clause.LeadingComments)
            {
                _w.NewLine(level * _u);
                _w.Token(c.T);
            }
            _w.NewLine(indent);
            foreach (var k in clause.Keywords) _w.Token(k.T);
            var body = clause.Body;
            // A comment written right after the keyword stays there, whatever follows is laid out under it.
            if (clause.Keywords.Count > 0)
                while (body.Count > 0 && body[0] is TokItem comment && comment.IsComment && comment.T.NewlinesBefore == 0)
                {
                    _w.Token(comment.T);
                    body = body.GetRange(1, body.Count - 1);
                }

            switch (clause.Kind)
            {
                case ClauseKind.Header:
                    Expr(body, kind == StatementKind.Insert ? new ExprOptions { GroupHint = Wrap.Commas, SpaceBeforeGroupAfterName = true } : null);
                    break;
                case ClauseKind.Select:
                    SelectBody(body, level);
                    break;
                case ClauseKind.From:
                    FromBody(body, level);
                    break;
                case ClauseKind.Where:
                case ClauseKind.Having:
                    Conditions(body, (level + 1) * _u);
                    break;
                case ClauseKind.MergeOn:
                    Conditions(body, (level + 2) * _u);
                    break;
                case ClauseKind.GroupBy:
                case ClauseKind.OrderBy:
                case ClauseKind.Set:
                    ListBody(SplitList(body), level, null);
                    break;
                case ClauseKind.Values:
                    ListBody(SplitList(body), level, new ExprOptions { GroupHint = Wrap.Commas });
                    break;
                case ClauseKind.MergeWhen:
                    MergeWhenBody(body, level);
                    break;
                default:
                    Expr(body);
                    break;
            }
        }

        private void MergeWhenBody(List<Item> body, int level)
        {
            int then = body.FindIndex(it => it is TokItem t && t.IsWord("THEN"));
            if (then < 0) { Expr(body); return; }
            Expr(body.GetRange(0, then + 1));
            var action = body.GetRange(then + 1, body.Count - then - 1);
            // A comment on the THEN line stays there.
            while (action.Count > 0 && action[0] is TokItem comment && comment.IsComment && comment.T.NewlinesBefore == 0)
            {
                _w.Token(comment.T);
                action.RemoveAt(0);
            }
            if (action.Count == 0) return;
            _w.NewLine((level + 1) * _u);
            Expr(action);
        }

        // ---- SELECT list, GROUP BY, ORDER BY, SET, VALUES ----

        private void SelectBody(List<Item> body, int level)
        {
            int mods = 0;
            if (mods < body.Count && IsWordItem(body[mods], "DISTINCT") || mods < body.Count && IsWordItem(body[mods], "ALL")) mods++;
            if (mods < body.Count && IsWordItem(body[mods], "TOP"))
            {
                mods++;
                if (mods < body.Count && (body[mods] is GroupItem || (body[mods] is TokItem v && (v.T.Kind == TokenKind.Number || v.T.Kind == TokenKind.Variable)))) mods++;
                if (mods < body.Count && IsWordItem(body[mods], "PERCENT")) mods++;
                if (mods + 1 < body.Count && IsWordItem(body[mods], "WITH") && IsWordItem(body[mods + 1], "TIES")) mods += 2;
            }
            if (mods > 0) Expr(body.GetRange(0, mods));
            ListBody(SplitList(body.GetRange(mods, body.Count - mods)), level, null);
        }

        /// <summary>One item stays on the clause's line; two or more go one per line, indented under the clause.</summary>
        private void ListBody(List<ListItem> list, int level, ExprOptions options)
        {
            if (list.Count == 0) return;
            if (list.Count == 1 && !IsMultiLine(list[0]))
            {
                Expr(list[0].Content, options);
                foreach (var c in list[0].TrailingComments) _w.Token(c.T);
                return;
            }
            WriteList(list, (level + 1) * _u, options);
        }

        private void WriteList(List<ListItem> list, int itemIndent, ExprOptions options)
        {
            for (int k = 0; k < list.Count; k++)
            {
                var item = list[k];
                foreach (var c in item.LeadingComments)
                {
                    _w.NewLine(itemIndent, itemIndent);
                    _w.Token(c.T);
                }
                if (_leading && k > 0)
                {
                    _w.NewLine(Math.Max(0, itemIndent - 2), itemIndent);
                    _w.Token(Comma);
                }
                else _w.NewLine(itemIndent, itemIndent);
                Expr(item.Content, options);
                if (!_leading && k < list.Count - 1) _w.Token(Comma);
                foreach (var c in item.TrailingComments) _w.Token(c.T);
            }
        }

        /// <summary>Splits at the commas of this level. A comment on the comma's own line belongs to the item before it; a
        /// comment on a line of its own, to the item after.</summary>
        private List<ListItem> SplitList(List<Item> items)
        {
            var list = new List<ListItem>();
            var current = new ListItem();
            foreach (var item in items)
            {
                if (item is TokItem t && t.IsSym(","))
                {
                    // Comments on lines of their own belong to the next item whichever side of the comma they were written,
                    // so moving the comma to the other end of its line leaves them where they are.
                    var moved = TakeOwnLineCommentsAtEnd(current.Content);
                    list.Add(current);
                    current = new ListItem();
                    current.LeadingComments.AddRange(moved);
                    continue;
                }
                if (item is TokItem c && c.IsComment && current.Content.Count == 0)
                {
                    if (c.T.NewlinesBefore > 0 || list.Count == 0) current.LeadingComments.Add(c);
                    else list[list.Count - 1].TrailingComments.Add(c);
                    continue;
                }
                current.Content.Add(item);
            }
            list.Add(current);
            // A trailing comma or a list that is only comments leaves empty items; the comments go to a neighbour.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Content.Count > 0) continue;
                if (i > 0) list[i - 1].TrailingComments.AddRange(list[i].LeadingComments.Concat(list[i].TrailingComments));
                if (list.Count > 1) list.RemoveAt(i);
            }
            if (list.Count == 1 && list[0].Content.Count == 0 && list[0].LeadingComments.Count == 0 && list[0].TrailingComments.Count == 0) list.Clear();
            return list;
        }

        // ---- FROM ----

        private static readonly HashSet<string> JoinModifiers = new HashSet<string>
            { "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER", "LOOP", "HASH", "MERGE", "REMOTE", "NATURAL" };

        private sealed class Join
        {
            public readonly List<TokItem> LeadingComments = new List<TokItem>();
            public readonly List<TokItem> Header = new List<TokItem>();
            public readonly List<Item> Source = new List<Item>();
            public TokItem OnWord;
            public List<Item> On;
        }

        private void FromBody(List<Item> body, int level)
        {
            var sources = SplitList(body);
            if (sources.Count <= 1)
            {
                FromSource(body, level);
                return;
            }
            int itemIndent = (level + 1) * _u;
            for (int k = 0; k < sources.Count; k++)
            {
                var source = sources[k];
                foreach (var c in source.LeadingComments) { _w.NewLine(itemIndent, itemIndent); _w.Token(c.T); }
                if (_leading && k > 0) { _w.NewLine(Math.Max(0, itemIndent - 2), itemIndent); _w.Token(Comma); }
                else _w.NewLine(itemIndent, itemIndent);
                FromSource(source.Content, level + 1);
                if (!_leading && k < sources.Count - 1) _w.Token(Comma);
                foreach (var c in source.TrailingComments) _w.Token(c.T);
            }
        }

        /// <summary>A table source and the joins that follow it; each join starts a line at <paramref name="level"/>.</summary>
        private void FromSource(List<Item> items, int level)
        {
            var source = new List<Item>();
            var joins = new List<Join>();
            Join join = null;
            bool inOn = false;

            for (int i = 0; i < items.Count; i++)
            {
                int headerEnd = JoinStart(items, i);
                if (headerEnd >= 0)
                {
                    var moved = TakeOwnLineCommentsAtEnd(join == null ? source : inOn ? join.On : join.Source);
                    join = new Join();
                    join.LeadingComments.AddRange(moved);
                    for (int k = i; k <= headerEnd; k++) join.Header.Add((TokItem)items[k]);
                    joins.Add(join);
                    inOn = false;
                    i = headerEnd;
                    continue;
                }
                if (join != null && !inOn && IsWordItem(items[i], "ON"))
                {
                    join.OnWord = (TokItem)items[i];
                    join.On = new List<Item>();
                    inOn = true;
                    continue;
                }
                (join == null ? source : inOn ? join.On : join.Source).Add(items[i]);
            }

            Expr(source);
            foreach (var j in joins)
            {
                foreach (var c in j.LeadingComments) { _w.NewLine(level * _u); _w.Token(c.T); }
                _w.NewLine(level * _u);
                foreach (var h in j.Header) _w.Token(h.T);
                Expr(j.Source);
                if (j.On != null)
                {
                    _w.NewLine((level + 1) * _u);
                    _w.Token(j.OnWord.T);
                    Conditions(j.On, (level + 2) * _u);
                }
            }
        }

        /// <summary>[INNER|LEFT|…] [OUTER] [hint] JOIN / APPLY starting at <paramref name="i"/>: the index of its last word, or -1.</summary>
        private static int JoinStart(List<Item> items, int i)
        {
            int j = i;
            while (j < items.Count && items[j] is TokItem t && t.T.Kind == TokenKind.Word && JoinModifiers.Contains(t.T.Upper)) j++;
            return j < items.Count && items[j] is TokItem end && (end.IsWord("JOIN") || end.IsWord("APPLY")) ? j : -1;
        }

        // ---- conditions ----

        /// <summary>The first condition continues the clause's line; each AND / OR after it starts a line at <paramref name="continuation"/>.</summary>
        private void Conditions(List<Item> items, int continuation)
        {
            var parts = SplitConditions(items);
            for (int k = 0; k < parts.Count; k++)
            {
                if (k > 0)
                {
                    foreach (var c in parts[k].LeadingComments) { _w.NewLine(continuation, continuation); _w.Token(c.T); }
                    _w.NewLine(continuation, continuation);
                }
                Expr(parts[k].Content);
            }
        }

        private List<ListItem> SplitConditions(List<Item> items)
        {
            var parts = new List<ListItem>();
            var current = new ListItem();
            bool between = false;
            foreach (var item in items)
            {
                if (item is TokItem t && t.T.Kind == TokenKind.Word)
                {
                    if (t.IsWord("BETWEEN")) between = true;
                    else if (t.IsWord("AND") && between) between = false;
                    else if (t.IsWord("AND") || t.IsWord("OR"))
                    {
                        parts.Add(current);
                        var moved = TakeOwnLineCommentsAtEnd(current.Content);
                        current = new ListItem();
                        current.LeadingComments.AddRange(moved);
                    }
                }
                current.Content.Add(item);
            }
            parts.Add(current);
            return parts;
        }

        // ---- expressions ----

        private void Expr(List<Item> items, ExprOptions options = null)
        {
            Item previous = null;
            foreach (var item in items)
            {
                switch (item)
                {
                    case TokItem t:
                        _w.Token(t.T);
                        break;
                    case GroupItem g:
                        if (options != null && options.SpaceBeforeGroupAfterName && previous is TokItem name &&
                            (name.T.Kind == TokenKind.QuotedIdentifier || (name.T.Kind == TokenKind.Word && !name.T.IsWord("VALUES"))))
                            _w.ForceSpace();
                        Group(g, options?.GroupHint ?? InferWrap(previous));
                        break;
                    case CaseItem c:
                        Case(c);
                        break;
                }
                if (!(item is TokItem ti && ti.IsComment)) previous = item;
            }
        }

        private static Wrap InferWrap(Item previous)
        {
            if (previous == null) return Wrap.Conditions;
            if (previous is TokItem t)
            {
                if (t.T.Kind == TokenKind.Symbol) return Wrap.Conditions;
                if (t.T.Kind == TokenKind.Word)
                {
                    string u = t.T.Upper;
                    if (u == "IN" || u == "VALUES") return Wrap.Commas;
                    if (SqlKeywords.IsReserved(u) && !IsFunctionLikeReserved(u)) return Wrap.Conditions;
                }
            }
            return Wrap.None;
        }

        private static bool IsFunctionLikeReserved(string u) =>
            u == "COALESCE" || u == "CONVERT" || u == "NULLIF" || u == "LEFT" || u == "RIGHT" || u == "TRY_CONVERT" || u == "IDENTITY";

        private void Group(GroupItem g, Wrap hint)
        {
            if (IsSubquery(g))
            {
                _w.Token(g.Open);
                int b = _w.NestedBase;
                _w.NewLine(b + _u, b + _u);
                Subquery(g.Children, (b + _u) / _u);
                _w.NewLine(b, b);
                _w.Token(g.Close);
                return;
            }

            if (hint != Wrap.None && !ContainsLineComment(g.Children) && FlatLength(g) > MaxInlineWidth)
            {
                if (hint == Wrap.Commas && g.Children.Any(i => i is TokItem t && t.IsSym(",")))
                {
                    _w.Token(g.Open);
                    int b = _w.NestedBase;
                    WriteList(SplitList(g.Children), b + _u, null);
                    _w.NewLine(b, b);
                    _w.Token(g.Close);
                    return;
                }
                if (hint == Wrap.Conditions && g.Children.Any(i => i is TokItem t && (t.IsWord("AND") || t.IsWord("OR"))))
                {
                    _w.Token(g.Open);
                    int b = _w.NestedBase;
                    foreach (var part in SplitConditions(g.Children))
                    {
                        foreach (var c in part.LeadingComments) { _w.NewLine(b + _u, b + _u); _w.Token(c.T); }
                        _w.NewLine(b + _u, b + _u);
                        Expr(part.Content);
                    }
                    _w.NewLine(b, b);
                    _w.Token(g.Close);
                    return;
                }
            }
            _w.Token(g.Open);
            Expr(g.Children);
            _w.Token(g.Close);
        }

        private void Subquery(List<Item> children, int level)
        {
            int start = 0;
            while (start < children.Count && children[start] is TokItem lead && lead.IsComment)
            {
                if (start > 0) _w.NewLine(level * _u);
                _w.Token(lead.T);
                start++;
            }
            if (start > 0) _w.NewLine(level * _u);
            Query(start == 0 ? children : children.GetRange(start, children.Count - start), StatementKind.Select, level);
        }

        private static bool IsSubquery(GroupItem g)
        {
            var first = g.Children.FirstOrDefault(i => !(i is TokItem t && t.IsComment));
            return first is TokItem w && (w.IsWord("SELECT") || w.IsWord("WITH"));
        }

        private void Case(CaseItem c)
        {
            var children = c.Children;
            int end = children.Count - 1;                        // the END token
            var input = new List<Item>();
            var clauses = new List<List<Item>>();
            List<Item> current = input;
            for (int i = 1; i < end; i++)
            {
                if (children[i] is TokItem t && (t.IsWord("WHEN") || t.IsWord("ELSE")))
                {
                    current = new List<Item>();
                    clauses.Add(current);
                }
                current.Add(children[i]);
            }
            int whens = clauses.Count(cl => IsWordItem(cl[0], "WHEN"));

            _w.Token(((TokItem)children[0]).T);
            Expr(input);
            if (whens < 2)
            {
                foreach (var cl in clauses) Expr(cl);
                _w.Token(((TokItem)children[end]).T);
                return;
            }
            int b = _w.NestedBase;
            foreach (var cl in clauses)
            {
                _w.NewLine(b + _u, b + _u);
                Expr(cl);
            }
            _w.NewLine(b, b);
            _w.Token(((TokItem)children[end]).T);
        }

        // ---- everything else ----

        /// <summary>Spacing and keyword case tidied, line breaks kept: a line that began a line in the source begins one here, one
        /// level in (two for something inside parentheses), and a closing parenthesis that began a line lines up with its statement.</summary>
        private void GenericItems(List<Item> items, int level, int depth, StatementKind kind)
        {
            Item previous = null;
            foreach (var item in items)
            {
                switch (item)
                {
                    case TokItem t:
                        // The AS that ends a procedure, view or function header lines up with CREATE.
                        bool headerAs = depth == 0 && (kind == StatementKind.Create || kind == StatementKind.Alter) && t.T.IsWord("AS");
                        BreakIfAsWritten(t.T, level * _u + (headerAs ? 0 : Math.Max(depth, 1) * _u));
                        _w.Token(t.T);
                        break;
                    case GroupItem g:
                        BreakIfAsWritten(g.Open, level * _u + Math.Max(depth, 1) * _u);
                        if (depth == 0 && (kind == StatementKind.Create || kind == StatementKind.Alter) && previous is TokItem name &&
                            (name.T.Kind == TokenKind.QuotedIdentifier ||
                             (name.T.Kind == TokenKind.Word && !SqlKeywords.IsReserved(name.T.Raw.Text) && !KeywordCaser.IsFunctionOrType(name.T.Upper))))
                            _w.ForceSpace();
                        _w.Token(g.Open);
                        GenericItems(g.Children, level, depth + 1, kind);
                        BreakIfAsWritten(g.Close, level * _u + depth * _u);
                        _w.Token(g.Close);
                        break;
                    case CaseItem c:
                        GenericItems(c.Children, level, depth + 1, kind);
                        break;
                }
                if (!(item is TokItem ti && ti.IsComment)) previous = item;
            }
        }

        private void BreakIfAsWritten(FToken t, int indent)
        {
            if (t.NewlinesBefore == 0 || _w.AtLineStart) return;
            if (t.NewlinesBefore >= 2) _w.BlankLine();
            _w.NewLine(indent);
        }

        // ---- helpers ----

        private static bool IsWordItem(Item item, string word) => item is TokItem t && t.IsWord(word);

        private static Item NextSignificant(List<Item> items, int i)
        {
            for (int j = i + 1; j < items.Count; j++)
                if (!(items[j] is TokItem t && t.IsComment)) return items[j];
            return null;
        }

        private static int NextIndex(List<Item> items, int i)
        {
            for (int j = i + 1; j < items.Count; j++)
                if (!(items[j] is TokItem t && t.IsComment)) return j;
            return i;
        }

        private static bool PreviousIsWord(List<Item> items, int i, string word)
        {
            for (int j = i - 1; j >= 0; j--)
                if (!(items[j] is TokItem t && t.IsComment)) return IsWordItem(items[j], word);
            return false;
        }

        /// <summary>Removes the comments that end a list and sit on lines of their own (from the first such comment on).</summary>
        private static List<TokItem> TakeOwnLineCommentsAtEnd(List<Item> items)
        {
            int start = items.Count;
            for (int i = items.Count - 1; i >= 0 && items[i] is TokItem t && t.IsComment; i--)
                if (t.T.NewlinesBefore > 0) start = i;
            var taken = new List<TokItem>();
            if (start == items.Count) return taken;
            for (int i = start; i < items.Count; i++)
                if (items[i] is TokItem t) taken.Add(t);
            items.RemoveRange(start, items.Count - start);
            return taken;
        }

        private bool IsMultiLine(ListItem item)
        {
            if (item.LeadingComments.Count > 0) return true;
            return ContainsMultiLine(item.Content);
        }

        /// <summary>Would this render on more than one line by itself: a subquery, a CASE with several WHENs, a comment across lines.</summary>
        private static bool ContainsMultiLine(List<Item> items)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case TokItem t when t.T.IsBlockComment && (t.T.Text.IndexOf('\n') >= 0 || t.T.Text.IndexOf('\r') >= 0): return true;
                    case GroupItem g when IsSubquery(g) || ContainsMultiLine(g.Children): return true;
                    case CaseItem c when c.Children.OfType<TokItem>().Count(w => w.IsWord("WHEN")) >= 2 || ContainsMultiLine(c.Children.Where(x => !(x is TokItem)).ToList()): return true;
                }
            }
            return false;
        }

        private static bool ContainsLineComment(List<Item> items)
        {
            foreach (var item in items)
            {
                if (item is TokItem t && t.T.IsLineComment) return true;
                if (item is GroupItem g && ContainsLineComment(g.Children)) return true;
                if (item is CaseItem c && ContainsLineComment(c.Children)) return true;
            }
            return false;
        }

        private int FlatLength(GroupItem g)
        {
            var flat = new SqlWriter(_nl, _u, 0, flat: true);
            FlatGroup(g, flat);
            return flat.Result().Length;
        }

        private static void FlatItems(List<Item> items, SqlWriter w)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case TokItem t: w.Token(t.T); break;
                    case GroupItem g: FlatGroup(g, w); break;
                    case CaseItem c: FlatItems(c.Children, w); break;
                }
            }
        }

        private static void FlatGroup(GroupItem g, SqlWriter w)
        {
            w.Token(g.Open);
            FlatItems(g.Children, w);
            w.Token(g.Close);
        }
    }
}
