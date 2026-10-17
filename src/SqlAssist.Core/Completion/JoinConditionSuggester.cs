using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Completion
{
    public sealed class JoinCondition
    {
        /// <summary>The predicate to insert after ON: "wr.ReqId = wia.ReqId".</summary>
        public string Text { get; set; }
        /// <summary>Name of the foreign key it comes from; null for a same-name column match.</summary>
        public string ForeignKeyName { get; set; }
    }

    /// <summary>Predicates for "JOIN t x ON |": foreign keys between x and the tables before it first (the whole key,
    /// AND-ed), then every pair of columns with the same name, id-like columns first. The earlier table is on the left,
    /// the joined one on the right: "wr.ReqId = wia.ReqId".</summary>
    public static class JoinConditionSuggester
    {
        public static IReadOnlyList<JoinCondition> Suggest(SchemaModel schema, TableRef target, IReadOnlyList<TableRef> partners, string conditionSoFar = null)
        {
            var result = new List<JoinCondition>();
            var joined = schema.Find(target);
            if (joined == null) return result;

            string joinedRef = SqlIdentifier.QuoteIfNeeded(target.RefName);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string already = Normalize(conditionSoFar);

            void Add(string text, string fk)
            {
                if (seen.Add(text) && (already.Length == 0 || already.IndexOf(Normalize(text), StringComparison.Ordinal) < 0))
                    result.Add(new JoinCondition { Text = text, ForeignKeyName = fk });
            }

            // Most recently joined partner first: "FROM a JOIN b ON .. JOIN c ON |" usually joins c to b.
            var resolved = partners.Reverse().Select(p => (p, o: schema.Find(p))).Where(x => x.o != null).ToList();

            foreach (var (p, o) in resolved)
            {
                string partnerRef = SqlIdentifier.QuoteIfNeeded(p.RefName);
                foreach (var fk in schema.ForeignKeysFrom(joined).Where(k => k.To == o))
                    Add(Predicate(partnerRef, joinedRef, fk.Columns.Select(c => (left: c.to, right: c.from))), fk.Name);
                foreach (var fk in schema.ForeignKeysTo(joined).Where(k => k.From == o))
                    Add(Predicate(partnerRef, joinedRef, fk.Columns.Select(c => (left: c.from, right: c.to))), fk.Name);
            }

            foreach (var (p, o) in resolved)
            {
                string partnerRef = SqlIdentifier.QuoteIfNeeded(p.RefName);
                var joinedColumns = new HashSet<string>(joined.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
                var shared = o.Columns.Where(c => joinedColumns.Contains(c.Name))
                    .OrderBy(c => IsIdLike(c.Name) ? 0 : 1); // stable: keeps table order within each group
                foreach (var c in shared)
                    Add(Predicate(partnerRef, joinedRef, new[] { (left: c.Name, right: c.Name) }), null);
            }
            return result;
        }

        private static string Predicate(string leftRef, string rightRef, IEnumerable<(string left, string right)> pairs) =>
            string.Join(" AND ", pairs.Select(x =>
                leftRef + "." + SqlIdentifier.QuoteIfNeeded(x.left) + " = " + rightRef + "." + SqlIdentifier.QuoteIfNeeded(x.right)));

        private static bool IsIdLike(string name) => name.EndsWith("id", StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string sql) =>
            sql == null ? "" : new string(sql.Where(c => !char.IsWhiteSpace(c) && c != '[' && c != ']').ToArray()).ToLowerInvariant();
    }
}
