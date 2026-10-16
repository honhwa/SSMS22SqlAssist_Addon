using System.Collections.Generic;

namespace SqlAssist.Core.Sql
{
    public enum TokenKind { Word, QuotedIdentifier, Variable, String, Number, Symbol, Comment }

    public readonly struct SqlToken
    {
        public SqlToken(TokenKind kind, int start, int length, string text, bool terminated)
        {
            Kind = kind; Start = start; Length = length; Text = text; Terminated = terminated;
        }

        public TokenKind Kind { get; }
        public int Start { get; }
        public int Length { get; }
        public int End => Start + Length;
        public string Text { get; }
        /// <summary>False for a string, comment or bracketed name still open at the end of the text.</summary>
        public bool Terminated { get; }

        public bool IsName => Kind == TokenKind.Word || Kind == TokenKind.QuotedIdentifier || Kind == TokenKind.Variable;
        public bool IsSymbol(char c) => Kind == TokenKind.Symbol && Text[0] == c;
        public bool IsWord(string w) => Kind == TokenKind.Word && string.Equals(Text, w, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>The name without brackets or quotes.</summary>
        public string Value
        {
            get
            {
                if (Kind != TokenKind.QuotedIdentifier) return Text;
                char open = Text[0];
                string close = open == '[' ? "]" : "\"";
                string inner = Text.Substring(1, Terminated ? Text.Length - 2 : Text.Length - 1);
                return inner.Replace(close + close, close);
            }
        }

        public override string ToString() => Kind + ":" + Text;
    }

    public static class SqlTokenizer
    {
        public static List<SqlToken> Tokenize(string text)
        {
            var tokens = new List<SqlToken>();
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                int start = i;

                if (c == '-' && Peek(text, i + 1) == '-')
                {
                    while (i < n && text[i] != '\n' && text[i] != '\r') i++;
                    // A line comment never "closes": the caret at its end is still inside it.
                    tokens.Add(Make(text, TokenKind.Comment, start, i, false));
                    continue;
                }
                if (c == '/' && Peek(text, i + 1) == '*')
                {
                    int depth = 0;
                    bool closed = false;
                    while (i < n)
                    {
                        if (text[i] == '/' && Peek(text, i + 1) == '*') { depth++; i += 2; continue; }
                        if (text[i] == '*' && Peek(text, i + 1) == '/')
                        {
                            depth--; i += 2;
                            if (depth == 0) { closed = true; break; }
                            continue;
                        }
                        i++;
                    }
                    tokens.Add(Make(text, TokenKind.Comment, start, i, closed));
                    continue;
                }
                if (c == '\'' || ((c == 'N' || c == 'n') && Peek(text, i + 1) == '\''))
                {
                    if (c != '\'') i++;
                    bool closed = ReadQuoted(text, ref i, '\'');
                    tokens.Add(Make(text, TokenKind.String, start, i, closed));
                    continue;
                }
                if (c == '[' || c == '"')
                {
                    // Stops at a line break when unterminated: a half-typed "[Cust" must not swallow the
                    // rest of the script (and with it the FROM clause the completion needs).
                    bool closed = ReadQuoted(text, ref i, c == '[' ? ']' : '"', stopAtNewLine: true);
                    tokens.Add(Make(text, TokenKind.QuotedIdentifier, start, i, closed));
                    continue;
                }
                if (c == '@')
                {
                    i++;
                    while (i < n && SqlIdentifier.IsWordChar(text[i])) i++;
                    tokens.Add(Make(text, TokenKind.Variable, start, i, true));
                    continue;
                }
                if (char.IsDigit(c))
                {
                    while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '.')) i++;
                    tokens.Add(Make(text, TokenKind.Number, start, i, true));
                    continue;
                }
                if (char.IsLetter(c) || c == '_' || c == '#')
                {
                    while (i < n && SqlIdentifier.IsWordChar(text[i])) i++;
                    tokens.Add(Make(text, TokenKind.Word, start, i, true));
                    continue;
                }
                i++;
                tokens.Add(Make(text, TokenKind.Symbol, start, i, true));
            }
            return tokens;
        }

        /// <summary>True when the caret sits inside a string literal or a comment, where completion must stay quiet.</summary>
        public static bool IsInStringOrComment(IReadOnlyList<SqlToken> tokens, int caret)
        {
            foreach (var t in tokens)
            {
                if (t.Start >= caret) break;
                if (t.Kind != TokenKind.String && t.Kind != TokenKind.Comment) continue;
                if (caret < t.End || (caret == t.End && !t.Terminated)) return true;
            }
            return false;
        }

        // i points at the opening quote; on return it is just past the closing one (or at end of text).
        private static bool ReadQuoted(string text, ref int i, char close, bool stopAtNewLine = false)
        {
            i++;
            while (i < text.Length)
            {
                if (stopAtNewLine && (text[i] == '\n' || text[i] == '\r')) return false;
                if (text[i] == close)
                {
                    if (Peek(text, i + 1) == close) { i += 2; continue; }
                    i++;
                    return true;
                }
                i++;
            }
            return false;
        }

        private static char Peek(string text, int i) => i < text.Length ? text[i] : '\0';

        private static SqlToken Make(string text, TokenKind kind, int start, int end, bool terminated) =>
            new SqlToken(kind, start, end - start, text.Substring(start, end - start), terminated);
    }
}
