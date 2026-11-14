using SqlAssist.Core.Ai;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class LiteralMaskerTests
    {
        [Theory]
        [InlineData("SELECT * FROM t WHERE Name = N'สมชาย' AND Age > 30", "SELECT * FROM t WHERE Name = '?' AND Age > 0")]
        [InlineData("WHERE Id IN (1, 22, 333)", "WHERE Id IN (0, 0, 0)")]
        [InlineData("SELECT [Order 1], \"Col 2\" FROM [dbo].[T1]", "SELECT [Order 1], \"Col 2\" FROM [dbo].[T1]")]
        [InlineData("SELECT 'it''s' -- keep 'this' 42", "SELECT '?' -- keep 'this' 42")]
        [InlineData("SELECT 0x1F, 1e5, 1.50", "SELECT 0, 0, 0")]
        [InlineData("SELECT 'unterminated", "SELECT '?'")]
        [InlineData("SELECT @p1, Col2 FROM T3", "SELECT @p1, Col2 FROM T3")]
        [InlineData("", "")]
        public void Masks_literals_only(string sql, string expected) => Assert.Equal(expected, LiteralMasker.Mask(sql));

        [Fact]
        public void Keeps_line_count_when_a_string_spans_lines()
        {
            string sql = "SELECT 'a\r\nb\r\nc'\r\nFROM t";
            string masked = LiteralMasker.Mask(sql);
            Assert.Equal(sql.Split('\n').Length, masked.Split('\n').Length);
            Assert.DoesNotContain("a", masked.Substring(0, masked.IndexOf("FROM")));
            Assert.EndsWith("FROM t", masked);
        }

        [Fact]
        public void Null_is_empty() => Assert.Equal("", LiteralMasker.Mask(null));
    }
}
