using System.Linq;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class JoinConditionTests
    {
        private static CaretContext At(string sql)
        {
            int caret = sql.IndexOf('|');
            return SqlContextAnalyzer.Analyze(sql.Remove(caret, 1), caret);
        }

        private static string[] Suggest(string sql, SchemaModel schema = null)
        {
            var ctx = At(sql);
            Assert.Equal(CaretContextKind.JoinCondition, ctx.Kind);
            return JoinConditionSuggester.Suggest(schema ?? TestSchema.Build(), ctx.JoinTarget, ctx.JoinPartners, ctx.JoinConditionSoFar)
                .Select(c => c.Text).ToArray();
        }

        [Fact]
        public void AfterOn_KnowsJoinedTableAndPartners()
        {
            var ctx = At("SELECT * FROM Customers c JOIN Orders o ON |");
            Assert.Equal(CaretContextKind.JoinCondition, ctx.Kind);
            Assert.Equal("o", ctx.JoinTarget.Alias);
            Assert.Equal(new[] { "c" }, ctx.JoinPartners.Select(t => t.Alias));
        }

        [Fact]
        public void ForeignKeyFirst_EarlierTableOnTheLeft()
        {
            var s = Suggest("SELECT * FROM Customers c JOIN Orders o ON |");
            Assert.Equal("c.CustomerID = o.CustomerID", s[0]);
        }

        [Fact]
        public void CompositeForeignKeyIsOnePredicate() =>
            Assert.Equal("od.OrderID = s.OrderID AND od.[LineNo] = s.[LineNo]",
                Suggest("SELECT * FROM OrderDetails od JOIN Shipments s ON |")[0]);

        [Fact]
        public void SameNameColumns_IdLikeFirst_InTableOrder()
        {
            // Two tables with no foreign key between them, like Work_Request / Work_InitialApproval.
            var wr = new DbObject("dbo", "Work_Request", false, new[] { "Code", "ReqId", "Step", "CreateBy", "Only_wr" }.Select(n => new DbColumn(n, "int")).ToArray());
            var wia = new DbObject("dbo", "Work_InitialApproval", false, new[] { "Id", "ReqId", "Code", "Step", "CreateBy" }.Select(n => new DbColumn(n, "int")).ToArray());
            var schema = new SchemaModel(new[] { wr, wia }, new ForeignKey[0]);

            Assert.Equal(new[]
            {
                "wr.ReqId = wia.ReqId",
                "wr.Code = wia.Code",
                "wr.Step = wia.Step",
                "wr.CreateBy = wia.CreateBy",
            }, Suggest("SELECT wr.ReqId FROM Work_Request wr\nJOIN Work_InitialApproval wia ON |", schema));
        }

        [Fact]
        public void LatestPartnerFirst() =>
            Assert.StartsWith("o.OrderID = od.OrderID", Suggest("SELECT * FROM Customers c JOIN Orders o ON 1=1 JOIN OrderDetails od ON |")[0]);

        [Fact]
        public void AfterAnd_SkipsPredicatesAlreadyWritten()
        {
            var s = Suggest("SELECT * FROM Customers c JOIN Orders o ON c.CustomerID = o.CustomerID AND |");
            Assert.DoesNotContain("c.CustomerID = o.CustomerID", s);
        }

        [Theory]
        [InlineData("SELECT * FROM Customers c WHERE c.Name = 'x' AND |")]
        [InlineData("SELECT * FROM Customers c JOIN Orders o ON c.CustomerID = o.|")]
        [InlineData("SELECT * FROM Customers c JOIN Orders o ON c.CustomerID = |")]
        public void NotAJoinConditionPosition(string sql) => Assert.NotEqual(CaretContextKind.JoinCondition, At(sql).Kind);
    }
}
