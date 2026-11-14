using System.Text;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Ai
{
    /// <summary>Replaces string and number literals before SQL leaves the machine: a script often carries real values (a customer's
    /// name, an ID list pasted from Excel). Everything else, comments included, is copied as typed, and a literal that spanned
    /// several lines leaves the same number of line breaks behind, so line numbers in the answer still point at the right line.</summary>
    public static class LiteralMasker
    {
        public const string StringPlaceholder = "'?'";
        public const string NumberPlaceholder = "0";

        public static string Mask(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return "";
            var sb = new StringBuilder(sql.Length);
            int copied = 0;
            foreach (var token in SqlTokenizer.Tokenize(sql))
            {
                if (token.Kind != TokenKind.String && token.Kind != TokenKind.Number) continue;
                sb.Append(sql, copied, token.Start - copied);
                sb.Append(token.Kind == TokenKind.String ? StringPlaceholder : NumberPlaceholder);
                foreach (char c in token.Text)
                    if (c == '\n') sb.Append('\n');
                copied = token.End;
            }
            sb.Append(sql, copied, sql.Length - copied);
            return sb.ToString();
        }
    }
}
