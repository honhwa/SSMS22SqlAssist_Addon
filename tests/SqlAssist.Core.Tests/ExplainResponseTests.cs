using SqlAssist.Core.Ai;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class ExplainResponseTests
    {
        private const string Ok = "{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"x\"},{\"type\":\"text\",\"text\":" +
            "\"{\\\"summary\\\":\\\"สรุป\\\",\\\"findings\\\":[{\\\"line\\\":2,\\\"severity\\\":\\\"warning\\\",\\\"title\\\":\\\"t\\\",\\\"detail\\\":\\\"d\\\"}," +
            "{\\\"line\\\":99,\\\"severity\\\":\\\"odd\\\",\\\"title\\\":\\\"u\\\",\\\"detail\\\":\\\"e\\\"}]}\"}],\"usage\":{\"input_tokens\":10}}";

        [Fact]
        public void Reads_summary_and_findings_and_clamps_lines()
        {
            var r = ExplainResponse.Parse(200, Ok, sentLineCount: 3);
            Assert.Null(r.Problem);
            Assert.Equal("สรุป", r.Summary);
            Assert.Equal(2, r.Findings.Count);
            Assert.Equal(2, r.Findings[0].Line);
            Assert.Equal(FindingSeverity.Warning, r.Findings[0].Severity);
            Assert.Equal("t", r.Findings[0].Title);
            Assert.Equal(0, r.Findings[1].Line);
            Assert.Equal(FindingSeverity.Info, r.Findings[1].Severity);
        }

        [Fact]
        public void No_findings_is_fine()
        {
            var r = ExplainResponse.Parse(200, "{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"{\\\"summary\\\":\\\"s\\\",\\\"findings\\\":[]}\"}]}", 5);
            Assert.Null(r.Problem);
            Assert.Empty(r.Findings);
        }

        [Theory]
        [InlineData(0, null, "ติดต่อ")]
        [InlineData(401, "{\"type\":\"error\",\"error\":{\"message\":\"invalid x-api-key\"}}", "API key")]
        [InlineData(403, "", "403")]
        [InlineData(429, "{}", "429")]
        [InlineData(500, "<html>", "500")]
        [InlineData(400, "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"bad model\"}}", "bad model")]
        [InlineData(200, "{\"stop_reason\":\"refusal\",\"content\":[]}", "ปฏิเสธ")]
        [InlineData(200, "{\"stop_reason\":\"max_tokens\",\"content\":[{\"type\":\"text\",\"text\":\"{\"}]}", "ยาวเกิน")]
        [InlineData(200, "{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"not json\"}]}", "อ่านคำตอบ")]
        [InlineData(200, "{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"{\\\"summary\\\":\\\"s\\\",\\\"findings\\\":[{\\\"line\\\":\\\"x\\\"}]}\"}]}", "อ่านคำตอบ")]
        [InlineData(200, "{\"stop_reason\":\"end_turn\",\"content\":[]}", "อ่านคำตอบ")]
        [InlineData(200, "", "อ่านคำตอบ")]
        [InlineData(200, null, "อ่านคำตอบ")]
        public void Failures_become_a_problem_text(int status, string body, string expectedPart)
        {
            var r = ExplainResponse.Parse(status, body, 10);
            Assert.Contains(expectedPart, r.Problem);
            Assert.Empty(r.Findings);
        }
    }
}
