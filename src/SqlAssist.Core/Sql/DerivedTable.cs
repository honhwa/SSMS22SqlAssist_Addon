using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Schema;

namespace SqlAssist.Core.Sql
{
    /// <summary>One select-list item of a CTE or derived table, as written in the query.</summary>
    public sealed class DerivedColumn
    {
        /// <summary>The column's name as the outer query sees it; null for "*" items.</summary>
        public string Name { get; internal set; }
        /// <summary>When the item is a plain column reference ("s.SalesPersonID"): its qualifier (may be null) and column.</summary>
        public string SourceQualifier { get; internal set; }
        public string SourceColumn { get; internal set; }
        /// <summary>"*" or "q.*": every column of the body's tables (or of table q).</summary>
        public bool IsStar { get; internal set; }
        public string StarQualifier { get; internal set; }
    }

    /// <summary>A table that exists only in the query: a CTE ("WITH Sales_CTE (...) AS (SELECT ...)") or a derived table
    /// ("FROM (SELECT ...) d"). Its columns come from the explicit column list or the body's select list; their types
    /// are borrowed from the real columns they pass through, when they do.</summary>
    public sealed class DerivedTable
    {
        private SchemaModel _resolvedFor;
        private DbObject _resolved;
        private bool _resolving;

        public string Name { get; internal set; }
        public bool IsCte { get; internal set; }
        public IReadOnlyList<DerivedColumn> Columns { get; internal set; } = new DerivedColumn[0];
        /// <summary>The FROM/JOIN tables of the body's (first) query block.</summary>
        public IReadOnlyList<TableRef> BodyTables { get; internal set; } = new TableRef[0];

        /// <summary>The table as a schema object. Cached per schema; null while resolving itself (a recursive CTE).</summary>
        public DbObject Resolve(SchemaModel schema)
        {
            if (_resolving) return null;
            if (_resolved != null && ReferenceEquals(_resolvedFor, schema)) return _resolved;
            _resolving = true;
            try
            {
                var sources = BodyTables.Select(t => (t, o: schema.Find(t))).ToList();
                var columns = new List<DbColumn>();
                foreach (var c in Columns)
                {
                    if (c.IsStar)
                    {
                        foreach (var (t, o) in sources.Where(s => s.o != null && (c.StarQualifier == null || s.t.Answers(c.StarQualifier))))
                            columns.AddRange(o.Columns.Select(src => PassThrough(src.Name, src, o)));
                        continue;
                    }
                    var (srcColumn, srcTable) = FindSource(c, sources);
                    columns.Add(srcColumn == null ? new DbColumn(c.Name, "") : PassThrough(c.Name, srcColumn, srcTable));
                }
                _resolved = new DbObject(null, Name, true, columns, kind: IsCte ? "CTE" : "Derived table");
                _resolvedFor = schema;
                return _resolved;
            }
            finally
            {
                _resolving = false;
            }
        }

        private static (DbColumn, DbObject) FindSource(DerivedColumn c, List<(TableRef t, DbObject o)> sources)
        {
            if (c.SourceColumn == null) return (null, null);
            foreach (var (t, o) in sources)
            {
                if (o == null || (c.SourceQualifier != null && !t.Answers(c.SourceQualifier))) continue;
                var col = o.Columns.FirstOrDefault(x => string.Equals(x.Name, c.SourceColumn, StringComparison.OrdinalIgnoreCase));
                if (col != null) return (col, o);
            }
            return (null, null);
        }

        // Keys and identity belong to the base table, not to the query's result; type, nullability and description carry over.
        private static DbColumn PassThrough(string name, DbColumn src, DbObject table) =>
            new DbColumn(name, src.TypeName, src.IsNullable, description: src.Description,
                origin: src.Origin ?? table, originColumn: src.OriginColumn ?? src.Name);
    }
}
