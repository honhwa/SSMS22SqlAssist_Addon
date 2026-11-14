using System;
using System.Globalization;
using System.Text;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Settings;

namespace SqlAssist.Core.Ai
{
    /// <summary>The body of one Explain SQL call to the Claude Messages API. It is built once and the same string is both shown to
    /// the user (before the first send, and on request) and sent, so what the user sees is exactly what leaves the machine.
    /// It carries the SQL (literals masked unless turned off) and the digest of the tables it names; never the server, database,
    /// login or any data.</summary>
    public sealed class ExplainRequest
    {
        public const int MaxSqlChars = 20000;
        public const int MaxTokens = 16000;
        public const string Effort = "medium";

        public const string SystemPrompt =
            "You review T-SQL for a developer working in SQL Server Management Studio. Answer in Thai; keep SQL keywords, object names and technical terms in English.\n" +
            "\"summary\": what the script does, step by step, in plain language (a short paragraph, or a few lines for a long script).\n" +
            "\"findings\": things that are likely wrong or likely slow, judged from the text alone. You cannot see data, statistics or execution plans, " +
            "so describe a performance point as something to check, not as a measured fact. Report nothing when there is nothing worth the developer's time; " +
            "do not pad the list with style remarks.\n" +
            "Each line of the SQL you are given starts with its line number and \"| \"; that prefix is not part of the SQL. " +
            "\"line\" is the number of the line where the finding starts, or 0 when it is about the whole script.\n" +
            "Literal values may have been replaced with '?' and 0 before the SQL was sent; do not report those placeholders as problems.\n" +
            "The schema lists only the tables the script names; a table missing from it is not an error.";

        private const string ResponseSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"summary\",\"findings\"],\"properties\":{" +
            "\"summary\":{\"type\":\"string\"}," +
            "\"findings\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"line\",\"severity\",\"title\",\"detail\"],\"properties\":{" +
            "\"line\":{\"type\":\"integer\"}," +
            "\"severity\":{\"type\":\"string\",\"enum\":[\"info\",\"warning\"]}," +
            "\"title\":{\"type\":\"string\"}," +
            "\"detail\":{\"type\":\"string\"}}}}}}";

        private ExplainRequest(string body, string userContent, string sentSql, int sentLineCount, int tableCount, bool schemaTruncated)
        {
            Body = body; UserContent = userContent; SentSql = sentSql; SentLineCount = sentLineCount; TableCount = tableCount; SchemaTruncated = schemaTruncated;
        }

        /// <summary>The JSON request body, exactly as sent.</summary>
        public string Body { get; }
        /// <summary>The user message inside <see cref="Body"/> (numbered SQL and the schema digest) as plain text, for people to read.</summary>
        public string UserContent { get; }
        /// <summary>The SQL as sent (masked or not), before the line numbers were added.</summary>
        public string SentSql { get; }
        public int SentLineCount { get; }
        public int TableCount { get; }
        public bool SchemaTruncated { get; }

        /// <summary>Throws <see cref="ArgumentException"/> when the SQL is longer than <see cref="MaxSqlChars"/>: the caller tells the
        /// user to select less. It is never cut short silently.</summary>
        public static ExplainRequest Build(string sql, SchemaModel schema, AiOptions options)
        {
            sql = sql ?? "";
            if (sql.Length > MaxSqlChars) throw new ArgumentException("The SQL is longer than " + MaxSqlChars + " characters.", nameof(sql));
            options = options ?? new AiOptions();

            string sent = options.MaskLiterals ? LiteralMasker.Mask(sql) : sql;
            var digest = SchemaDigest.Build(sql, schema, options.SendDescriptions);
            string[] lines = sent.Split('\n');
            var content = new StringBuilder("SQL:\n");
            int width = lines.Length.ToString(CultureInfo.InvariantCulture).Length;
            for (int i = 0; i < lines.Length; i++)
                content.Append((i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width)).Append("| ").Append(lines[i].TrimEnd('\r')).Append('\n');
            content.Append("\nSchema:\n").Append(digest.Text.Length == 0 ? "(none available)" : digest.Text);

            var body = new StringBuilder();
            body.Append("{\"model\":").Append(JsonString(AiOptions.CleanModel(options.Model)))
                .Append(",\"max_tokens\":").Append(MaxTokens.ToString(CultureInfo.InvariantCulture))
                .Append(",\"fallbacks\":\"default\"")
                .Append(",\"output_config\":{\"effort\":\"").Append(Effort).Append("\",\"format\":{\"type\":\"json_schema\",\"schema\":").Append(ResponseSchema).Append("}}")
                .Append(",\"system\":").Append(JsonString(SystemPrompt))
                .Append(",\"messages\":[{\"role\":\"user\",\"content\":").Append(JsonString(content.ToString())).Append("}]}");
            return new ExplainRequest(body.ToString(), content.ToString(), sent, lines.Length, digest.TableCount, digest.Truncated);
        }

        /// <summary>A JSON string literal. Non-ASCII text (Thai) stays as is; the body is sent as UTF-8.</summary>
        public static string JsonString(string value)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
