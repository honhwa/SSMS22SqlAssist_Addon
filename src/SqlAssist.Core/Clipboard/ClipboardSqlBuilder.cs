using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SqlAssist.Core.Results;
using SqlAssist.Core.Sql;

namespace SqlAssist.Core.Clipboard
{
    /// <summary>Everything the CSV to SQL dialog decides. <see cref="Rows"/> are the parsed cells, header row included
    /// when <see cref="FirstRowIsHeader"/>.</summary>
    public sealed class ClipboardSqlRequest
    {
        public IReadOnlyList<string[]> Rows { get; set; }
        public bool FirstRowIsHeader { get; set; }
        /// <summary>One per column.</summary>
        public IReadOnlyList<ClipboardColumnType> ColumnTypes { get; set; }
        public ClipboardSqlKind Kind { get; set; }
        /// <summary>The column an IN list is made from.</summary>
        public int InColumn { get; set; }
        /// <summary>IN only: each value once, in the order first seen.</summary>
        public bool Distinct { get; set; }
        /// <summary>TempTable only: #Name or ##Name.</summary>
        public string TempTableName { get; set; } = "#Temp";
        public EmptyCellRule EmptyCell { get; set; } = EmptyCellRule.Null;
        public NullTextRule NullText { get; set; } = NullTextRule.Null;
        public DateInputFormat DateFormat { get; set; } = DateInputFormat.Iso;
        public string Newline { get; set; } = "\r\n";
        /// <summary>When above 0, only this many rows (IN: values) are written; every cell is still checked.</summary>
        public int PreviewRows { get; set; }
    }

    public sealed class ClipboardSqlResult
    {
        public ClipboardSqlResult(string script, IReadOnlyList<string> errors, IReadOnlyList<string> warnings, bool isTruncated, int rowCount)
        {
            Script = script; Errors = errors; Warnings = warnings; IsTruncated = isTruncated; RowCount = rowCount;
        }

        /// <summary>Null when <see cref="Errors"/> is not empty.</summary>
        public string Script { get; }
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }
        /// <summary>The script holds only the first <see cref="ClipboardSqlRequest.PreviewRows"/>.</summary>
        public bool IsTruncated { get; }
        /// <summary>Data rows, not counting a header row.</summary>
        public int RowCount { get; }
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>CSV to SQL: IN (...), VALUES (...), or CREATE TABLE #Temp + INSERT from pasted cells. Values are
    /// literals built by the same <see cref="SqlLiteral"/> and INSERT writer that Generate Script As uses; dates are
    /// written in a form SET DATEFORMAT / SET LANGUAGE cannot misread. Nothing is converted silently: a cell that is
    /// not a value of its column's type is an error naming its row and column.</summary>
    public static class ClipboardSqlBuilder
    {
        public const int MaxReportedProblems = DelimitedTextParser.MaxReportedProblems;
        /// <summary>An IN list this long starts to cost the query processor; a #Temp table and a JOIN do better.</summary>
        public const int LargeInListWarning = 10000;
        private const int InValuesPerLine = 10;
        private const int MaxDecimalPrecision = 38;

        /// <summary>The name each column has in the pasted data: the header cell, or Column1, Column2… when there is none
        /// (or it is empty). Raw text, for showing; scripts quote the names themselves.</summary>
        public static IReadOnlyList<string> HeaderNames(IReadOnlyList<string[]> rows, bool firstRowIsHeader, int columnCount)
        {
            var names = new string[columnCount];
            for (int c = 0; c < columnCount; c++)
            {
                string name = firstRowIsHeader && rows.Count > 0 && c < rows[0].Length ? rows[0][c] : "";
                names[c] = string.IsNullOrWhiteSpace(name) ? "Column" + (c + 1) : name;
            }
            return names;
        }

