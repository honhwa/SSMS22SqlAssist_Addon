using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Completion
{
    /// <summary>A table picked in the completion tree, and which of its columns (all, when the table itself was picked).</summary>
    public sealed class PickedTable
    {
        public PickedTable(DbObject table, IEnumerable<string> columns = null)
        {
            Table = table;
            Columns = columns?.ToList() ?? table.Columns.Select(c => c.Name).ToList();
        }
        public DbObject Table { get; }
        public IReadOnlyList<string> Columns { get; }
    }

    /// <summary>Writes the query for columns picked in the completion tree:
    /// <code>
    /// SELECT wr.ReqId
    ///       ,wia.Status
    /// FROM Work_Request wr
    /// JOIN Work_InitialApproval wia ON wia.ReqId = wr.ReqId
    /// </code>
    /// The text replaces the word being typed after SELECT. Tables after the first are joined on a foreign key, else on
    /// a shared id-like column, else CROSS JOIN (left for the user to turn into the join they mean).</summary>
    public static class SelectBuilder
    {
        /// <param name="linePrefix">Text of the line before the insertion point ("SELECT "), for comma alignment.</param>
        /// <param name="takenAliases">Aliases already used in the statement.</param>
        public static string Build(SchemaModel schema, IReadOnlyList<PickedTable> picks, string linePrefix, string newLine,
            IEnumerable<string> takenAliases = null)
        {
            picks = picks.Where(p => p.Columns.Count > 0).ToList();
            if (picks.Count == 0) return "";

            var taken = new HashSet<string>(takenAliases ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var aliases = new List<string>();
            foreach (var p in picks)
            {
                string alias = AliasGenerator.Generate(p.Table.Name, taken);
                taken.Add(alias);
                aliases.Add(alias);
            }

            // Column list: the same leading-comma layout as SELECT * expansion.
            var columns = new List<string>();
            for (int i = 0; i < picks.Count; i++)
                columns.AddRange(picks[i].Columns.Select(c => SqlIdentifier.QuoteIfNeeded(aliases[i]) + "." + SqlIdentifier.QuoteIfNeeded(c)));
            string commaIndent = Indent(linePrefix, dropLast: true);
            string lineIndent = LeadingWhitespace(linePrefix);

            var sb = new StringBuilder(string.Join(newLine + commaIndent + ",", columns));
            sb.Append(newLine).Append(lineIndent).Append("FROM ").Append(SchemaModel.DisplayName(picks[0].Table)).Append(' ').Append(aliases[0]);
            for (int i = 1; i < picks.Count; i++)
            {
                string table = SchemaModel.DisplayName(picks[i].Table) + " " + aliases[i];
                string predicate = JoinPredicate(schema, picks, aliases, i);
                sb.Append(newLine).Append(lineIndent)
                  .Append(predicate == null ? "CROSS JOIN " + table : "JOIN " + table + " ON " + predicate);
            }
            return sb.ToString();
        }

        // Joins table i to an earlier pick: a foreign key either way (whole key), else one shared id-like column.
        private static string JoinPredicate(SchemaModel schema, IReadOnlyList<PickedTable> picks, IReadOnlyList<string> aliases, int i)
        {
            var table = picks[i].Table;
            string me = SqlIdentifier.QuoteIfNeeded(aliases[i]);
            for (int j = i - 1; j >= 0; j--)
            {
                var other = picks[j].Table;
                string them = SqlIdentifier.QuoteIfNeeded(aliases[j]);
                var fk = schema.ForeignKeysFrom(table).FirstOrDefault(k => k.To == other);
                if (fk != null) return Pairs(me, them, fk.Columns.Select(c => (c.from, c.to)));
                fk = schema.ForeignKeysFrom(other).FirstOrDefault(k => k.To == table);
                if (fk != null) return Pairs(me, them, fk.Columns.Select(c => (c.to, c.from)));
            }
            for (int j = i - 1; j >= 0; j--)
            {
                var shared = table.Columns.FirstOrDefault(c => c.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase) &&
                    picks[j].Table.Columns.Any(o => string.Equals(o.Name, c.Name, StringComparison.OrdinalIgnoreCase)));
                if (shared != null) return Pairs(me, SqlIdentifier.QuoteIfNeeded(aliases[j]), new[] { (shared.Name, shared.Name) });
            }
            return null;
        }

        private static string Pairs(string me, string them, IEnumerable<(string mine, string theirs)> pairs) =>
            string.Join(" AND ", pairs.Select(p => me + "." + SqlIdentifier.QuoteIfNeeded(p.mine) + " = " + them + "." + SqlIdentifier.QuoteIfNeeded(p.theirs)));

        private static string Indent(string prefix, bool dropLast)
        {
            var sb = new StringBuilder(prefix.Length);
            foreach (char c in prefix) sb.Append(c == '\t' ? '\t' : ' ');
            return dropLast && sb.Length > 0 ? sb.ToString(0, sb.Length - 1) : sb.ToString();
        }

        private static string LeadingWhitespace(string s) => s.Substring(0, s.Length - s.TrimStart().Length);
    }
}
