using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Completion
{
    public static class StarExpander
    {
        /// <summary>Builds the column list that replaces "*" (or "c.*"). Null when no table could be resolved,
        /// so the caller can leave the star alone rather than replace it with nothing.</summary>
        public static string Expand(StarContext star, SchemaModel schema)
        {
            var resolved = new List<(TableRef r, DbObject o)>();
            foreach (var r in star.Tables)
            {
                var o = schema.Find(r);
                if (o != null && o.Columns.Count > 0) resolved.Add((r, o));
            }
            if (resolved.Count == 0 || resolved.Count < star.Tables.Count) return null;

            // One table without an alias: bare names. Otherwise qualify, so the list survives later JOINs.
            bool qualify = star.Qualifier != null || resolved.Count > 1 || resolved[0].r.Alias != null;
            var columns = new List<string>();
            foreach (var (r, o) in resolved)
            {
                string prefix = qualify ? SqlIdentifier.QuoteIfNeeded(star.Qualifier ?? r.RefName) + "." : "";
                columns.AddRange(o.Columns.Select(c => prefix + SqlIdentifier.QuoteIfNeeded(c.Name)));
            }
            return string.Join(", ", columns);
        }
    }
}
