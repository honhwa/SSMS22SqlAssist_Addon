using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class HoverTargetTests
    {
        // "|" marks the mouse position; returns (table name, hovered text) or null.
        private static (string table, string span)? Hover(string sql)
        {
            int pos = sql.IndexOf('|');
            string text = sql.Remove(pos, 1);
            var h = SqlContextAnalyzer.FindHoverTarget(text, pos);
            return h == null ? ((string, string)?)null : (h.Table.Name, text.Substring(h.Start, h.End - h.Start));
        }

        private const string Query = "SELECT wr.ReqId FROM Work_Request wr\nJOIN Work_InitialApproval wia ON wr.ReqId = wia.ReqId";

        [Fact]
        public void TableNameInJoin() =>
            Assert.Equal(("Work_InitialApproval", "Work_InitialApproval"), Hover(Query.Replace("Work_Initial", "Work_Ini|tial")));

        [Fact]
        public void TableNameWithSchema_SpanCoversWholeName() =>
            Assert.Equal(("Customers", "dbo.Customers"), Hover("SELECT * FROM dbo.Cust|omers c"));

        [Fact]
        public void AliasWhereDeclared() =>
            Assert.Equal(("Work_InitialApproval", "wia"), Hover(Query.Replace("Approval wia", "Approval w|ia")));

        [Fact]
        public void AliasUsedElsewhere() =>
            Assert.Equal(("Work_Request", "wr"), Hover("SELECT w|r.ReqId FROM Work_Request wr"));

        [Fact]
        public void UnaliasedTableNameUsedAsQualifier() =>
            Assert.Equal(("Orders", "Orders"), Hover("SELECT Ord|ers.OrderID FROM Orders"));

        [Theory]
        [InlineData("SELECT wr.Req|Id FROM Work_Request wr")]
        [InlineData("SEL|ECT 1 FROM Work_Request wr")]
        [InlineData("SELECT 'Work_Req|uest' FROM Work_Request wr")]
        public void NothingToShow(string sql) => Assert.Null(Hover(sql));

        [Fact]
        public void DerivedTableAlias_ShowsItsColumns()
        {
            const string sql = "SELECT * FROM (SELECT 1 a) dt";
            var h = SqlContextAnalyzer.FindHoverTarget(sql, sql.Length - 1);
            Assert.NotNull(h.Table.Derived);
            Assert.Equal("dt", sql.Substring(h.Start, h.End - h.Start));
        }
    }
}
