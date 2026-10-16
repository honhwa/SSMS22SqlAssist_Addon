using SqlAssist.Core.Snippets;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class SnippetTests
    {
        private static readonly SnippetStore Defaults = SnippetStore.Parse(SnippetStore.DefaultXml);

        [Fact]
        public void Ssf_ExpandsWithCaretAtEnd()
        {
            Assert.True(Defaults.TryGet("ssf", out var s));
            var (text, caret) = SnippetStore.Render(s, "", "\r\n");
            Assert.Equal("SELECT * FROM ", text);
            Assert.Equal(text.Length, caret);
        }

        [Fact]
        public void KeysAreCaseInsensitive() => Assert.True(Defaults.TryGet("SSF", out _));

        [Fact]
        public void MultiLine_IndentedAndCaretPlaced()
        {
            Assert.True(Defaults.TryGet("cte", out var s));
            var (text, caret) = SnippetStore.Render(s, "  ", "\n");
            Assert.Equal("WITH cte AS (\n      \n  )\n  SELECT * FROM cte;", text);
            Assert.Equal("WITH cte AS (\n      ".Length, caret);
        }

        [Fact]
        public void UserFileParses()
        {
            var store = SnippetStore.Parse("<snippets><snippet key='x1'>SELECT 1</snippet><snippet key=''>ignored</snippet></snippets>");
            var s = Assert.Single(store.All);
            var (text, caret) = SnippetStore.Render(s, "", "\n");
            Assert.Equal("SELECT 1", text);
            Assert.Equal(8, caret);
        }

        [Theory]
        [InlineData("ssf|", "ssf")]
        [InlineData("SELECT 1\n  ssf|", "ssf")]
        public void SnippetWord(string sql, string expected)
        {
            int caret = sql.IndexOf('|');
            Assert.True(SqlContextAnalyzer.TryGetSnippetWord(sql.Remove(caret, 1), caret, out var word, out _));
            Assert.Equal(expected, word);
        }

        [Theory]
        [InlineData("c.ssf|")]
        [InlineData("'ssf|'")]
        [InlineData("ss|f")]
        [InlineData("ssf |")]
        public void NotASnippetWord(string sql)
        {
            int caret = sql.IndexOf('|');
            Assert.False(SqlContextAnalyzer.TryGetSnippetWord(sql.Remove(caret, 1), caret, out _, out _));
        }
    }
}
