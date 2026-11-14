using System;
using System.Text.Json;
using SqlAssist.Core.Ai;
using SqlAssist.Core.Settings;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class ExplainRequestTests
    {
        [Fact]
        public void Body_carries_masked_sql_schema_and_no_identity_of_the_server()
        {
            var r = ExplainRequest.Build("SELECT * FROM dbo.Orders WHERE Note = 'ลับ' AND Qty > 5", TestSchema.Build(), new AiOptions());
            Assert.Equal("SELECT * FROM dbo.Orders WHERE Note = '?' AND Qty > 0", r.SentSql);
            Assert.DoesNotContain("ลับ", r.Body);
            Assert.Contains("\"model\":\"claude-opus-5-5\"", r.Body);
            Assert.Contains("\"max_tokens\":16000", r.Body);
            Assert.Contains("\"fallbacks\":\"default\"", r.Body);
            Assert.Contains("\"effort\":\"medium\"", r.Body);
            Assert.Contains("dbo.Orders (table):", r.Body);
            Assert.DoesNotContain("temperature", r.Body);
            Assert.DoesNotContain("\"thinking\"", r.Body);
            Assert.Equal(1, r.TableCount);
            Assert.Equal(1, r.SentLineCount);
        }

        [Fact]
        public void Masking_off_sends_the_sql_as_typed()
        {
            var r = ExplainRequest.Build("SELECT 'x'", null, new AiOptions { MaskLiterals = false });
            Assert.Equal("SELECT 'x'", r.SentSql);
            Assert.Contains("(none available)", r.Body);
            Assert.Equal(0, r.TableCount);
        }

        [Fact]
        public void Body_is_valid_json_with_quotes_newlines_thai_and_numbered_lines()
        {
            var r = ExplainRequest.Build("SELECT \"a\\b\"\r\nFROM [ตาราง] -- คอมเมนต์\u0001", null, new AiOptions());
            using (var doc = JsonDocument.Parse(r.Body))
            {
                var root = doc.RootElement;
                string content = root.GetProperty("messages")[0].GetProperty("content").GetString();
                Assert.Equal("SQL:\n1| SELECT \"a\\b\"\n2| FROM [ตาราง] -- คอมเมนต์\u0001\n\nSchema:\n(none available)", content);
                Assert.Equal(16000, root.GetProperty("max_tokens").GetInt32());
                Assert.Equal("json_schema", root.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
                Assert.Equal(ExplainRequest.SystemPrompt, root.GetProperty("system").GetString());
            }
            Assert.Equal(2, r.SentLineCount);
        }

        [Fact]
        public void Line_numbers_are_padded_to_the_same_width()
        {
            var r = ExplainRequest.Build(string.Join("\n", new string[10]).Replace("\0", ""), null, new AiOptions());
            using (var doc = JsonDocument.Parse(r.Body))
            {
                string content = doc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
                Assert.StartsWith("SQL:\n 1| \n 2| ", content);
                Assert.Contains("\n10| \n", content);
            }
        }

        [Fact]
        public void Too_long_is_rejected_not_truncated() =>
            Assert.Throws<ArgumentException>(() => ExplainRequest.Build(new string('x', ExplainRequest.MaxSqlChars + 1), null, new AiOptions()));
    }
}
