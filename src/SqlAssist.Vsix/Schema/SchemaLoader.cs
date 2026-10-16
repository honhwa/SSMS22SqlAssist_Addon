using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using SqlAssist.Core.Schema;

namespace SqlAssist.Schema
{
    /// <summary>Reads tables, views, their columns and foreign keys of one database in a single round trip.</summary>
    internal static class SchemaLoader
    {
        private const string Sql = @"
SET NOCOUNT ON;
SELECT s.name, o.name, o.type, c.name, t.name, c.max_length, c.precision, c.scale
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
JOIN sys.columns c ON c.object_id = o.object_id
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0
ORDER BY s.name, o.name, c.column_id;

SELECT fk.name, ps.name, pt.name, rs.name, rt.name, pc.name, rc.name
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.objects pt ON pt.object_id = fkc.parent_object_id
JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.objects rt ON rt.object_id = fkc.referenced_object_id
JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
ORDER BY fk.object_id, fkc.constraint_column_id;";

        public static async Task<SchemaModel> LoadAsync(string connectionString, CancellationToken token)
        {
            var objects = new List<DbObject>();
            var byKey = new Dictionary<string, DbObject>(System.StringComparer.OrdinalIgnoreCase);
            var fks = new List<ForeignKey>();

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (var cmd = new SqlCommand(Sql, conn) { CommandTimeout = 30 })
                using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    string curSchema = null, curName = null, curType = null;
                    var columns = new List<DbColumn>();
                    void FlushObject()
                    {
                        if (curName == null) return;
                        var o = new DbObject(curSchema, curName, curType.Trim() == "V", columns.ToArray());
                        objects.Add(o);
                        byKey[curSchema + "." + curName] = o;
                    }

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        string schema = reader.GetString(0), name = reader.GetString(1);
                        if (schema != curSchema || name != curName)
                        {
                            FlushObject();
                            curSchema = schema; curName = name; curType = reader.GetString(2);
                            columns.Clear();
                        }
                        columns.Add(new DbColumn(reader.GetString(3), FormatType(reader.GetString(4), reader.GetInt16(5), reader.GetByte(6), reader.GetByte(7))));
                    }
                    FlushObject();

                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    string curFk = null;
                    DbObject from = null, to = null;
                    var pairs = new List<(string, string)>();
                    void FlushKey()
                    {
                        if (curFk != null && from != null && to != null) fks.Add(new ForeignKey(curFk, from, to, pairs.ToArray()));
                    }

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        string fkName = reader.GetString(0);
                        if (fkName != curFk)
                        {
                            FlushKey();
                            curFk = fkName;
                            byKey.TryGetValue(reader.GetString(1) + "." + reader.GetString(2), out from);
                            byKey.TryGetValue(reader.GetString(3) + "." + reader.GetString(4), out to);
                            pairs.Clear();
                        }
                        pairs.Add((reader.GetString(5), reader.GetString(6)));
                    }
                    FlushKey();
                }
            }
            return new SchemaModel(objects, fks);
        }

        private static string FormatType(string type, short maxLength, byte precision, byte scale)
        {
            switch (type)
            {
                case "varchar": case "char": case "varbinary": case "binary":
                    return type + "(" + (maxLength == -1 ? "max" : maxLength.ToString()) + ")";
                case "nvarchar": case "nchar":
                    return type + "(" + (maxLength == -1 ? "max" : (maxLength / 2).ToString()) + ")";
                case "decimal": case "numeric":
                    return type + "(" + precision + "," + scale + ")";
                default:
                    return type;
            }
        }
    }
}
