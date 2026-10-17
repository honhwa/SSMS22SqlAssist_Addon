using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Search;

namespace SqlAssist.Search
{
    /// <summary>Reads one database's Search Index (schemas, objects with their module source, columns and primary keys)
    /// in a single round trip, and lists the databases a search can cover.</summary>
    internal static class SearchIndexLoader
    {
        // Schema ids 2-4 are guest, INFORMATION_SCHEMA and sys; 16384+ are the db_* role schemas.
        private const string IndexSql = @"
SET NOCOUNT ON;
SELECT s.name FROM sys.schemas s WHERE s.schema_id NOT IN (2, 3, 4) AND s.schema_id < 16384 ORDER BY s.name;

SELECT o.object_id, s.name, o.name, o.type, p.name, m.definition, p.type
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.objects p ON p.object_id = o.parent_object_id AND o.type = 'TR'
LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
WHERE o.is_ms_shipped = 0 AND o.type IN ('U', 'V', 'P', 'PC', 'FN', 'IF', 'TF', 'FS', 'FT', 'TR')
ORDER BY s.name, o.name;

SELECT c.object_id, c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable
FROM sys.columns c
JOIN sys.objects o ON o.object_id = c.object_id
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE o.is_ms_shipped = 0 AND o.type IN ('U', 'V')
ORDER BY c.object_id, c.column_id;

SELECT i.object_id, i.name, CAST(CASE WHEN i.type = 1 THEN 1 ELSE 0 END AS bit), c.name
FROM sys.indexes i
JOIN sys.objects o ON o.object_id = i.object_id
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.is_primary_key = 1 AND o.is_ms_shipped = 0
ORDER BY i.object_id, ic.key_ordinal;";

        private const string DatabasesSql = @"
SELECT name FROM sys.databases
WHERE database_id > 4 AND state = 0 AND HAS_DBACCESS(name) = 1
ORDER BY name;";

        private sealed class ObjectRow
        {
            public string Schema, Name, Parent, Definition;
            public SearchObjectType Type, ParentType;
            public readonly List<TableColumn> Columns = new List<TableColumn>();
            public PrimaryKey Key;
        }

        public static string ForDatabase(string connectionString, string database) =>
            new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database }.ConnectionString;

        /// <summary>User databases that are online and that this login can open. System databases are left out.</summary>
        public static async Task<List<string>> LoadDatabasesAsync(string connectionString, CancellationToken token)
        {
            var names = new List<string>();
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (var cmd = new SqlCommand(DatabasesSql, conn) { CommandTimeout = 30 })
                using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                        names.Add(reader.GetString(0));
            }
            return names;
        }

        /// <summary>The login's default database, for a query window whose connection names none.</summary>
        public static async Task<string> CurrentDatabaseAsync(string connectionString, CancellationToken token)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (var cmd = new SqlCommand("SELECT DB_NAME();", conn) { CommandTimeout = 10 })
                    return (string)await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
            }
        }

        public static async Task<List<SearchItem>> LoadAsync(string connectionString, string database, CancellationToken token)
        {
            var items = new List<SearchItem>();
            var objects = new List<ObjectRow>();
            var byId = new Dictionary<int, ObjectRow>();

            using (var conn = new SqlConnection(ForDatabase(connectionString, database)))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                // Module source of a big database can be large; allow longer than the schema load.
                using (var cmd = new SqlCommand(IndexSql, conn) { CommandTimeout = 120 })
                using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                        items.Add(new SearchItem(database, null, reader.GetString(0), SearchObjectType.Schema));

                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        var row = new ObjectRow
                        {
                            Schema = reader.GetString(1),
                            Name = reader.GetString(2),
                            Type = MapType(reader.GetString(3).Trim()),
                            Parent = reader.IsDBNull(4) ? null : reader.GetString(4),
                            // NULL for encrypted modules and CLR objects: still found by name.
                            Definition = reader.IsDBNull(5) ? null : reader.GetString(5),
                            ParentType = reader.IsDBNull(6) ? SearchObjectType.None : MapType(reader.GetString(6).Trim()),
                        };
                        objects.Add(row);
                        byId[reader.GetInt32(0)] = row;
                    }

                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (!byId.TryGetValue(reader.GetInt32(0), out var owner)) continue;
                        string type = SqlTypeFormatter.Format(reader.GetString(2), reader.GetInt16(3), reader.GetByte(4), reader.GetByte(5));
                        owner.Columns.Add(new TableColumn(reader.GetString(1), type, reader.GetBoolean(6)));
                    }

                    await reader.NextResultAsync(token).ConfigureAwait(false);
                    ObjectRow keyOwner = null;
                    string keyName = null;
                    bool clustered = false;
                    var keyColumns = new List<string>();
                    void FlushKey()
                    {
                        if (keyOwner != null) keyOwner.Key = new PrimaryKey(keyName, clustered, keyColumns.ToArray());
                    }
                    int lastId = -1;
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        int id = reader.GetInt32(0);
                        if (id != lastId)
                        {
                            FlushKey();
                            lastId = id;
                            byId.TryGetValue(id, out keyOwner);
                            keyName = reader.GetString(1);
                            clustered = reader.GetBoolean(2);
                            keyColumns.Clear();
                        }
                        keyColumns.Add(reader.GetString(3));
                    }
                    FlushKey();
                }
            }

            foreach (var o in objects)
            {
                string script = o.Type == SearchObjectType.Table ? TableScriptBuilder.Build(o.Schema, o.Name, o.Columns, o.Key) : null;
                items.Add(new SearchItem(database, o.Schema, o.Name, o.Type, o.Parent, o.Definition, script, o.ParentType));
                // A column previews its owner: the same string instance, so it costs no memory per column.
                string ownerPreview = o.Definition ?? script;
                foreach (var c in o.Columns)
                    items.Add(new SearchItem(database, o.Schema, c.Name, SearchObjectType.Column, o.Name, null, ownerPreview, o.Type));
            }
            return items;
        }

        private static SearchObjectType MapType(string type)
        {
            switch (type)
            {
                case "U": return SearchObjectType.Table;
                case "V": return SearchObjectType.View;
                case "P": case "PC": return SearchObjectType.Procedure;
                case "TR": return SearchObjectType.Trigger;
                default: return SearchObjectType.Function;
            }
        }
    }
}