        public static ClipboardSqlResult Build(ClipboardSqlRequest request)
        {
            var errors = new Problems();
            var warnings = new Problems();
            int columnCount = request.Rows.Count > 0 ? request.Rows[0].Length : 0;
            int firstData = request.FirstRowIsHeader ? 1 : 0;
            int dataRows = Math.Max(0, request.Rows.Count - firstData);

            if (columnCount == 0 || dataRows == 0) errors.Add("没有数据行（请先粘贴数据" + (request.FirstRowIsHeader ? "，或取消勾选\"首行为列名\"" : "") + ")");
            else if (request.ColumnTypes == null || request.ColumnTypes.Count < columnCount) errors.Add("尚未为所有列指定数据类型");
            if (request.Kind == ClipboardSqlKind.In && columnCount > 0 && (request.InColumn < 0 || request.InColumn >= columnCount))
                errors.Add("为 IN 选择的列无效");
            string tempName = request.TempTableName;
            if (request.Kind == ClipboardSqlKind.TempTable && !TryTempTableName(request.TempTableName, out tempName, out string nameError))
                errors.Add("临时表名称：" + nameError);
            if (errors.Total > 0) return Failed(errors, warnings, dataRows);

            var headerNames = HeaderNames(request.Rows, request.FirstRowIsHeader, columnCount);
            var values = Convert(request, columnCount, firstData, headerNames, errors, warnings);
            if (errors.Total > 0) return Failed(errors, warnings, dataRows);

            var columns = new ResultColumn[columnCount];
            for (int c = 0; c < columnCount; c++)
                columns[c] = new ResultColumn(headerNames[c], LiteralType(request.ColumnTypes[c]));

            string script;
            bool truncated;
            switch (request.Kind)
            {
                case ClipboardSqlKind.In: script = RenderIn(request, columns, values, warnings, errors, out truncated); break;
                case ClipboardSqlKind.Values: script = RenderValues(request, columns, values, warnings, out truncated); break;
                default: script = RenderTempTable(request, tempName, columns, values, errors, out truncated); break;
            }
            if (errors.Total > 0) return Failed(errors, warnings, dataRows);
            return new ClipboardSqlResult(script, errors.List, warnings.List, truncated, dataRows);
        }

        // ---- reading the cells ----

        private static List<object[]> Convert(ClipboardSqlRequest request, int columnCount, int firstData, IReadOnlyList<string> names,
            Problems errors, Problems warnings)
        {
            var values = new List<object[]>(request.Rows.Count - firstData);
            var varcharNonAscii = new bool[columnCount];
            for (int r = firstData; r < request.Rows.Count; r++)
            {
                var row = new object[columnCount];
                for (int c = 0; c < columnCount; c++)
                {
                    string cell = request.Rows[r][c];
                    var type = request.ColumnTypes[c];
                    bool isText = type == ClipboardColumnType.Text || type == ClipboardColumnType.Varchar;

                    CellParse parsed;
                    if (cell.Length == 0)
                        parsed = request.EmptyCell == EmptyCellRule.Null ? new CellParse(null)
                            : isText ? new CellParse("")
                            : new CellParse(null, "空单元格不能以空字符串存入 " + TypeName(type) + "（请将空单元格设为 NULL，或把类型改为文本）");
                    else if (cell == "NULL")
                        parsed = request.NullText == NullTextRule.Null ? new CellParse(null)
                            : isText ? new CellParse(cell)
                            : new CellParse(null, "文本 NULL 不能以文本形式存入 " + TypeName(type) + "（请将文本 NULL 视为 SQL NULL，或把类型改为文本）");
                    else
                        parsed = ClipboardValueParser.Parse(cell, type, request.DateFormat);

                    if (!parsed.IsValid)
                        errors.Add(() => Where(r, c, names) + ": " + parsed.Error + (cell.Length > 0 ? " —— 值为 \"" + Shorten(cell) + "\"" : ""));
                    else if (parsed.Warning != null)
                        warnings.Add(() => Where(r, c, names) + ": " + parsed.Warning);
                    if (type == ClipboardColumnType.Varchar && parsed.IsValid && parsed.Value is string s && s.Any(ch => ch > 127)) varcharNonAscii[c] = true;
                    row[c] = parsed.Value;
                }
                values.Add(row);
            }
            for (int c = 0; c < columnCount; c++)
                if (varcharNonAscii[c])
                {
                    int column = c;
                    warnings.Add(() => "列 " + (column + 1) + " (" + names[column] + ") 是 varchar 但包含非 ASCII 字符 —— 若数据库排序规则不支持该字符，数据会损坏，为安全起见请改用文本类型 (nvarchar)");
                }
            return values;
        }

        /// <summary>"第 3 行，第 2 列 (Qty)"：行号把表头行也算进去，和粘贴文本的行号一致。</summary>
        private static string Where(int row, int column, IReadOnlyList<string> names) =>
            "第 " + (row + 1) + " 行，第 " + (column + 1) + " 列 (" + names[column] + ")";

        private static string Shorten(string cell)
        {
            string flat = cell.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            return flat.Length <= 40 ? flat : flat.Substring(0, 40) + "…";
        }

