using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SqlAssist.Core.Schema
{
    public enum ColumnKeyKind { None, PrimaryKey, ForeignKey }

    public sealed class ColumnDetail
    {
        public string Name { get; set; }
        /// <summary>Declared type in upper case: NVARCHAR(50), DATETIME2(3).</summary>
        public string Type { get; set; }
        /// <summary>IDENTITY, or NOT NULL, or empty.</summary>
        public string Flags { get; set; }
        public ColumnKeyKind Key { get; set; }
    }

    /// <summary>What the completion list shows beside a selected table or view: its columns with type, key and
    /// nullability, and the estimated row count.</summary>
    public sealed class TableDetails
    {
        public string Schema { get; set; }
        public string Name { get; set; }
        /// <summary>"Table" or "View".</summary>
        public string Kind { get; set; }
        public IReadOnlyList<ColumnDetail> Columns { get; set; }
        /// <summary>Columns left out to keep the panel on screen (see <see cref="Build"/>).</summary>
        public int HiddenColumns { get; set; }
        /// <summary>"60,533", or null for views and unknown counts.</summary>
        public string RowCount { get; set; }

        public static TableDetails Build(SchemaModel schema, DbObject o, int maxColumns = 60)
        {
            var fkColumns = new HashSet<string>(
                schema.ForeignKeysFrom(o).SelectMany(fk => fk.Columns.Select(c => c.from)), StringComparer.OrdinalIgnoreCase);

            var columns = o.Columns.Take(maxColumns).Select(c => new ColumnDetail
            {
                Name = c.Name,
                Type = (c.TypeName ?? "").ToUpperInvariant(),
                // IDENTITY implies NOT NULL, so one word is enough.
                Flags = c.IsIdentity ? "IDENTITY" : c.IsNullable ? "" : "NOT NULL",
                Key = c.IsPrimaryKey ? ColumnKeyKind.PrimaryKey : fkColumns.Contains(c.Name) ? ColumnKeyKind.ForeignKey : ColumnKeyKind.None,
            }).ToList();

            return new TableDetails
            {
                Schema = o.Schema,
                Name = o.Name,
                Kind = o.IsView ? "View" : "Table",
                Columns = columns,
                HiddenColumns = Math.Max(0, o.Columns.Count - maxColumns),
                RowCount = o.IsView || o.RowCount == null ? null : o.RowCount.Value.ToString("N0", CultureInfo.InvariantCulture),
            };
        }
    }
}
