using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlAssist.Core.Search
{
    /// <summary>A compiled Search Query. Matching is "contains"; whole word uses the same word characters as
    /// <see cref="Sql.SqlIdentifier.IsWordChar"/>, so "Order" does not hit "OrderId" or "@Order".</summary>
    public sealed class SearchMatcher
    {
        private const string WordChar = @"[\p{L}\p{Nd}_$@#]";
        private static readonly IReadOnlyList<TextSpan> NoHits = new TextSpan[0];
        // Lazy wildcards can backtrack badly on a long single-line definition; give up on that one item instead.
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

        private readonly Regex _regex;
        private readonly SearchObjectType _types;

        private SearchMatcher(Regex regex, SearchObjectType types) { _regex = regex; _types = types; }

        /// <summary>A matcher that matches nothing: the text is shorter than <see cref="SearchQuery.MinLength"/>,
        /// is only wildcards, or no object type is selected.</summary>
        public bool IsEmpty => _regex == null;

        public static SearchMatcher Create(SearchQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            string text = (query.Text ?? "").Trim();
            if (text.Length < SearchQuery.MinLength || query.Types == SearchObjectType.None)
                return new SearchMatcher(null, query.Types);

            var pattern = new StringBuilder();
            bool literal = false;
            foreach (char c in text)
            {
                if (query.UseWildcards && c == '*') pattern.Append(@"[^\r\n]*?");
                else if (query.UseWildcards && c == '?') pattern.Append(@"[^\r\n]");
                else { pattern.Append(Regex.Escape(c.ToString())); literal = true; }
            }
            if (!literal) return new SearchMatcher(null, query.Types);

            string body = pattern.ToString();
            if (query.WholeWord) body = "(?<!" + WordChar + ")" + body + "(?!" + WordChar + ")";

            var options = RegexOptions.CultureInvariant;
            if (!query.MatchCase) options |= RegexOptions.IgnoreCase;
            return new SearchMatcher(new Regex(body, options, Timeout), query.Types);
        }

        public IReadOnlyList<TextSpan> FindAll(string text)
        {
            if (_regex == null || string.IsNullOrEmpty(text)) return NoHits;
            try
            {
                List<TextSpan> hits = null;
                for (var m = _regex.Match(text); m.Success; m = m.NextMatch())
                {
                    if (m.Length == 0) continue;
                    (hits ?? (hits = new List<TextSpan>())).Add(new TextSpan(m.Index, m.Length));
                }
                return (IReadOnlyList<TextSpan>)hits ?? NoHits;
            }
            catch (RegexMatchTimeoutException)
            {
                return NoHits;
            }
        }

        /// <summary>Null when the item is filtered out by type or matches neither its name nor its definition.</summary>
        public SearchMatch Match(SearchItem item)
        {
            if (_regex == null || item == null || (item.Type & _types) == 0) return null;
            var nameHits = FindAll(item.Name);
            var definitionHits = FindAll(item.Definition);
            return nameHits.Count == 0 && definitionHits.Count == 0 ? null : new SearchMatch(item, nameHits, definitionHits);
        }

        /// <summary>Matches in index order, stopping after <paramref name="limit"/> hits when it is positive.</summary>
        public List<SearchMatch> Search(IEnumerable<SearchItem> items, int limit = 0)
        {
            var results = new List<SearchMatch>();
            if (_regex == null) return results;
            foreach (var item in items)
            {
                var match = Match(item);
                if (match == null) continue;
                results.Add(match);
                if (limit > 0 && results.Count >= limit) break;
            }
            return results;
        }
    }
}
