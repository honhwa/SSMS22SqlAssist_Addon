using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Schema
{
    public sealed class DbColumn
    {
        public DbColumn(string name, string typeName) { Name = name; TypeName = typeName; }
        public string Name { get; }
        public string TypeName { get; }
    }

    public sealed class DbObject
    {
        public DbObject(string schema, string name, bool isView, IReadOnlyList<DbColumn> columns)
        {
            Schema = schema; Name = name; IsView = isView; Columns = columns;
        }
        public string Schema { get; }
        public string Name { get; }
        public bool IsView { get; }
        public IReadOnlyList<DbColumn> Columns { get; }
        public override string ToString() => Schema + "." + Name;
    }

    public sealed class ForeignKey
    {
        public ForeignKey(string name, DbObject from, DbObject to, IReadOnlyList<(string from, string to)> columns)
        {
            Name = name; From = from; To = to; Columns = columns;
        }
        public string Name { get; }
        /// <summary>The referencing (child) table.</summary>
        public DbObject From { get; }
        /// <summary>The referenced (parent) table.</summary>
        public DbObject To { get; }
        /// <summary>Column pairs in key order: From column → To column.</summary>
        public IReadOnlyList<(string from, string to)> Columns { get; }
    }

    public sealed class SchemaModel
    {
        public const string DefaultSchema = "dbo";

        private readonly Dictionary<string, List<DbObject>> _byName = new Dictionary<string, List<DbObject>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<DbObject, List<ForeignKey>> _from = new Dictionary<DbObject, List<ForeignKey>>();
        private readonly Dictionary<DbObject, List<ForeignKey>> _to = new Dictionary<DbObject, List<ForeignKey>>();

        public SchemaModel(IReadOnlyList<DbObject> objects, IReadOnlyList<ForeignKey> foreignKeys)
        {
            Objects = objects;
            ForeignKeys = foreignKeys;
            foreach (var o in objects)
            {
                if (!_byName.TryGetValue(o.Name, out var list)) _byName[o.Name] = list = new List<DbObject>();
                list.Add(o);
            }
            foreach (var fk in foreignKeys)
            {
                Add(_from, fk.From, fk);
                Add(_to, fk.To, fk);
            }
        }

        public static SchemaModel Empty { get; } = new SchemaModel(new DbObject[0], new ForeignKey[0]);

        public IReadOnlyList<DbObject> Objects { get; }
        public IReadOnlyList<ForeignKey> ForeignKeys { get; }

        public IEnumerable<string> Schemas => Objects.Select(o => o.Schema).Distinct(StringComparer.OrdinalIgnoreCase);

        /// <summary>Finds a table or view. Without a schema, dbo wins, then any unique match.</summary>
        public DbObject Find(string schema, string name)
        {
            if (name == null || !_byName.TryGetValue(name, out var list)) return null;
            if (schema != null) return list.FirstOrDefault(o => string.Equals(o.Schema, schema, StringComparison.OrdinalIgnoreCase));
            return list.FirstOrDefault(o => string.Equals(o.Schema, DefaultSchema, StringComparison.OrdinalIgnoreCase))
                ?? (list.Count == 1 ? list[0] : null);
        }

        public DbObject Find(TableRef r) => r.HasColumns ? Find(r.Schema, r.Name) : null;

        public IReadOnlyList<ForeignKey> ForeignKeysFrom(DbObject o) => _from.TryGetValue(o, out var l) ? l : (IReadOnlyList<ForeignKey>)new ForeignKey[0];
        public IReadOnlyList<ForeignKey> ForeignKeysTo(DbObject o) => _to.TryGetValue(o, out var l) ? l : (IReadOnlyList<ForeignKey>)new ForeignKey[0];

        /// <summary>The name to insert for an object: dbo objects bare, others schema-qualified.</summary>
        public static string DisplayName(DbObject o)
        {
            string name = SqlIdentifier.QuoteIfNeeded(o.Name);
            return string.Equals(o.Schema, DefaultSchema, StringComparison.OrdinalIgnoreCase)
                ? name
                : SqlIdentifier.QuoteIfNeeded(o.Schema) + "." + name;
        }

        private static void Add(Dictionary<DbObject, List<ForeignKey>> map, DbObject key, ForeignKey fk)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<ForeignKey>();
            list.Add(fk);
        }
    }
}
