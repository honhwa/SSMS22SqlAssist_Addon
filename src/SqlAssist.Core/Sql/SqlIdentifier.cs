using System.Collections.Generic;
using System.Text;

namespace SqlAssist.Core.Sql
{
    public static class SqlIdentifier
    {
        public static bool IsWordChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '@' || c == '#';

        /// <summary>Brackets a name that would not survive bare: spaces, symbols, a leading digit or a reserved word.
        /// Leaves @variables and #temp prefixes alone, since bracketing changes what they mean.</summary>
        public static string QuoteIfNeeded(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name[0] == '@') return name;
            string body = name.TrimStart('#');
            if (IsSimple(body) && !SqlKeywords.IsReserved(body)) return name;
            return "[" + name.Replace("]", "]]") + "]";
        }

        private static bool IsSimple(string name)
        {
            if (name.Length == 0 || char.IsDigit(name[0])) return false;
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }

        /// <summary>Splits PascalCase / snake_case / ALLCAPS names into words: "OrderDetails" → Order, Details;
        /// "CustomerID" → Customer, ID; "tbl_order_line2" → tbl, order, line, 2.</summary>
        public static List<string> SplitWords(string name)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(name)) return words;
            var current = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (!char.IsLetterOrDigit(c))
                {
                    Flush(words, current);
                    continue;
                }
                if (current.Length > 0)
                {
                    char prev = name[i - 1];
                    bool boundary =
                        (char.IsLower(prev) && char.IsUpper(c)) ||
                        (char.IsLetter(prev) && char.IsDigit(c)) ||
                        (char.IsDigit(prev) && char.IsLetter(c)) ||
                        (char.IsUpper(prev) && char.IsUpper(c) && i + 1 < name.Length && char.IsLower(name[i + 1]));
                    if (boundary) Flush(words, current);
                }
                current.Append(c);
            }
            Flush(words, current);
            return words;
        }

        private static void Flush(List<string> words, StringBuilder current)
        {
            if (current.Length == 0) return;
            words.Add(current.ToString());
            current.Clear();
        }

        public static string Initials(string name)
        {
            var sb = new StringBuilder();
            foreach (var w in SplitWords(name)) sb.Append(w[0]);
            return sb.ToString();
        }
    }
}