        // ---- IN (...) ----

        private static string RenderIn(ClipboardSqlRequest request, IReadOnlyList<ResultColumn> columns, List<object[]> values,
            Problems warnings, Problems errors, out bool truncated)
        {
            int column = request.InColumn;
            var literals = new List<string>();
            var seen = new HashSet<string>();
            int nulls = 0;
            foreach (var row in values)
            {
                if (SqlLiteral.IsNull(row[column])) { nulls++; continue; }
                string literal = SqlLiteral.Format(row[column], columns[column].TypeName, true);
                if (request.Distinct && !seen.Add(literal)) continue;
                literals.Add(literal);
            }
            if (nulls > 0) warnings.Add(() => "已跳过 NULL " + nulls.ToString("N0", CultureInfo.InvariantCulture) + " 个值 —— IN (...) 不匹配 NULL（如需请另行使用 IS NULL）");
            if (literals.Count == 0)
            {
                errors.Add("IN (...) 中没有可用的值（全部为 NULL）");
                truncated = false;
                return null;
            }
            if (literals.Count > LargeInListWarning)
                warnings.Add(() => "IN (...) 包含 " + literals.Count.ToString("N0", CultureInfo.InvariantCulture) + " 个值，数量过多，SQL Server 可能变慢或无法执行，建议改用 #Temp 再 JOIN");

            truncated = request.PreviewRows > 0 && literals.Count > request.PreviewRows;
            if (truncated) literals = literals.Take(request.PreviewRows).ToList();

            string nl = request.Newline;
            if (literals.Count <= InValuesPerLine) return "IN (" + string.Join(", ", literals) + ")" + nl;
            var sb = new StringBuilder("IN (").Append(nl);
            for (int start = 0; start < literals.Count; start += InValuesPerLine)
            {
                int count = Math.Min(InValuesPerLine, literals.Count - start);
                sb.Append("    ").Append(string.Join(", ", literals.Skip(start).Take(count)));
                sb.Append(start + count < literals.Count ? "," : "").Append(nl);
            }
            return sb.Append(')').Append(nl).ToString();
        }

        // ---- VALUES ----

        private static string RenderValues(ClipboardSqlRequest request, IReadOnlyList<ResultColumn> columns, List<object[]> values,
            Problems warnings, out bool truncated)
        {
            truncated = request.PreviewRows > 0 && values.Count > request.PreviewRows;
            var rows = truncated ? values.Take(request.PreviewRows).ToList() : values;
            var all = Enumerable.Range(0, columns.Count).ToList();
            var sb = new StringBuilder();
            int chunks = 0;
            for (int start = 0; start < rows.Count; start += ResultScriptBuilder.MaxRowsPerInsert)
            {
                if (chunks++ > 0) sb.Append(request.Newline);
                sb.Append("VALUES").Append(request.Newline);
                ResultScriptBuilder.AppendValueRows(sb, columns, all, rows, start, Math.Min(rows.Count, start + ResultScriptBuilder.MaxRowsPerInsert), request.Newline, true);
            }
            if (values.Count > ResultScriptBuilder.MaxRowsPerInsert)
                warnings.Add(() => "VALUES 每批最多 " + ResultScriptBuilder.MaxRowsPerInsert.ToString("N0", CultureInfo.InvariantCulture) +
                                   " 行，因此分为 " + ((values.Count + ResultScriptBuilder.MaxRowsPerInsert - 1) / ResultScriptBuilder.MaxRowsPerInsert) +
                                   " 批，请逐批使用（大数据集请改用 #Temp + INSERT）");
            return sb.ToString();
        }

        // ---- CREATE TABLE #Temp + INSERT ----

        private static string RenderTempTable(ClipboardSqlRequest request, string table, IReadOnlyList<ResultColumn> columns, List<object[]> values,
            Problems errors, out bool truncated)
        {
            var names = ResultScriptBuilder.ColumnNames(columns);
            string nl = request.Newline;
            var definitions = new List<string>();
            for (int c = 0; c < columns.Count; c++)
            {
                string type = ColumnDefinition(request.ColumnTypes[c], c, values, out string problem);
                if (problem != null) { errors.Add(() => "列 " + (c + 1) + " (" + names[c] + "): " + problem); continue; }
                definitions.Add("    " + names[c] + " " + type);
            }
            if (errors.Total > 0) { truncated = false; return null; }

            truncated = request.PreviewRows > 0 && values.Count > request.PreviewRows;
            var rows = truncated ? values.Take(request.PreviewRows).ToList() : values;
            var sb = new StringBuilder();
            sb.Append("CREATE TABLE ").Append(table).Append(" (").Append(nl);
            sb.Append(string.Join("," + nl, definitions)).Append(')').Append(nl).Append(nl);
            sb.Append(ResultScriptBuilder.Insert(table, columns, names, Enumerable.Range(0, columns.Count).ToList(), rows, nl, header: false, languageNeutralDates: true));
            return sb.ToString();
        }

