using System;
using System.Collections.Generic;
using System.Text;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Formatting
{
    /// <summary>Writes tokens and line breaks. It owns the one thing every layout rule shares: whether two neighbouring
    /// tokens are separated by a space ("a, b", "f(x)", "x = -1", "t.*"), so that no rule has to think about it.</summary>
    internal sealed class SqlWriter
    {
        /// <summary>Reserved words that are called like functions: no space before the parenthesis.</summary>
        private static readonly HashSet<string> FunctionLikeReserved = new HashSet<string>
        {
            "COALESCE", "CONVERT", "NULLIF", "LEFT", "RIGHT", "TRY_CONVERT", "OPENQUERY", "OPENROWSET", "OPENDATASOURCE", "OPENXML",
            "IDENTITY", "CONTAINS", "CONTAINSTABLE", "FREETEXT", "FREETEXTTABLE", "SEMANTICKEYPHRASETABLE",
            "SEMANTICSIMILARITYDETAILSTABLE", "SEMANTICSIMILARITYTABLE",
        };

        /// <summary>Words that are not reserved, but are followed by a parenthesis with a space.</summary>
        private static readonly HashSet<string> SpaceBeforeParenWords = new HashSet<string>
            { "APPLY", "PIVOT", "UNPIVOT", "USING", "OUTPUT", "OFFSET", "INCLUDE" };

        private readonly StringBuilder _sb = new StringBuilder();
        private readonly string _nl;
        private readonly int _unit;
        private readonly int _baseIndent;
        private readonly bool _flat;

        private bool _atLineStart = true;
        private bool _firstLine = true;
        private int _lineIndent;
        private int _nestedBase;
        private FToken _prev;
        private FToken _prevSignificant;
        private bool _prevUnary;
        private bool _forceSpace;
        private bool _breakAfter;

        /// <param name="baseIndent">Spaces added to every line but the first (a selection that starts mid-document).</param>
        /// <param name="flat">Measuring only: line breaks become spaces and nothing is indented.</param>
        public SqlWriter(string newline, int indentUnit, int baseIndent = 0, bool flat = false)
        {
            _nl = newline; _unit = indentUnit; _baseIndent = baseIndent; _flat = flat;
        }

        public int Unit => _unit;
        /// <summary>The indentation (in spaces, before the base indent) that blocks opened on the current line are measured from.</summary>
        public int NestedBase => _nestedBase;
        public int Length => _sb.Length;
        public bool AtLineStart => _atLineStart;

        public void ForceSpace() => _forceSpace = true;

        /// <summary>Ends the current line (when it has anything on it) and starts the next at <paramref name="indent"/>.</summary>
        public void NewLine(int indent, int? nestedBase = null)
        {
            if (_flat)
            {
                _forceSpace = _sb.Length > 0;
                return;
            }
            if (_sb.Length > 0 && !_atLineStart)
            {
                TrimTrailingSpaces();
                _sb.Append(_nl);
                _firstLine = false;
                _atLineStart = true;
            }
            _lineIndent = indent;
            _nestedBase = nestedBase ?? indent;
            _prev = _prevSignificant = null;
            _prevUnary = _forceSpace = _breakAfter = false;
        }

        /// <summary>One empty line before whatever comes next (nothing at the very start, never two in a row).</summary>
        public void BlankLine()
        {
            if (_flat || _sb.Length == 0) return;
            if (!_atLineStart) NewLine(0);
            string two = _nl + _nl;
            bool already = _sb.Length >= two.Length && _sb.ToString(_sb.Length - two.Length, two.Length) == two;
            if (!already) _sb.Append(_nl);
        }

        public void Token(FToken t)
        {
            if (_breakAfter)
            {
                int indent = _nestedBase + _unit;
                NewLine(indent, indent);
            }
            bool atStart = _atLineStart;
            if (atStart)
            {
                if (!_flat) _sb.Append(' ', _lineIndent + (_firstLine ? 0 : _baseIndent));
                _atLineStart = false;
            }
            else if (NeedSpace(_prev, t)) _sb.Append(' ');

            string text = t.Text;
            if (t.IsBlockComment && (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)) text = MultiLine(t, atStart);
            _sb.Append(text);

            _prevUnary = t.Kind == TokenKind.Symbol && (t.Text == "-" || t.Text == "+" || t.Text == "~") && IsUnaryContext();
            _prev = t;
            if (!t.IsComment) _prevSignificant = t;
            _forceSpace = false;
            if (t.IsLineComment && !_flat) _breakAfter = true;
        }

        /// <summary>Everything written, without trailing whitespace: how the text ends is the caller's business.</summary>
        public string Result()
        {
            int end = _sb.Length;
            while (end > 0 && (_sb[end - 1] == ' ' || _sb[end - 1] == '\n' || _sb[end - 1] == '\r')) end--;
            return _sb.ToString(0, end);
        }

        // ---- spacing ----

        private bool NeedSpace(FToken prev, FToken next)
        {
            if (_forceSpace) return true;
            if (prev == null) return false;
            if (prev.IsComment || next.IsComment) return true;

            if (prev.Kind == TokenKind.Symbol)
            {
                if (prev.Text == "(" || prev.Text == "." || prev.Text == "::") return false;
                // After a prefix operator no space, except that "- -1" must never turn into the comment "--1".
                if (_prevUnary && !(next.Kind == TokenKind.Symbol && (next.Text == "-" || next.Text == "+" || next.Text == "~"))) return false;
            }
            if (next.Kind == TokenKind.Symbol)
            {
                switch (next.Text)
                {
                    case ",": case ";": case ")": case ".": case "::": case ":": return false;
                    case "(": return SpaceBeforeParen(prev, next);
                }
            }
            return true;
        }

        private static bool SpaceBeforeParen(FToken prev, FToken next)
        {
            if (prev.Kind == TokenKind.Word)
            {
                string u = prev.Upper;
                if (FunctionLikeReserved.Contains(u)) return false;
                if (SqlKeywords.IsReserved(u) || SpaceBeforeParenWords.Contains(u)) return true;
                if (KeywordCaser.IsFunctionOrType(u)) return false;
                return next.SpaceBefore;   // a name followed by "(": a call or a definition; keep what was typed
            }
            if (prev.Kind == TokenKind.QuotedIdentifier) return next.SpaceBefore;
            return true;
        }

        /// <summary>A + or - with no value before it: after an operator, a comma, "(" or a keyword.</summary>
        private bool IsUnaryContext()
        {
            var p = _prevSignificant;
            if (p == null) return true;
            switch (p.Kind)
            {
                case TokenKind.Symbol: return p.Text != ")";
                case TokenKind.Word:
                    string u = p.Upper;
                    return u != "END" && u != "NULL" && SqlKeywords.IsReserved(u);
                default: return false;
            }
        }

        // ---- text ----

        private void TrimTrailingSpaces()
        {
            int end = _sb.Length;
            while (end > 0 && _sb[end - 1] == ' ') end--;
            _sb.Length = end;
        }

        /// <summary>A block comment spanning lines: line breaks follow the document, and when the comment starts its line it
        /// moves as a whole, so the lines under the first keep their place relative to it.</summary>
        private string MultiLine(FToken t, bool atLineStart)
        {
            var lines = t.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool move = atLineStart && t.OriginalIndent >= 0 && !_flat;
            int indent = _lineIndent + (_firstLine ? 0 : _baseIndent);
            var sb = new StringBuilder(lines[0]);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (move)
                {
                    int strip = 0;
                    while (strip < line.Length && strip < t.OriginalIndent && (line[strip] == ' ' || line[strip] == '\t')) strip++;
                    line = new string(' ', indent) + line.Substring(strip);
                }
                sb.Append(_nl).Append(line);
            }
            return sb.ToString();
        }
    }
}
