using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class SqlObjectNameTests
    {
        [Theory]
        [InlineData("Orders", "Orders")]
        [InlineData("dbo.Orders", "dbo.Orders")]
        [InlineData("[dbo].[Orders]", "dbo.Orders")]
        [InlineData("  dbo . Orders  ", "dbo.Orders")]
        [InlineData("Sales.[Order Line]", "Sales.[Order Line]")]
        [InlineData("[Order Line]", "[Order Line]")]
        [InlineData("mydb.dbo.Orders", "mydb.dbo.Orders")]
        [InlineData("[My.Db].dbo.Orders", "[My.Db].dbo.Orders")]
        [InlineData("[a]]b]", "[a]]b]")]
        [InlineData("\"My Table\"", "[My Table]")]
        [InlineData("\"a\"\"b\"", "[a\"b]")]
        [InlineData("dbo.Order", "dbo.[Order]")]
        [InlineData("1abc", "[1abc]")]
        [InlineData("#tmp", "#tmp")]
        [InlineData("##global", "##global")]
        [InlineData("@tv", "@tv")]
        // Plain Thai letters are a regular identifier; a name with vowel/tone marks is bracketed (see SqlIdentifier).
        [InlineData("[ตาราง].[ลูกค้า]", "ตาราง.[ลูกค้า]")]
        public void Parses(string text, string expected)
        {
            Assert.True(SqlObjectName.TryParse(text, out string formatted, out string error), error);
            Assert.Equal(expected, formatted);
            Assert.Null(error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("dbo.")]
        [InlineData(".Orders")]
        [InlineData("a..b")]
        [InlineData("[]")]
        [InlineData("[ ]")]
        [InlineData("a.b.c.d")]
        [InlineData("Order Line")]
        [InlineData("Orders;DROP TABLE x")]
        [InlineData("Orders--x")]
        [InlineData("Order-Line")]
        [InlineData("[abc")]
        [InlineData("abc]")]
        [InlineData("\"abc")]
        [InlineData("[a]b")]
        [InlineData("dbo.[Orders] x")]
        [InlineData("dbo.Orders\r\nx")]
        [InlineData("[a\nb]")]
        public void Rejects(string text)
        {
            Assert.False(SqlObjectName.TryParse(text, out string formatted, out string error));
            Assert.Null(formatted);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Fact]
        public void Rejects_NameLongerThan128Characters()
        {
            Assert.True(SqlObjectName.TryParse(new string('a', 128), out _, out _));
            Assert.False(SqlObjectName.TryParse(new string('a', 129), out _, out string error));
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.False(SqlObjectName.TryParse("dbo.[" + new string('a', 129) + "]", out _, out _));
        }

        [Fact]
        public void TempAndVariablePrefixesOnlyBelongToSinglePartNames()
        {
            // dbo.#tmp / dbo.@t are not objects SQL Server can resolve.
            Assert.False(SqlObjectName.TryParse("dbo.#tmp", out _, out _));
            Assert.False(SqlObjectName.TryParse("dbo.@tv", out _, out _));
        }
    }
}