        /// <summary>The column's type in CREATE TABLE, sized from the data: text as long as its longest cell, decimal with
        /// just the digits its values need.</summary>
        private static string ColumnDefinition(ClipboardColumnType type, int column, List<object[]> values, out string problem)
        {
            problem = null;
            switch (type)
            {
                case ClipboardColumnType.Text:
                case ClipboardColumnType.Varchar:
                    int length = Math.Max(1, values.Max(row => (row[column] as string)?.Length ?? 0));
                    int limit = type == ClipboardColumnType.Text ? 4000 : 8000;
                    return (type == ClipboardColumnType.Text ? "nvarchar(" : "varchar(") + (length > limit ? "max" : length.ToString(CultureInfo.InvariantCulture)) + ")";
                case ClipboardColumnType.Decimal:
                    int integerDigits = 0, scale = 0;
                    foreach (var row in values)
                    {
                        if (!(row[column] is decimal d)) continue;
                        scale = Math.Max(scale, (decimal.GetBits(d)[3] >> 16) & 0xFF);
                        decimal whole = Math.Truncate(Math.Abs(d));
                        if (whole >= 1) integerDigits = Math.Max(integerDigits, whole.ToString(CultureInfo.InvariantCulture).Length);
                    }
                    int precision = Math.Max(1, integerDigits + scale);
                    if (precision > MaxDecimalPrecision)
                    {
                        problem = "最宽的值（" + integerDigits + " 位整数，" + scale + " 位小数）超出 decimal(38)";
                        return null;
                    }
                    return "decimal(" + precision + "," + scale + ")";
                default:
                    return TypeName(type);
            }
        }

        private static bool TryTempTableName(string text, out string formatted, out string error)
        {
            if (!SqlObjectName.TryParse(text, out formatted, out error)) return false;
            if (formatted[0] != '#' || formatted.Length < 2)
            {
                formatted = null;
                error = "必须以 #（局部）或 ##（全局）开头，后接字母、数字或 _，例如 #Temp";
                return false;
            }
            return true;
        }

        /// <summary>The type name <see cref="SqlLiteral"/> picks the literal's form from.</summary>
        private static string LiteralType(ClipboardColumnType type) => type == ClipboardColumnType.Text ? "nvarchar" : TypeName(type);

        private static string TypeName(ClipboardColumnType type)
        {
            switch (type)
            {
                case ClipboardColumnType.Text: return "nvarchar";
                case ClipboardColumnType.Varchar: return "varchar";
                case ClipboardColumnType.Int: return "int";
                case ClipboardColumnType.BigInt: return "bigint";
                case ClipboardColumnType.Decimal: return "decimal";
                case ClipboardColumnType.Float: return "float";
                case ClipboardColumnType.Bit: return "bit";
                case ClipboardColumnType.Date: return "date";
                case ClipboardColumnType.DateTime: return "datetime";
                case ClipboardColumnType.DateTime2: return "datetime2";
                case ClipboardColumnType.UniqueIdentifier: return "uniqueidentifier";
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static ClipboardSqlResult Failed(Problems errors, Problems warnings, int dataRows) =>
            new ClipboardSqlResult(null, errors.List, warnings.List, false, dataRows);

        /// <summary>A list of messages that keeps the first few and counts the rest, so a bad column of 100,000 cells does
        /// not build 100,000 strings.</summary>
        private sealed class Problems
        {
            public List<string> List { get; } = new List<string>();
            public int Total { get; private set; }
            private readonly List<string> _shown = new List<string>();

            public void Add(string message) => Add(() => message);

            public void Add(Func<string> message)
            {
                Total++;
                if (_shown.Count < MaxReportedProblems) _shown.Add(message());
                List.Clear();
                List.AddRange(_shown);
                if (Total > _shown.Count) List.Add("共找到 " + Total.ToString("N0", CultureInfo.InvariantCulture) + " 条（显示前 " + _shown.Count + " 条）");
            }
        }
    }
}
