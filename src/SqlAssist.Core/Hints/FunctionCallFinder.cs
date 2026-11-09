using System;
using System.Collections.Generic;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Hints
{
    /// <summary>The function call whose argument list holds the caret.</summary>
    public sealed class CallSite
    {
        public CallSite(IReadOnlyList<string> nameParts, string leader, int nameStart, int openParen, int argumentIndex)
        {
            NameParts = nameParts; Leader = leader; NameStart = nameStart; OpenParen = openParen; ArgumentIndex = argumentIndex;
        }

        /// <summary>The name as written, without brackets: ["dbo", "fnTax"] or ["SUBSTRING"].</summary>
        public IReadOnlyList<string> NameParts { get; }
        /// <summary>The word right before the name (upper case: FROM, INTO, ...), or null. It tells a function call from a table
        /// or procedure name that is also followed by "(".</summary>
        public string Leader { get; }
        /// <summary>Offsets in the text the finder was given.</summary>
        public int NameStart { get; }
        public int OpenParen { get; }
        /// <summary>How many commas of this call lie before the caret: 0 for the first argument.</summary>
        public int ArgumentIndex { get; }

        public string Name => NameParts[NameParts.Count - 1];
        /// <summary>The schema of a two-part name; null for a bare name (and for names with more parts, which are never resolved).</summary>
        public string Schema => NameParts.Count == 2 ? NameParts[0] : null;
    }

    /// <summary>Finds the innermost function call the caret is inside, from the tokens before the caret only — no grammar, no schema.
    /// What counts as an argument boundary is a comma of that call's own parentheses: commas in strings, comments, nested
    /// parentheses and inner calls are tokens of something else and never count. Parentheses that are not a call (IN (...),
    /// VALUES (...), OVER (...), a grouping) are looked through to the call around them; a subquery's parentheses are a wall,
    /// because the caret is then in another query.</summary>
    public static class FunctionCallFinder
    {
        /// <summary>How far before the caret to read: a call longer than this is not recognised, and no hint is shown for it.</summary>
        public const int MaxBack = 8000;

        // A statement cannot start inside the arguments of a function. Meeting one there means the text before it was left unclosed,
        // so the open calls are forgotten rather than holding a hint over the next statement.
        private static readonly HashSet<string> StatementWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "CREATE", "ALTER", "DROP", "TRUNCATE", "EXEC", "EXECUTE",
            "USE", "PRINT", "RETURN", "WHILE", "FROM", "WHERE", "HAVING", "UNION", "EXCEPT", "INTERSECT", "JOIN", "INTO", "VALUES", "SET",
        };

        /// <summary>The call at <paramref name="caret"/> in <paramref name="text"/>, or null: not inside a call, inside a comment, in a
        /// call that cannot be a function call, or inside a subquery of one. Only the text before the caret is read.</summary>
        public static CallSite Find(string text, int caret)
        {
            if (string.IsNullOrEmpty(text) || caret <= 0 || caret > text.Length) return null;
            int from = Math.Max(0, caret - MaxBack);
            if (from > 0)
            {
                // Start on a line, so the window never begins in the middle of a string or a comment's first line.
                int newLine = text.IndexOf('\n', from, caret - from);
                if (newLine >= 0) from = newLine + 1;
            }
            string head = text.Substring(from, caret - from);
            var tokens = SqlTokenizer.Tokenize(head);

            // Meaningful tokens only; a comment under the caret means no hint.
            var code = new List<SqlToken>(tokens.Count);
            foreach (var t in tokens)
            {
                if (t.Kind != TokenKind.Comment) { code.Add(t); continue; }
                if (caret - from < t.End || (caret - from == t.End && !t.Terminated)) return null;
            }

            var stack = new List<Frame>();
            for (int i = 0; i < code.Count; i++)
            {
                var t = code[i];
                var top = stack.Count > 0 ? stack[stack.Count - 1] : null;
                if (top != null && !top.SawFirstToken)
                {
                    top.SawFirstToken = true;
                    top.IsSubquery = t.IsWord("SELECT") || t.IsWord("WITH");
                }

                if (t.Kind == TokenKind.Symbol)
                {
                    char c = t.Text[0];
                    if (c == '(') stack.Add(OpenFrame(code, i));
                    else if (c == ')') { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
                    else if (c == ',') { if (top != null) top.ArgumentIndex++; }
                    else if (c == ';') stack.Clear();
                }
                else if (t.Kind == TokenKind.Word)
                {
                    if (t.IsWord("GO") && IsOwnLine(head, t)) stack.Clear();
                    else if (top != null && top.NameParts != null && StatementWords.Contains(t.Text) &&
                             !(t.IsWord("FROM") && top.NameParts.Count == 1 && top.NameParts[0].Equals("TRIM", StringComparison.OrdinalIgnoreCase)))
                        stack.Clear();
                }
            }

            for (int i = stack.Count - 1; i >= 0; i--)
            {
                var frame = stack[i];
                if (frame.IsSubquery) return null;
                if (frame.NameParts == null) continue;   // grouping, IN (...), VALUES (...), OVER (...): the call around it still holds the caret
                return new CallSite(frame.NameParts, frame.Leader, from + frame.NameStart, from + frame.OpenParen, frame.ArgumentIndex);
            }
            return null;
        }

        private sealed class Frame
        {
            public int OpenParen, NameStart;
            public List<string> NameParts;   // null: not a call
            public string Leader;
            public int ArgumentIndex;
            public bool SawFirstToken, IsSubquery;
        }

        private static Frame OpenFrame(List<SqlToken> code, int open)
        {
            var frame = new Frame { OpenParen = code[open].Start, NameStart = code[open].Start };
            int i = open - 1;
            if (i < 0 || !IsNamePart(code[i])) return frame;

            // name, or [schema].name, read backwards
            var parts = new List<string>();
            while (i >= 0 && IsNamePart(code[i]))
            {
                parts.Insert(0, code[i].Value);
                frame.NameStart = code[i].Start;
                if (i >= 2 && code[i - 1].IsSymbol('.') && IsNamePart(code[i - 2])) i -= 2;
                else { i--; break; }
            }
            // i is now the token before the name (or -1)
            if (parts.Count == 1 && code[i + 1].Kind == TokenKind.Word && SqlKeywords.IsReserved(parts[0]) && BuiltInSignatures.Find(parts[0]) == null &&
                !BuiltInSignatures.IsSpecialSyntax(parts[0]))
                return frame;   // IN (, VALUES (, AS (, OVER (, ...: a grouping, not a call
            frame.NameParts = parts;
            frame.Leader = i >= 0 && code[i].Kind == TokenKind.Word ? code[i].Text.ToUpperInvariant() : null;
            return frame;
        }

        private static bool IsNamePart(SqlToken t) => t.Kind == TokenKind.Word || t.Kind == TokenKind.QuotedIdentifier;

        // GO alone on its line (an optional count after it): the batch separator, which ends every open call.
        private static bool IsOwnLine(string head, SqlToken go)
        {
            for (int i = go.Start - 1; i >= 0 && head[i] != '\n'; i--)
                if (!char.IsWhiteSpace(head[i])) return false;
            for (int i = go.End; i < head.Length && head[i] != '\n'; i++)
                if (!char.IsWhiteSpace(head[i]) && !char.IsDigit(head[i])) return false;
            return true;
        }
    }
}
