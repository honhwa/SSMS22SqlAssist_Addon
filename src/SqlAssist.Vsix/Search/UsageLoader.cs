using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using SqlAssist.Connection;
using SqlAssist.Core.Search;

namespace SqlAssist.Search
{
    /// <summary>Asks the database which objects refer to a table, view, procedure, function or user-defined type. Read-only:
    /// catalog views only, the names go in as parameters, and nothing is executed on the user's data. It covers the one
    /// database of the target; a reference from another database, dynamic SQL and applications are not visible here.</summary>
    internal static class UsageLoader
    {
        // sys.sql_expression_dependencies keeps an unresolved reference (deferred name resolution: the object did not exist
        // when the module was created) with a NULL id and only the name, hence the second branch of the WHERE. Ids of
        // different referenced classes (object, type, ...) share a number space, so the class is always compared as well.
        // A DEFAULT / CHECK constraint that refers to the target is reported as its table, the thing a person recognises.
        internal const string ObjectSql = @"
DECLARE @id int = OBJECT_ID(QUOTENAME(@schema) + N'.' + QUOTENAME(@name));

SELECT DISTINCT
    CASE WHEN o.type IN ('D', 'C') THEN ps.name ELSE s.name END,
    CASE WHEN o.type IN ('D', 'C') THEN po.name ELSE o.name END,
    CASE WHEN o.type IN ('D', 'C') THEN po.type ELSE o.type END,
    CAST(CASE WHEN d.referenced_id = @id THEN 0 ELSE 1 END AS int)
FROM sys.sql_expression_dependencies d
JOIN sys.objects o ON o.object_id = d.referencing_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.objects po ON po.object_id = o.parent_object_id AND o.type IN ('D', 'C')
LEFT JOIN sys.schemas ps ON ps.schema_id = po.schema_id
WHERE d.referencing_class = 1
  AND ((d.referenced_class = 1 AND d.referenced_id = @id)
    OR (d.referenced_id IS NULL AND d.referenced_database_name IS NULL AND d.referenced_server_name IS NULL
        AND d.referenced_entity_name = @name
        AND (d.referenced_schema_name IS NULL OR d.referenced_schema_name = @schema)));

SELECT DISTINCT s.name, t.name
FROM sys.foreign_keys fk
JOIN sys.tables t ON t.object_id = fk.parent_object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE fk.referenced_object_id = @id;

SELECT s.name, tr.name
FROM sys.triggers tr
JOIN sys.objects ot ON ot.object_id = tr.object_id
JOIN sys.schemas s ON s.schema_id = ot.schema_id
WHERE tr.parent_id = @id;

SELECT s.name, sy.name
FROM sys.synonyms sy
JOIN sys.schemas s ON s.schema_id = sy.schema_id
WHERE PARSENAME(sy.base_object_name, 3) IS NULL AND PARSENAME(sy.base_object_name, 4) IS NULL
  AND OBJECT_ID(sy.base_object_name) = @id;";

        // A column or parameter declared with the type is a certain reference; the dependency rows add the modules that
        // use it inside their body (DECLARE @x dbo.MyType).
        internal const string TypeSql = @"
DECLARE @tid int = (SELECT t.user_type_id FROM sys.types t JOIN sys.schemas ts ON ts.schema_id = t.schema_id
                    WHERE ts.name = @schema AND t.name = @name);

SELECT s.name, o.name, o.type, CAST(0 AS int)
FROM sys.columns c
JOIN sys.objects o ON o.object_id = c.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE c.user_type_id = @tid AND o.is_ms_shipped = 0
UNION
SELECT s.name, o.name, o.type, CAST(0 AS int)
FROM sys.parameters p
JOIN sys.objects o ON o.object_id = p.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE p.user_type_id = @tid AND o.is_ms_shipped = 0
UNION
SELECT s.name, o.name, o.type, CAST(0 AS int)
FROM sys.sql_expression_dependencies d
JOIN sys.objects o ON o.object_id = d.referencing_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE d.referencing_class = 1 AND d.referenced_class = 6 AND d.referenced_id = @tid AND o.is_ms_shipped = 0;";

        /// <param name="server">The connection the Search window is using; the target's database is swapped in.</param>
        /// <exception cref="SqlException">The query failed (no permission, timeout, connection lost): the caller shows it.</exception>
        public static async Task<IReadOnlyList<RawUsage>> LoadAsync(ConnectionTarget server, UsageTarget target, CancellationToken token)
        {
            var rows = new List<RawUsage>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using (var conn = new SqlConnection(SearchIndexLoader.ForDatabase(server.ConnectionString, target.Database)))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                bool isType = target.Type == SearchObjectType.UserDefinedType;
                using (var cmd = new SqlCommand(isType ? TypeSql : ObjectSql, conn) { CommandTimeout = 30 })
                {
                    cmd.Parameters.Add("@schema", SqlDbType.NVarChar, 128).Value = target.Schema;
                    cmd.Parameters.Add("@name", SqlDbType.NVarChar, 128).Value = target.Name;
                    using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        // 1. dependencies, resolved or by name only
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            string code = reader.IsDBNull(2) ? null : reader.GetString(2);
                            if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
                            // A type the Search window has no kind for (a table type's internal object, ...) is not listed;
                            // a synonym is, as itself.
                            if (UsageFinder.TypeOf(code) == SearchObjectType.None && (code ?? "").Trim() != "SN") continue;
                            rows.Add(new RawUsage(reader.GetString(0), reader.GetString(1), code,
                                reader.GetInt32(3) == 1 ? UsageKind.NameOnly : UsageKind.Dependency));
                        }
                        if (isType) return Done(rows, watch, target);

                        // 2. foreign keys that point at the table, 3. triggers on it, 4. synonyms of it
                        await AddAsync(reader, rows, "U", UsageKind.ForeignKey, token).ConfigureAwait(false);
                        await AddAsync(reader, rows, "TR", UsageKind.Trigger, token).ConfigureAwait(false);
                        await AddAsync(reader, rows, "SN", UsageKind.Synonym, token).ConfigureAwait(false);
                    }
                }
            }
            return Done(rows, watch, target);
        }

        private static async Task AddAsync(SqlDataReader reader, List<RawUsage> rows, string typeCode, UsageKind kind, CancellationToken token)
        {
            if (!await reader.NextResultAsync(token).ConfigureAwait(false)) return;
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                if (!reader.IsDBNull(0) && !reader.IsDBNull(1)) rows.Add(new RawUsage(reader.GetString(0), reader.GetString(1), typeCode, kind));
        }

        private static IReadOnlyList<RawUsage> Done(List<RawUsage> rows, System.Diagnostics.Stopwatch watch, UsageTarget target)
        {
            // Names and counts only: definitions and values never go to the log.
            Log.Info("Usages of " + target.DisplayName + " in " + target.Database + ": " + rows.Count + " rows in " + watch.ElapsedMilliseconds + " ms");
            return rows;
        }
    }
}
