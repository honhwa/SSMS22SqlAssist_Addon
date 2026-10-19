using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class SelectBuilderTests
    {
        private static readonly SchemaModel Schema = TestSchema.Build();

        [Fact]
        public void WholeTable_AllColumnsAndFrom() =>
            Assert.Equal(
                "c.CustomerID\n" +
                "      ,c.Name\n" +
                "      ,c.Email\n" +
                "FROM Customers c",
                SelectBuilder.Build(Schema, new[] { new PickedTable(TestSchema.Customers) }, "SELECT ", "\n"));

        [Fact]
        public void PickedColumnsFromTwoTables_JoinedOnForeignKey() =>
            Assert.Equal(
                "c.Name\n" +
                "      ,o.OrderDate\n" +
                "FROM Customers c\n" +
                "JOIN Orders o ON o.CustomerID = c.CustomerID",
                SelectBuilder.Build(Schema, new[]
                {
                    new PickedTable(TestSchema.Customers, new[] { "Name" }),
                    new PickedTable(TestSchema.Orders, new[] { "OrderDate" }),
                }, "SELECT ", "\n"));

        [Fact]
        public void ForeignKeyTheOtherWay_AndIndentedLine() =>
            Assert.Equal(
                "od.Qty\n" +
                "\t      ,o.OrderDate\n" +
                "\tFROM OrderDetails od\n" +
                "\tJOIN Orders o ON o.OrderID = od.OrderID",
                SelectBuilder.Build(Schema, new[]
                {
                    new PickedTable(TestSchema.OrderDetails, new[] { "Qty" }),
                    new PickedTable(TestSchema.Orders, new[] { "OrderDate" }),
                }, "\tSELECT ", "\n"));

        [Fact]
        public void NoKey_SharedIdColumn_ThenCrossJoin()
        {
            var a = new DbObject("dbo", "Work_Request", false, new[] { new DbColumn("ReqId", "int"), new DbColumn("Code", "int") });
            var b = new DbObject("dbo", "Work_InitialApproval", false, new[] { new DbColumn("Id", "int"), new DbColumn("ReqId", "int") });
            var c = new DbObject("dbo", "Lookup", false, new[] { new DbColumn("Val", "int") });
            var schema = new SchemaModel(new[] { a, b, c }, new ForeignKey[0]);
            Assert.Equal(
                "wr.Code\n" +
                "      ,wia.Id\n" +
                "      ,l.Val\n" +
                "FROM Work_Request wr\n" +
                "JOIN Work_InitialApproval wia ON wia.ReqId = wr.ReqId\n" +
                "CROSS JOIN Lookup l",
                SelectBuilder.Build(schema, new[]
                {
                    new PickedTable(a, new[] { "Code" }),
                    new PickedTable(b, new[] { "Id" }),
                    new PickedTable(c, new[] { "Val" }),
                }, "SELECT ", "\n"));
        }

        [Theory]
        [InlineData("SELECT |", true, true)]
        [InlineData("SELECT MAS_|", true, false)]
        [InlineData("SELECT a, |", true, false)]
        [InlineData("WITH c AS (SELECT 1 x)\nSELECT |", true, true)]
        [InlineData("SELECT | FROM Customers c", false, false)]
        [InlineData("SELECT * FROM Customers c WHERE |", false, false)]
        [InlineData("SELECT * FROM Customers c WHERE x IN (SELECT |)", true, true)]
        public void SelectListWithoutFrom(string sql, bool withoutFrom, bool rightAfterSelect)
        {
            int caret = sql.IndexOf('|');
            var ctx = Sql.SqlContextAnalyzer.Analyze(sql.Remove(caret, 1), caret);
            Assert.Equal((withoutFrom, rightAfterSelect), (ctx.IsSelectListWithoutFrom, ctx.IsRightAfterSelect));
        }

        [Fact]
        public void AliasesAvoidTakenOnesAndSchemaIsKept() =>
            Assert.Equal("p2.Name\nFROM sales.Products p2",
                SelectBuilder.Build(Schema, new[] { new PickedTable(TestSchema.Products, new[] { "Name" }) }, "", "\n", new[] { "p" }));
    }
}
