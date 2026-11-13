using System;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Completion
{
    /// <summary>Small additions to a keyword's <see cref="FuzzyMatcher"/> score where the caret makes it the likely next word.
    /// Fuzzy ranking alone puts shorter prefix matches first, so after "FROM t x w" WHERE (5 letters) would sit below WHEN and
    /// WITH (4 letters).</summary>
    public static class KeywordPreference
    {
        /// <summary>Each letter of length costs one point among prefix matches, so 2 lifts WHERE above the 4-letter W keywords
        /// but keeps it below anything two or more letters shorter (an exact or 2-letter match such as the "wh" snippet).</summary>
        public const int AfterTableSourceBonus = 2;

        public static int Bonus(CaretContext context, string keyword) =>
            context != null && context.IsAfterTableSource && string.Equals(keyword, "WHERE", StringComparison.OrdinalIgnoreCase)
                ? AfterTableSourceBonus : 0;
    }
}
