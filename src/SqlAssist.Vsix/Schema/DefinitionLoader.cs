using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace SqlAssist.Schema
{
    /// <summary>Reads one module's current definition (sys.sql_modules), for "ALTER PROCEDURE p" + Tab.</summary>
    internal static class DefinitionLoader
    {
        /// <summary>Null when the object does not exist, is not a module, or is WITH ENCRYPTION.</summary>
        public static async Task<string> LoadAsync(string connectionString, string schema, string name, CancellationToken token)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (var cmd = new SqlCommand("SELECT m.definition FROM sys.sql_modules m WHERE m.object_id = OBJECT_ID(@name);", conn) { CommandTimeout = 15 })
                {
                    cmd.Parameters.AddWithValue("@name", (schema != null ? Quote(schema) + "." : "") + Quote(name));
                    var value = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    return value as string;
                }
            }
        }

        private static string Quote(string part) => "[" + part.Replace("]", "]]") + "]";
    }
}
