using System;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Completion
{
    /// <summary>Scores how well typed text matches a name. 0 = no match; higher = better.
    /// Exact &gt; prefix &gt; initials ("od" → OrderDetails) &gt; word prefix ("id" → CustomerID) &gt; substring &gt; subsequence ("custid" → CustomerID).</summary>
    public static class FuzzyMatcher
    {
        public const int Exact = 1000;
        public const int Prefix = 900;
        public const int InitialsMatch = 700;
        public const int WordPrefix = 600;
        public const int Substring = 500;
        public const int Subsequence = 300;

        /// <summary>Scores at or above this are confident enough to commit with Enter.</summary>
        public const int Strong = WordPrefix;

        public static int Score(string pattern, string candidate)
        {
            if (string.IsNullOrEmpty(candidate)) return 0;
            if (string.IsNullOrEmpty(pattern)) return 1;
            if (pattern.Length > candidate.Length) return 0;

            if (string.Equals(pattern, candidate, StringComparison.OrdinalIgnoreCase)) return Exact;
            // Shorter names first among prefix matches: "Order" before "OrderDetails" when typing "ord".
            if (candidate.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return Prefix - Math.Min(candidate.Length - pattern.Length, 99);

            var words = SqlIdentifier.SplitWords(candidate);
            string initials = SqlIdentifier.Initials(candidate);
            if (words.Count > 1 && initials.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return InitialsMatch - (initials.Length - pattern.Length);

            for (int i = 1; i < words.Count; i++)
                if (words[i].StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    return WordPrefix - i;

            int at = candidate.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) return Substring - Math.Min(at, 99);

            // Subsequence: every typed letter appears in order. Fewer gaps score higher.
            int gaps = 0, ci = 0;
            for (int pi = 0; pi < pattern.Length; pi++)
            {
                char pc = char.ToLowerInvariant(pattern[pi]);
                bool found = false;
                while (ci < candidate.Length)
                {
                    bool hit = char.ToLowerInvariant(candidate[ci]) == pc;
                    ci++;
                    if (hit) { found = true; break; }
                    if (pi > 0) gaps++;
                }
                if (!found) return 0;
            }
            // Must start on the first letter, or "ab" would match half the dictionary.
            if (char.ToLowerInvariant(candidate[0]) != char.ToLowerInvariant(pattern[0])) return 0;
            return Math.Max(Subsequence - gaps * 5, 100);
        }
    }
}
