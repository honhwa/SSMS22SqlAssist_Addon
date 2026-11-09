using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlAssist.Core.Search
{
    /// <summary>How we know a Usage Referrer refers to the Usage Target, strongest evidence first: when one referrer is
    /// found in several ways the lowest value wins, and referrers are listed in this order.</summary>
    public enum UsageKind
    {
        /// <summary>sys.sql_expression_dependencies resolved the reference to the target's object id.</summary>
        Dependency,
        /// <summary>A foreign key on the referrer points at the target table.</summary>
        ForeignKey,
        /// <summary>The referrer is a trigger on the target.</summary>
        Trigger,
        /// <summary>The referrer is a synonym for the target.</summary>
        Synonym,
        /// <summary>The reference is unresolved (deferred name resolution) and matched by name only: it may be another
        /// object of the same name.</summary>
        NameOnly,
        /// <summary>Found by searching the referrer's definition for the name; used for columns.</summary>
        TextMatch,
    }

    /// <summary>The object whose Usages are wanted: a table, view, procedure, function, user-defined type, or a column
    /// (then <see cref="Schema"/>, <see cref="Name"/> and <see cref="Type"/> describe its owner).</summary>
    public sealed class UsageTarget
    {
        public UsageTarget(SearchItem item)
        {
            if (!CanFind(item)) throw new ArgumentException("Usages can be found for a table, view, procedure, function, user-defined type or column.", nameof(item));
            Database = item.Database;
            Schema = item.Schema;
            if (item.Type == SearchObjectType.Column)
            {
                Name = item.Parent;
                Column = item.Name;
                Type = item.ParentType == SearchObjectType.None ? SearchObjectType.Table : item.ParentType;
            }
            else
            {
                Name = item.Name;
                Type = item.Type;
            }
        }

        public string Database { get; }
        public string Schema { get; }
        public string Name { get; }
        /// <summary>For a column, its owner's type (Table when the index did not say).</summary>
        public SearchObjectType Type { get; }
        /// <summary>The column's name; null when the target is the object itself.</summary>
        public string Column { get; }
        public bool IsColumn => Column != null;

        public string DisplayName =>
            Search.TableScriptBuilder.Bracket(Schema) + "." + Search.TableScriptBuilder.Bracket(Name) +
            (IsColumn ? "." + Search.TableScriptBuilder.Bracket(Column) : "");

        private const SearchObjectType Findable =
            SearchObjectType.Table | SearchObjectType.View | SearchObjectType.Procedure | SearchObjectType.Function |
            SearchObjectType.UserDefinedType | SearchObjectType.Column;

        public static bool CanFind(SearchItem item) =>
            item != null && item.Schema != null && item.Name != null && (item.Type & Findable) != 0 && item.Type != SearchObjectType.None &&
            (item.Type != SearchObjectType.Column || item.Parent != null);
    }

    /// <summary>One row from the database: an object that refers to the target, before it is matched with the Search Index.</summary>
    public sealed class RawUsage
    {
        public RawUsage(string schema, string name, string typeCode, UsageKind kind)
        {
            Schema = schema; Name = name; TypeCode = typeCode; Kind = kind;
        }

        public string Schema { get; }
        public string Name { get; }
        /// <summary>sys.objects.type, e.g. "P", "V", "FN", "U", "TR", "SN".</summary>
        public string TypeCode { get; }
        public UsageKind Kind { get; }
    }

    /// <summary>One object that refers to the Usage Target, and how we know.</summary>
    public sealed class Usage
    {
        public Usage(SearchItem referrer, UsageKind kind, IReadOnlyList<TextSpan> hits)
        {
            Referrer = referrer; Kind = kind; Hits = hits;
        }

        /// <summary>The Search Item when the index has it (so it has its Definition); otherwise a bare item with no preview.</summary>
        public SearchItem Referrer { get; }
        public UsageKind Kind { get; }
        /// <summary>Where the target's name appears in <see cref="SearchItem.Definition"/>; empty when it has none.</summary>
        public IReadOnlyList<TextSpan> Hits { get; }
    }

    public static class UsageFinder
    {
        private const SearchObjectType Referrers =
            SearchObjectType.Table | SearchObjectType.View | SearchObjectType.Procedure | SearchObjectType.Function | SearchObjectType.Trigger;

        /// <summary>sys.objects.type as a Search type; None for anything the Search window has no type for (a synonym).</summary>
        public static SearchObjectType TypeOf(string typeCode)
        {
            switch ((typeCode ?? "").Trim().ToUpperInvariant())
            {
                case "U": return SearchObjectType.Table;
                case "V": return SearchObjectType.View;
                case "P": case "PC": return SearchObjectType.Procedure;
                case "FN": case "IF": case "TF": case "FS": case "FT": return SearchObjectType.Function;
                case "TR": case "TA": return SearchObjectType.Trigger;
                default: return SearchObjectType.None;
            }
        }

        /// <summary>Merges the database's rows with the Search Index of the target's database: one Usage per referrer (the
        /// strongest <see cref="UsageKind"/> wins), carrying the index item for its Definition and the places the target's
        /// name is written in it. The target itself is not its own referrer. Ordered by evidence, then schema and name.</summary>
        public static IReadOnlyList<Usage> Merge(UsageTarget target, IEnumerable<RawUsage> rows, IReadOnlyList<SearchItem> index)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var known = new Dictionary<string, SearchItem>(StringComparer.OrdinalIgnoreCase);
            if (index != null)
                foreach (var item in index)
                    if (item != null && item.Schema != null && (item.Type & Referrers) != 0 && item.Type != SearchObjectType.None &&
                        !known.ContainsKey(Key(item.Schema, item.Name)))
                        known[Key(item.Schema, item.Name)] = item;

            var best = new Dictionary<string, (RawUsage row, UsageKind kind)>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows ?? Enumerable.Empty<RawUsage>())
            {
                if (row == null || row.Name == null) continue;
                if (string.Equals(row.Schema, target.Schema, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Name, target.Name, StringComparison.OrdinalIgnoreCase)) continue;
                string key = Key(row.Schema, row.Name);
                // sys.sql_expression_dependencies lists a synonym as an ordinary dependency (type SN): it is still a synonym.
                var kind = (row.TypeCode ?? "").Trim().ToUpperInvariant() == "SN" ? UsageKind.Synonym : row.Kind;
                if (!best.TryGetValue(key, out var seen) || kind < seen.kind) best[key] = (row, kind);
            }

            var matcher = NameMatcher(target.Name);
            var usages = new List<Usage>();
            foreach (var pair in best)
            {
                var row = pair.Value.row;
                if (!known.TryGetValue(pair.Key, out var referrer))
                    referrer = new SearchItem(target.Database, row.Schema, row.Name, TypeOf(row.TypeCode),
                        subKind: pair.Value.kind == UsageKind.Synonym ? "Synonym" : null);
                usages.Add(new Usage(referrer, pair.Value.kind, matcher.FindAll(referrer.Definition)));
            }
            return usages
                .OrderBy(u => u.Kind)
                .ThenBy(u => u.Referrer.Schema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.Referrer.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The Usages of a column, from the Usages of its owner: the referrers whose Definition names the column.
        /// This is a text search, not a dependency (SQL Server records column dependencies only for schema-bound
        /// objects), so another table's column of the same name can match: every result is a <see cref="UsageKind.TextMatch"/>.</summary>
        public static IReadOnlyList<Usage> ForColumn(UsageTarget column, IReadOnlyList<Usage> ownerUsages)
        {
            if (column == null) throw new ArgumentNullException(nameof(column));
            if (!column.IsColumn) throw new ArgumentException("The target is not a column.", nameof(column));
            var matcher = NameMatcher(column.Column);
            var result = new List<Usage>();
            foreach (var usage in ownerUsages ?? new Usage[0])
            {
                var hits = matcher.FindAll(usage.Referrer.Definition);
                if (hits.Count > 0) result.Add(new Usage(usage.Referrer, UsageKind.TextMatch, hits));
            }
            return result;
        }

        private static SearchMatcher NameMatcher(string name) =>
            SearchMatcher.Create(new SearchQuery { Text = name, WholeWord = true, Types = SearchObjectType.All });

        private static string Key(string schema, string name) => schema + "\0" + name;
    }
}
