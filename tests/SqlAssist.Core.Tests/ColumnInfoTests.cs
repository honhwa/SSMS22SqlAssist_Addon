using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class ColumnInfoTests
    {
        private static readonly DbObject SalesPerson = new DbObject("Sales", "SalesPerson", false,
            new[] { new DbColumn("BusinessEntityID", "int", false, false, true) });
        private static readonly DbObject Header = new DbObject("Sales", "SalesOrderHeader", false, new[]
        {
            new DbColumn("SalesOrderID", "int", false, true, true),
            new DbColumn("SalesPersonID", "int", description: "Sales person who created the sales order."),
            new DbColumn("Comment", "nvarchar(100)"),
        }, description: "General sales order information.");
        private static readonly SchemaModel Schema = new SchemaModel(new[] { SalesPerson, Header },
            new[] { new ForeignKey("FK_Header_Person", Header, SalesPerson, new[] { ("SalesPersonID", "BusinessEntityID") }) });

        [Fact]
        public void TableColumn_TypeFlagsDescriptionAndForeignKey() =>
            Assert.Equal(
                "Sales.SalesOrderHeader.SalesPersonID (Column)\n" +
                "SalesPersonID  INT\n" +
                "Sales person who created the sales order.\n" +
                "Foreign key to Sales.SalesPerson.BusinessEntityID",
                ColumnInfo.Describe(Schema, Header, Header.Columns[1]));

        [Fact]
        public void PrimaryKeyIdentity() =>
            Assert.Equal("Sales.SalesOrderHeader.SalesOrderID (Column)\nSalesOrderID  INT  PRIMARY KEY  IDENTITY",
                ColumnInfo.Describe(Schema, Header, Header.Columns[0]));

        [Fact]
        public void CteColumn_BorrowsDescriptionAndKeyFromSource()
        {
            const string sql = "WITH Sales_CTE AS (SELECT SalesPersonID FROM Sales.SalesOrderHeader)\nSELECT  FROM Sales_CTE";
            var ctx = SqlContextAnalyzer.Analyze(sql, sql.IndexOf("  FROM") + 1);
            var cte = Schema.Find(ctx.TablesInScope[0]);
            Assert.Equal(
                "Sales_CTE.SalesPersonID (Column)\n" +
                "SalesPersonID  INT\n" +
                "Sales person who created the sales order.\n" +
                "Foreign key to Sales.SalesPerson.BusinessEntityID",
                ColumnInfo.Describe(Schema, cte, cte.Columns[0]));

            var details = TableDetails.Build(Schema, cte);
            Assert.Null(details.Schema);
            Assert.Equal("CTE", details.Kind);
            Assert.Equal(ColumnKeyKind.ForeignKey, details.Columns[0].Key);
        }

        [Fact]
        public void TableDescriptionInDetails() =>
            Assert.Equal("General sales order information.", TableDetails.Build(Schema, Header).Description);
    }
}
