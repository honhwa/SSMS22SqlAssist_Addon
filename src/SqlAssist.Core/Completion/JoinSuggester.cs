using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Completion
{
    public sealed class JoinSuggestion
    {
        public DbObject Target { get; set; }
        public string Alias { get; set; }
        public string ForeignKeyName { get; set; }
        /// <summary>The whole clause after JOIN: "Orders o ON o.CustomerID = c.CustomerID".</summary>
        public string Text { get; set; }
    }

    public static class JoinSuggester
    {
        /// <summary>Tables linked by a foreign key (either direction) to a table already in the query block.</summary>
        public static IReadOnlyList<JoinSuggestion> Suggest(SchemaModel schema, IReadOnlyList<TableRef> inScope, IEnumerable<string> takenAliases)
        {
            var taken = new HashSet<string>(takenAliases ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var r in inScope) if (r.Alias == null && r.Name != null) taken.Add(r.Name);

            // One alias per target table: two keys to the same table are alternatives, not both taken.
            var aliases = new Dictionary<DbObject, string>();
            string AliasFor(DbObject o)
            {
                if (!aliases.TryGetValue(o, out var a)) aliases[o] = a = AliasGenerator.Generate(o.Name, taken);
                return a;
            }

            var result = new List<JoinSuggestion>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in inScope)
            {
                var existing = schema.Find(r);
                if (existing == null) continue;
                string existingRef = SqlIdentifier.QuoteIfNeeded(r.RefName);

                // existing → target: the new table is the referenced one.
                foreach (var fk in schema.ForeignKeysFrom(existing))
                    Add(fk.To, fk, fk.Columns.Select(c => (newCol: c.to, oldCol: c.from)));
                // target → existing: the new table is a child pointing at us.
                foreach (var fk in schema.ForeignKeysTo(existing))
                    Add(fk.From, fk, fk.Columns.Select(c => (newCol: c.from, oldCol: c.to)));

                void Add(DbObject target, ForeignKey fk, IEnumerable<(string newCol, string oldCol)> pairs)
                {
                    string alias = AliasFor(target);
                    // The new table's alias always leads, whichever way the key points.
                    string predicate = string.Join(" AND ", pairs.Select(p =>
                        alias + "." + SqlIdentifier.QuoteIfNeeded(p.newCol) + " = " + existingRef + "." + SqlIdentifier.QuoteIfNeeded(p.oldCol)));
                    string text = SchemaModel.DisplayName(target) + " " + alias + " ON " + predicate;
                    if (seen.Add(text))
                        result.Add(new JoinSuggestion { Target = target, Alias = alias, ForeignKeyName = fk.Name, Text = text });
                }
            }
            return result;
        }
    }
}
