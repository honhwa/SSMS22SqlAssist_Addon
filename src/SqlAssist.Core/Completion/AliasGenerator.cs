using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Completion
{
    public static class AliasGenerator
    {
        /// <summary>Initials of the name in lower case: Customers → c, OrderDetails → od, tbl_order_line → ol.
        /// Adds a number when the alias is already used in the statement or is a keyword (InventoryStock → is2).</summary>
        public static string Generate(string tableName, IEnumerable<string> taken)
        {
            var words = SqlIdentifier.SplitWords(tableName ?? "");
            if (words.Count > 1 && (words[0].Equals("tbl", StringComparison.OrdinalIgnoreCase) || words[0].Equals("tb", StringComparison.OrdinalIgnoreCase)))
                words.RemoveAt(0);

            string baseAlias = string.Concat(words.Select(w => char.ToLowerInvariant(w[0])));
            if (baseAlias.Length == 0 || !char.IsLetter(baseAlias[0])) baseAlias = "t" + baseAlias;

            var used = new HashSet<string>(taken ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (IsFree(baseAlias, used)) return baseAlias;
            for (int n = 2; ; n++)
            {
                string candidate = baseAlias + n;
                if (IsFree(candidate, used)) return candidate;
            }
        }

        private static bool IsFree(string alias, HashSet<string> used) => !used.Contains(alias) && SqlKeywords.CanBeAlias(alias);
    }
}
