using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Results
{
    /// <summary>What the user chose in the UPDATE dialog. Nothing here is guessed from the result set: the table
    /// is whatever was typed and the key columns are the ones ticked.</summary>
    public sealed class UpdateScriptOptions
    {
        public UpdateScriptOptions(string table, IReadOnlyList<int> setColumns, IReadOnlyList<int> keyColumns)
        {
            Table = table; SetColumns = setColumns; KeyColumns = keyColumns;
        }

        /// <summary>As typed, e.g. dbo.Orders or [Sales].[Order Line].</summary>
        public string Table { get; }
        /// <summary>Indexes into the result set's columns, for SET.</summary>
        public IReadOnlyList<int> SetColumns { get; }
        /// <summary>Indexes into the result set's columns, for WHERE.</summary>
        public IReadOnlyList<int> KeyColumns { get; }
    }

    public sealed class UpdateScriptResult
    {
        public UpdateScriptResult(string script, IReadOnlyList<string> errors)
        {
            Script = script; Errors = errors;
        }

        /// <summary>Null when <see cref="Errors"/> is not empty.</summary>
        public string Script { get; }
        /// <summary>What the user must fix before a script can be made, in Thai.</summary>
        public IReadOnlyList<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>Several selected rows whose key columns hold the same values, so their UPDATEs share one WHERE.</summary>
    public sealed class DuplicateKey
    {
        public DuplicateKey(string condition, IReadOnlyList<int> rows)
        {
            Condition = condition; Rows = rows;
        }

        /// <summary>The WHERE condition they share, e.g. "Id = 1 AND Code IS NULL".</summary>
        public string Condition { get; }
        /// <summary>1-based positions within the selected rows.</summary>
        public IReadOnlyList<int> Rows { get; }
    }

    /// <summary>Results grid › Generate Script As › UPDATE: one UPDATE per selected row, SET from the chosen columns and
    /// WHERE from the chosen key columns. The key is the user's claim; the script cannot know it is unique in the table.</summary>
    public static class UpdateScriptBuilder
    {
        /// <param name="columns">Every data column of the result set.</param>
        /// <param name="rows">One array per selected row, indexed like <paramref name="columns"/>; only the SET and key
        /// entries are read.</param>
        public static UpdateScriptResult Build(IReadOnlyList<ResultColumn> columns, IReadOnlyList<object[]> rows,
            UpdateScriptOptions options, string newline = "\r\n")
        {
            var errors = new List<string>();
            if (!SqlObjectName.TryParse(options.Table, out string table, out string tableError)) errors.Add("ชื่อตารางปลายทาง: " + tableError);
            if (options.SetColumns.Count == 0) errors.Add("เลือกคอลัมน์ที่จะ SET อย่างน้อย 1 คอลัมน์");
            if (options.KeyColumns.Count == 0) errors.Add("เลือกคอลัมน์ Key อย่างน้อย 1 คอลัมน์ (ใช้สร้าง WHERE)");
            if (!InRange(columns, options.SetColumns) || !InRange(columns, options.KeyColumns)) errors.Add("มีคอลัมน์ที่เลือกซึ่งไม่อยู่ในผลลัพธ์");
            if (rows.Count == 0) errors.Add("ไม่มีแถวที่เลือก");
            if (errors.Count > 0) return new UpdateScriptResult(null, errors);

            var names = ResultScriptBuilder.ColumnNames(columns);
            var sb = new StringBuilder();
            sb.Append("SET DATEFORMAT ymd").Append(newline).Append(newline);
            foreach (var row in rows)
            {
                sb.Append("UPDATE ").Append(table).Append(" SET ");
                sb.Append(string.Join(", ", options.SetColumns.Select(i => names[i] + " = " + SqlLiteral.Format(row[i], columns[i].TypeName))));
                sb.Append(" WHERE ").Append(KeyCondition(columns, names, options.KeyColumns, row));
                sb.Append(newline).Append("GO").Append(newline);
            }
            return new UpdateScriptResult(sb.ToString(), errors);
        }

        /// <summary>Groups of rows that would get the same WHERE, in order of their first row. Values compare by their SQL
        /// literal and NULLs equal each other, so this is what the generated conditions say, not what a collation might
        /// treat as equal.</summary>
        public static IReadOnlyList<DuplicateKey> FindDuplicateKeys(IReadOnlyList<ResultColumn> columns, IReadOnlyList<object[]> rows,
            IReadOnlyList<int> keyColumns)
        {
            if (keyColumns.Count == 0 || !InRange(columns, keyColumns)) return new DuplicateKey[0];
            var names = ResultScriptBuilder.ColumnNames(columns);
            var groups = new Dictionary<string, List<int>>();
            var order = new List<string>();
            for (int r = 0; r < rows.Count; r++)
            {
                string condition = KeyCondition(columns, names, keyColumns, rows[r]);
                if (!groups.TryGetValue(condition, out var list))
                {
                    groups[condition] = list = new List<int>();
                    order.Add(condition);
                }
                list.Add(r + 1);
            }
            return order.Where(c => groups[c].Count > 1).Select(c => new DuplicateKey(c, groups[c])).ToList();
        }

        private static string KeyCondition(IReadOnlyList<ResultColumn> columns, string[] names, IReadOnlyList<int> keys, object[] row) =>
            string.Join(" AND ", keys.Select(i => ResultScriptBuilder.Condition(names[i], row[i], columns[i].TypeName)));

        private static bool InRange(IReadOnlyList<ResultColumn> columns, IReadOnlyList<int> indexes) =>
            indexes.All(i => i >= 0 && i < columns.Count);
    }
}
