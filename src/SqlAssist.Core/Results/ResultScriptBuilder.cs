using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Results
{
    /// <summary>What Generate Script As writes from the cells selected in a results grid.</summary>
    public enum ResultScriptKind
    {
        Insert,
        InsertTempTable,
        WhereIn,
        WhereAndOr,
        Update,
    }

    /// <summary>One data column of a result set (the grid's row-number column is not one).</summary>
    public sealed class ResultColumn
    {
        public ResultColumn(string name, string typeName, bool isKey = false, string baseSchema = null, string baseTable = null)
        {
            Name = name; TypeName = typeName; IsKey = isKey; BaseSchema = baseSchema; BaseTable = baseTable;
        }

        /// <summary>As the grid shows it; may be empty ("(No column name)") or repeated.</summary>
        public string Name { get; }
        /// <summary>Formatted server type, e.g. nvarchar(25) or decimal(18,2); null when unknown.</summary>
        public string TypeName { get; }
        /// <summary>Part of the source table's key, when the result set says so.</summary>
        public bool IsKey { get; }
        public string BaseSchema { get; }
        public string BaseTable { get; }
    }

    /// <summary>Builds INSERT / INSERT #tmpTable / WHERE IN() / WHERE AND OR / UPDATE scripts from result-grid cells,
    /// in the shape dbForge SQL Complete writes them. Values are what the grid holds: null or DBNull for NULL,
    /// otherwise CLR values (string, numbers, DateTime, byte[]…) whose literal form follows the column's SQL type.</summary>
    public static class ResultScriptBuilder
    {
        /// <summary>The placeholder target when the result set does not name a single source table.</summary>
        public const string DefaultTable = "[Table]";
        public const string TempTable = "#tmpTable";
        /// <summary>SQL Server's limit on row value expressions in one INSERT ... VALUES.</summary>
        public const int MaxRowsPerInsert = 1000;

        /// <summary>The columns <see cref="Build"/> reads for each row: the selected ones, plus for UPDATE the key
        /// columns its WHERE clause needs.</summary>
        public static IReadOnlyList<int> RequiredColumns(ResultScriptKind kind, IReadOnlyList<ResultColumn> columns, IReadOnlyList<int> selected)
        {
            var needed = new List<int>(selected);
            if (kind == ResultScriptKind.Update)
                foreach (int k in KeyColumns(columns))
                    if (!needed.Contains(k)) needed.Add(k);
            return needed;
        }

        /// <param name="columns">Every data column of the result set.</param>
        /// <param name="selected">Indexes into <paramref name="columns"/>, in grid order.</param>
        /// <param name="rows">One array per selected row, indexed like <paramref name="columns"/>; only the
        /// <see cref="RequiredColumns"/> entries are read.</param>
        public static string Build(ResultScriptKind kind, IReadOnlyList<ResultColumn> columns, IReadOnlyList<int> selected,
            IReadOnlyList<object[]> rows, string newline = "\r\n")
        {
            if (selected.Count == 0 || rows.Count == 0) return "";
            var names = ColumnNames(columns);
            switch (kind)
            {
                case ResultScriptKind.Insert: return Insert(TargetTable(columns, selected), columns, names, selected, rows, newline);
                case ResultScriptKind.InsertTempTable: return InsertTempTable(columns, names, selected, rows, newline);
                case ResultScriptKind.WhereIn: return WhereIn(columns, names, selected, rows, newline);
                case ResultScriptKind.WhereAndOr: return WhereAndOr(columns, names, selected, rows, newline);
                case ResultScriptKind.Update: return Update(columns, names, selected, rows, newline);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static string Insert(string table, IReadOnlyList<ResultColumn> columns, string[] names, IReadOnlyList<int> selected,
            IReadOnlyList<object[]> rows, string nl, bool header = true)
        {
            var sb = new StringBuilder();
            if (header) sb.Append("SET DATEFORMAT ymd").Append(nl).Append(nl);
            string columnList = string.Join(", ", selected.Select(i => names[i]));
            for (int start = 0; start < rows.Count; start += MaxRowsPerInsert)
            {
                if (start > 0) sb.Append(nl);
                sb.Append("INSERT INTO ").Append(table).Append(" (").Append(columnList).Append(") VALUES").Append(nl);
                int end = Math.Min(rows.Count, start + MaxRowsPerInsert);
                for (int r = start; r < end; r++)
                {
                    sb.Append('(').Append(string.Join(", ", selected.Select(i => SqlLiteral.Format(rows[r][i], columns[i].TypeName)))).Append(')');
                    sb.Append(r + 1 < end ? "," : "").Append(nl);
                }
                sb.Append("GO").Append(nl);
            }
            return sb.ToString();
        }

        private static string InsertTempTable(IReadOnlyList<ResultColumn> columns, string[] names, IReadOnlyList<int> selected,
            IReadOnlyList<object[]> rows, string nl)
        {
            var sb = new StringBuilder();
            sb.Append("SET DATEFORMAT ymd").Append(nl).Append(nl);
            sb.Append("CREATE TABLE ").Append(TempTable).Append(" (").Append(nl);
            sb.Append(string.Join("," + nl, selected.Select(i => "    " + names[i] + " " + TempTableType(columns[i].TypeName))));
            sb.Append(')').Append(nl).Append(nl);
            sb.Append(Insert(TempTable, columns, names, selected, rows, nl, header: false));
            sb.Append(nl).Append("SELECT * FROM ").Append(TempTable).Append(nl).Append(nl);
            sb.Append("DROP TABLE ").Append(TempTable).Append(nl).Append("GO").Append(nl);
            return sb.ToString();
        }

        private static string WhereIn(IReadOnlyList<ResultColumn> columns, string[] names, IReadOnlyList<int> selected,
            IReadOnlyList<object[]> rows, string nl)
        {
            var conditions = new List<string>();
            foreach (int i in selected)
            {
                bool hasNull = false;
                var values = new List<string>();
                foreach (var row in rows)
                {
                    if (SqlLiteral.IsNull(row[i])) { hasNull = true; continue; }
                    string literal = SqlLiteral.Format(row[i], columns[i].TypeName);
                    if (!values.Contains(literal)) values.Add(literal);
                }
                string condition = values.Count > 0 ? names[i] + " IN(" + string.Join(", ", values) + ")" : null;
                // IN (NULL) never matches, so NULLs become their own test.
                if (hasNull) condition = condition == null ? names[i] + " IS NULL" : "(" + condition + " OR " + names[i] + " IS NULL)";
                conditions.Add(condition);
            }
            return "WHERE " + string.Join(nl + "  AND ", conditions) + nl;
        }

        private static string WhereAndOr(IReadOnlyList<ResultColumn> columns, string[] names, IReadOnlyList<int> selected,
            IReadOnlyList<object[]> rows, string nl)
        {
            var groups = new List<string>();
            foreach (var row in rows)
            {
                string group = "(" + string.Join(" AND ", selected.Select(i => Condition(names[i], row[i], columns[i].TypeName))) + ")";
                if (!groups.Contains(group)) groups.Add(group);
            }
            return "WHERE " + string.Join(" OR" + nl, groups) + nl;
        }

        private static string Update(IReadOnlyList<ResultColumn> columns, string[] names, IReadOnlyList<int> selected,
            IReadOnlyList<object[]> rows, string nl)
        {
            var keys = KeyColumns(columns);
            var set = selected.Where(i => !keys.Contains(i)).ToList();
            if (set.Count == 0) set = selected.ToList();
            string table = TargetTable(columns, selected.Concat(keys).ToList());

            var sb = new StringBuilder();
            sb.Append("SET DATEFORMAT ymd").Append(nl).Append(nl);
            foreach (var row in rows)
            {
                sb.Append("UPDATE ").Append(table).Append(" SET ");
                sb.Append(string.Join(", ", set.Select(i => names[i] + " = " + SqlLiteral.Format(row[i], columns[i].TypeName))));
                sb.Append(" WHERE ").Append(string.Join(" AND ", keys.Select(i => Condition(names[i], row[i], columns[i].TypeName))));
                sb.Append(nl).Append("GO").Append(nl);
            }
            return sb.ToString();
        }

        /// <summary>The columns an UPDATE's WHERE uses: the result set's key columns when it reports them, otherwise
        /// its first column (an ID by convention).</summary>
        private static List<int> KeyColumns(IReadOnlyList<ResultColumn> columns)
        {
            var keys = new List<int>();
            for (int i = 0; i < columns.Count; i++)
                if (columns[i].IsKey) keys.Add(i);
            if (keys.Count == 0 && columns.Count > 0) keys.Add(0);
            return keys;
        }

        private static string Condition(string name, object value, string typeName) =>
            SqlLiteral.IsNull(value) ? name + " IS NULL" : name + " = " + SqlLiteral.Format(value, typeName);

        /// <summary>The source table when every column involved comes from the same one; otherwise [Table].</summary>
        private static string TargetTable(IReadOnlyList<ResultColumn> columns, IReadOnlyList<int> used)
        {
            string schema = null, table = null;
            foreach (int i in used)
            {
                var c = columns[i];
                if (string.IsNullOrEmpty(c.BaseTable)) return DefaultTable;
                if (table == null) { table = c.BaseTable; schema = c.BaseSchema; }
                else if (!string.Equals(table, c.BaseTable, StringComparison.OrdinalIgnoreCase) ||
                         !string.Equals(schema ?? "", c.BaseSchema ?? "", StringComparison.OrdinalIgnoreCase)) return DefaultTable;
            }
            if (table == null) return DefaultTable;
            return string.IsNullOrEmpty(schema) ? SqlIdentifier.QuoteIfNeeded(table)
                : SqlIdentifier.QuoteIfNeeded(schema) + "." + SqlIdentifier.QuoteIfNeeded(table);
        }

        /// <summary>Script-ready column names: unnamed columns become Column1, Column2…, repeats get _2, _3….</summary>
        private static string[] ColumnNames(IReadOnlyList<ResultColumn> columns)
        {
            var result = new string[columns.Count];
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < columns.Count; i++)
            {
                string name = columns[i].Name;
                if (string.IsNullOrWhiteSpace(name) || name == "(No column name)") name = "Column" + (i + 1);
                string unique = name;
                for (int n = 2; !used.Add(unique); n++) unique = name + "_" + n;
                result[i] = SqlIdentifier.QuoteIfNeeded(unique);
            }
            return result;
        }

        /// <summary>A type a temp table column can hold: rowversion values go in as binary(8), unknown types as sql_variant.</summary>
        private static string TempTableType(string typeName)
        {
            string baseType = SqlLiteral.BaseType(typeName);
            if (baseType.Length == 0) return "sql_variant";
            if (baseType == "timestamp" || baseType == "rowversion") return "binary(8)";
            return typeName.Trim();
        }
    }

    /// <summary>T-SQL literals for result values.</summary>
    public static class SqlLiteral
    {
        private static readonly HashSet<string> Numeric = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "bit", "tinyint", "smallint", "int", "bigint", "decimal", "numeric", "money", "smallmoney", "float", "real" };
        private static readonly HashSet<string> Binary = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "binary", "varbinary", "image", "timestamp", "rowversion" };
        /// <summary>Written without the N prefix: non-Unicode strings, and the dates, times and GUIDs the grid hands over as text.</summary>
        private static readonly HashSet<string> Ansi = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "char", "varchar", "text", "date", "time", "datetime", "datetime2", "smalldatetime", "datetimeoffset", "uniqueidentifier" };

        public static bool IsNull(object value) =>
            value == null || value is DBNull || (value is System.Data.SqlTypes.INullable n && n.IsNull);

        /// <summary>"nvarchar(25)" → "nvarchar"; "sys.int" → "int"; null → "".</summary>
        public static string BaseType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return "";
            string t = typeName.Trim();
            int paren = t.IndexOf('(');
            if (paren >= 0) t = t.Substring(0, paren);
            int dot = t.LastIndexOf('.');
            if (dot >= 0) t = t.Substring(dot + 1);
            return t.Trim().Trim('[', ']').ToLowerInvariant();
        }

        public static string Format(object value, string typeName)
        {
            if (IsNull(value)) return "NULL";
            string type = BaseType(typeName);
            switch (value)
            {
                case bool b: return b ? "1" : "0";
                case byte[] bytes: return Hex(bytes);
                case DateTime dt: return "'" + FormatDateTime(dt, type) + "'";
                case DateTimeOffset dto: return "'" + dto.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture) + "'";
                case TimeSpan ts: return "'" + ts.ToString(ts.Ticks % TimeSpan.TicksPerSecond == 0 ? @"hh\:mm\:ss" : @"hh\:mm\:ss\.fffffff", CultureInfo.InvariantCulture) + "'";
                case Guid g: return "'" + g.ToString("D").ToUpperInvariant() + "'";
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case decimal m: return m.ToString(CultureInfo.InvariantCulture);
                case sbyte _: case byte _: case short _: case ushort _: case int _: case uint _: case long _: case ulong _:
                    return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
            }

            // SqlDecimal, SqlMoney, strings the grid already formatted, …
            string text = value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString();
            if (Numeric.Contains(type))
            {
                if (type == "bit")
                {
                    if (string.Equals(text, "True", StringComparison.OrdinalIgnoreCase)) return "1";
                    if (string.Equals(text, "False", StringComparison.OrdinalIgnoreCase)) return "0";
                }
                return text.Trim();
            }
            if (Binary.Contains(type) && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return text.Trim();
            return (Ansi.Contains(type) ? "'" : "N'") + text.Replace("'", "''") + "'";
        }

        private static string FormatDateTime(DateTime dt, string type)
        {
            switch (type)
            {
                case "date": return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case "smalldatetime": return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                case "datetime2": return dt.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
                default: return dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            }
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(2 + bytes.Length * 2).Append("0x");
            foreach (byte b in bytes) sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
