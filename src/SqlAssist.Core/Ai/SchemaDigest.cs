using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Ai
{
    /// <summary>One line per table or view the SQL names, from a schema that is already loaded: columns with their types,
    /// keys and foreign keys, so an explanation can follow the joins. Only what the script names is sent, never the whole
    /// database. A word that happens to match a table name (a column called like a table) is included too; that is harmless.</summary>
    public sealed class SchemaDigest
    {
        public const int DefaultMaxTables = 30;

        private SchemaDigest(string text, int tableCount, bool truncated)
        {
            Text = text; TableCount = tableCount; Truncated = truncated;
        }

        /// <summary>Empty when no table was found (or no schema is loaded).</summary>
        public string Text { get; }
        public int TableCount { get; }
        /// <summary>More tables were named than <c>maxTables</c>; the rest were left out.</summary>
        public bool Truncated { get; }

        public static SchemaDigest Build(string sql, SchemaModel schema, bool includeDescriptions, int maxTables = DefaultMaxTables)
        {
            if (schema == null || string.IsNullOrEmpty(sql)) return new SchemaDigest("", 0, false);
            var found = new List<DbObject>();
            bool truncated = false;
            var tokens = SqlTokenizer.Tokenize(sql);
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (!t.IsName || t.Kind == TokenKind.Variable) continue;
                string owner = i >= 2 && tokens[i - 1].IsSymbol('.') && tokens[i - 2].IsName ? tokens[i - 2].Value : null;
                var obj = schema.Find(owner, t.Value);
                if (obj == null || found.Contains(obj)) continue;
                if (found.Count >= maxTables) { truncated = true; break; }
                found.Add(obj);
            }
            var lines = found.Select(o => Line(schema, o, includeDescriptions));
            return new SchemaDigest(string.Join("\n", lines), found.Count, truncated);
        }

        private static string Line(SchemaModel schema, DbObject o, bool includeDescriptions)
        {
            var references = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var fk in schema.ForeignKeysFrom(o))
                foreach (var (from, to) in fk.Columns)
                {
                    if (!references.TryGetValue(from, out var list)) references[from] = list = new List<string>();
                    list.Add(fk.To.Schema + "." + fk.To.Name + "." + to);
                }

            var sb = new StringBuilder();
            sb.Append(o.Schema).Append('.').Append(o.Name).Append(" (").Append((o.Kind ?? "table").ToLowerInvariant()).Append("):");
            if (includeDescriptions && !string.IsNullOrWhiteSpace(o.Description)) sb.Append(" -- ").Append(OneLine(o.Description)).Append(';');
            bool first = true;
            foreach (var c in o.Columns)
            {
                sb.Append(first ? " " : ", ");
                first = false;
                sb.Append(c.Name);
                if (!string.IsNullOrEmpty(c.TypeName)) sb.Append(' ').Append(c.TypeName);
                sb.Append(c.IsNullable ? " NULL" : " NOT NULL");
                if (c.IsPrimaryKey) sb.Append(" PK");
                if (c.IsIdentity) sb.Append(" IDENTITY");
                if (c.IsComputed) sb.Append(" COMPUTED");
                if (references.TryGetValue(c.Name, out var targets))
                    foreach (var target in targets) sb.Append(" -> ").Append(target);
                if (includeDescriptions && !string.IsNullOrWhiteSpace(c.Description)) sb.Append(" -- ").Append(OneLine(c.Description));
            }
            return sb.ToString();
        }

        private static string OneLine(string text) =>
            string.Join(" ", text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
