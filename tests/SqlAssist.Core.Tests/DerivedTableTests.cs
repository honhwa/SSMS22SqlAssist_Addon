using System.Linq;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class DerivedTableTests
    {
        private static readonly DbObject OrderHeader = new DbObject("Sales", "SalesOrderHeader", false, new[]
        {
            new DbColumn("SalesOrderID", "int", false, true, true),
            new DbColumn("SalesPersonID", "int", description: "Sales person who created the sales order."),
            new DbColumn("OrderDate", "datetime", false),
        });

        private static readonly SchemaModel Schema = new SchemaModel(new[] { OrderHeader }, new ForeignKey[0]);

        // The query from the dbForge screenshot, blank line and all.
        private const string Cte =
            "WITH Sales_CTE (SalesPersonID, SalesOrderID, SalesYear)\n" +
            "AS\n" +
            "(\n" +
            "    SELECT SalesPersonID, SalesOrderID, YEAR(OrderDate) AS SalesYear\n" +
            "    FROM Sales.SalesOrderHeader\n" +
            "    WHERE SalesPersonID IS NOT NULL\n" +
            ")\n" +
            "\n";

        private static CaretContext At(string sql)
        {
            int caret = sql.IndexOf('|');
            return SqlContextAnalyzer.Analyze(sql.Remove(caret, 1), caret);
        }

        [Fact]
        public void CteSurvivesBlankLineBeforeTheQuery()
        {
            var ctx = At(Cte + "SELECT | FROM Sales_CTE");
            var cte = Assert.Single(ctx.CommonTableExpressions);
            Assert.Equal("Sales_CTE", cte.Name);
            var r = Assert.Single(ctx.TablesInScope);
            Assert.Same(cte, r.Derived);
        }

        [Fact]
        public void CteColumns_ExplicitList_TypesFromSource()
        {
            var ctx = At(Cte + "SELECT | FROM Sales_CTE s");
            var o = Schema.Find(ctx.TablesInScope[0]);
            Assert.Equal("CTE", o.Kind);
            Assert.Equal(new[] { ("SalesPersonID", "int"), ("SalesOrderID", "int"), ("SalesYear", "") },
                o.Columns.Select(c => (c.Name, c.TypeName)));
            Assert.Equal("Sales person who created the sales order.", o.Columns[0].Description);
            Assert.Same(OrderHeader, o.Columns[0].Origin);
        }

        [Fact]
        public void CteColumns_FromSelectList_AliasForms()
        {
            const string sql = "WITH c AS (SELECT h.SalesOrderID, h.OrderDate d1, Total = 1, 2 AS Two, COUNT(*) FROM Sales.SalesOrderHeader h)\nSELECT | FROM c";
            var o = Schema.Find(At(sql).TablesInScope[0]);
            Assert.Equal(new[] { "SalesOrderID", "d1", "Total", "Two" }, o.Columns.Select(c => c.Name));
            Assert.Equal("datetime", o.Columns[1].TypeName);
        }

        [Fact]
        public void CteStar_ExpandsSourceColumns() =>
            Assert.Equal(3, Schema.Find(At("WITH c AS (SELECT * FROM Sales.SalesOrderHeader) SELECT | FROM c").TablesInScope[0]).Columns.Count);

        [Fact]
        public void DerivedTableInFrom()
        {
            var ctx = At("SELECT d.| FROM (SELECT TOP 10 SalesOrderID, OrderDate FROM Sales.SalesOrderHeader) d");
            var r = Assert.Single(ctx.TablesInScope);
            var o = Schema.Find(r);
            Assert.Equal("Derived table", o.Kind);
            Assert.Equal(new[] { "SalesOrderID", "OrderDate" }, o.Columns.Select(c => c.Name));
        }

        [Fact]
        public void StarExpansionOverCte()
        {
            string sql = Cte + "SELECT *| FROM Sales_CTE";
            int caret = sql.IndexOf('|');
            var star = SqlContextAnalyzer.FindStar(sql.Remove(caret, 1), caret);
            Assert.Equal("SalesPersonID, SalesOrderID, SalesYear", StarExpander.Expand(star, Schema));
        }

        [Fact]
        public void RecursiveCteDoesNotLoop()
        {
            const string sql = "WITH r AS (SELECT SalesOrderID FROM Sales.SalesOrderHeader UNION ALL SELECT SalesOrderID FROM r)\nSELECT | FROM r";
            var o = Schema.Find(At(sql).TablesInScope[0]);
            Assert.Equal(new[] { "SalesOrderID" }, o.Columns.Select(c => c.Name));
        }

        [Fact]
        public void TableHintIsNotACte() =>
            Assert.Empty(At("SELECT * FROM T WITH (NOLOCK) WHERE |").CommonTableExpressions);

        [Fact]
        public void BlankLineInsideParensDoesNotSplit() =>
            Assert.Single(At("SELECT * FROM T t WHERE x IN (\n\nSELECT | FROM U u)").TablesInScope, t => t.Alias == "u");

        [Fact]
        public void BlankLineStillSeparatesOrdinaryStatements() =>
            Assert.Equal(new[] { "o" }, At("SELECT * FROM Customers c\n\nSELECT | FROM Orders o").TablesInScope.Select(t => t.Alias));
    }
}
