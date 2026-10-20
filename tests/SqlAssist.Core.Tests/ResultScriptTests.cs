using System;
using SqlAssist.Core.Results;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class ResultScriptTests
    {
        // The dbForge documentation's example: SELECT ProductID, ProductNumber, ... with ProductNumber cells selected.
        private static readonly ResultColumn[] Product =
        {
            new ResultColumn("ProductID", "int"),
            new ResultColumn("ProductNumber", "nvarchar(25)"),
            new ResultColumn("Color", "nvarchar(15)"),
        };

        private static readonly object[][] Rows =
        {
            new object[] { 680, "FR-R92B-58", "Black" },
            new object[] { 706, "FR-R92R-58", "Red" },
            new object[] { 707, "HL-U509-R", "Red" },
        };

        private static string Build(ResultScriptKind kind, params int[] selected) =>
            ResultScriptBuilder.Build(kind, Product, selected, Rows, "\n");

        [Fact]
        public void Insert()
        {
            Assert.Equal(
                "SET DATEFORMAT ymd\n\n" +
                "INSERT INTO [Table] (ProductNumber) VALUES\n" +
                "(N'FR-R92B-58'),\n" +
                "(N'FR-R92R-58'),\n" +
                "(N'HL-U509-R')\n" +
                "GO\n", Build(ResultScriptKind.Insert, 1));
        }

        [Fact]
        public void InsertTempTable()
        {
            Assert.Equal(
                "SET DATEFORMAT ymd\n\n" +
                "CREATE TABLE #tmpTable (\n" +
                "    ProductID int,\n" +
                "    ProductNumber nvarchar(25))\n\n" +
                "INSERT INTO #tmpTable (ProductID, ProductNumber) VALUES\n" +
                "(680, N'FR-R92B-58'),\n" +
                "(706, N'FR-R92R-58'),\n" +
                "(707, N'HL-U509-R')\n" +
                "GO\n\n" +
                "SELECT * FROM #tmpTable\n\n" +
                "DROP TABLE #tmpTable\n" +
                "GO\n", Build(ResultScriptKind.InsertTempTable, 0, 1));
        }

        [Fact]
        public void WhereIn_DistinctValues()
        {
            Assert.Equal("WHERE ProductNumber IN(N'FR-R92B-58', N'FR-R92R-58', N'HL-U509-R')\n", Build(ResultScriptKind.WhereIn, 1));
            Assert.Equal("WHERE Color IN(N'Black', N'Red')\n", Build(ResultScriptKind.WhereIn, 2));
        }

        [Fact]
        public void WhereIn_MultipleColumnsAndNull()
        {
            var rows = new[] { new object[] { 1, null, "x" }, new object[] { 2, "A", DBNull.Value } };
            Assert.Equal(
                "WHERE (ProductNumber IN(N'A') OR ProductNumber IS NULL)\n" +
                "  AND (Color IN(N'x') OR Color IS NULL)\n",
                ResultScriptBuilder.Build(ResultScriptKind.WhereIn, Product, new[] { 1, 2 }, rows, "\n"));
            Assert.Equal("WHERE Color IS NULL\n",
                ResultScriptBuilder.Build(ResultScriptKind.WhereIn, Product, new[] { 2 }, new[] { new object[] { 1, "a", null } }, "\n"));
        }

        [Fact]
        public void WhereAndOr()
        {
            Assert.Equal(
                "WHERE (ProductNumber = N'FR-R92B-58') OR\n" +
                "(ProductNumber = N'FR-R92R-58') OR\n" +
                "(ProductNumber = N'HL-U509-R')\n", Build(ResultScriptKind.WhereAndOr, 1));
            Assert.Equal(
                "WHERE (ProductID = 680 AND Color = N'Black') OR\n" +
                "(ProductID = 706 AND Color = N'Red') OR\n" +
                "(ProductID = 707 AND Color = N'Red')\n", Build(ResultScriptKind.WhereAndOr, 0, 2));
        }

        [Fact]
        public void Update_UsesFirstColumnAsKey()
        {
            Assert.Equal(new[] { 1, 0 }, ResultScriptBuilder.RequiredColumns(ResultScriptKind.Update, Product, new[] { 1 }));
            Assert.Equal(
                "SET DATEFORMAT ymd\n\n" +
                "UPDATE [Table] SET ProductNumber = N'FR-R92B-58' WHERE ProductID = 680\nGO\n" +
                "UPDATE [Table] SET ProductNumber = N'FR-R92R-58' WHERE ProductID = 706\nGO\n" +
                "UPDATE [Table] SET ProductNumber = N'HL-U509-R' WHERE ProductID = 707\nGO\n",
                Build(ResultScriptKind.Update, 1));
            // The key column itself is not SET when other columns are selected with it.
            Assert.StartsWith("SET DATEFORMAT ymd\n\nUPDATE [Table] SET Color = N'Black' WHERE ProductID = 680\n", Build(ResultScriptKind.Update, 0, 2));
        }

        [Fact]
        public void Update_ReportedKeysAndBaseTable()
        {
            var columns = new[]
            {
                new ResultColumn("Name", "varchar(10)", false, "Sales", "Order Line"),
                new ResultColumn("OrderId", "int", true, "Sales", "Order Line"),
                new ResultColumn("LineNo", "int", true, "Sales", "Order Line"),
            };
            var rows = new[] { new object[] { "it's", 5, null } };
            Assert.Equal(
                "SET DATEFORMAT ymd\n\n" +
                "UPDATE Sales.[Order Line] SET Name = 'it''s' WHERE OrderId = 5 AND [LineNo] IS NULL\nGO\n",
                ResultScriptBuilder.Build(ResultScriptKind.Update, columns, new[] { 0 }, rows, "\n"));
        }

        [Fact]
        public void Insert_SplitsAfterThousandRows()
        {
            var rows = new object[1001][];
            for (int i = 0; i < rows.Length; i++) rows[i] = new object[] { i, "n", "c" };
            string script = ResultScriptBuilder.Build(ResultScriptKind.Insert, Product, new[] { 0 }, rows, "\n");
            Assert.Equal(2, script.Split(new[] { "INSERT INTO" }, StringSplitOptions.None).Length - 1);
            Assert.Contains("(999)\nGO\n\nINSERT INTO [Table] (ProductID) VALUES\n(1000)\nGO\n", script);
        }

        [Fact]
        public void ColumnNames_UnnamedRepeatedAndOdd()
        {
            var columns = new[] { new ResultColumn("", "int"), new ResultColumn("Id", "int"), new ResultColumn("id", "int"), new ResultColumn("Order Date", "date") };
            var rows = new[] { new object[] { 1, 2, 3, new DateTime(2024, 1, 31) } };
            Assert.Equal("WHERE (Column1 = 1 AND Id = 2 AND id_2 = 3 AND [Order Date] = '2024-01-31')\n",
                ResultScriptBuilder.Build(ResultScriptKind.WhereAndOr, columns, new[] { 0, 1, 2, 3 }, rows, "\n"));
        }

        [Fact]
        public void TempTable_TypesItCannotCopy()
        {
            var columns = new[] { new ResultColumn("v", "timestamp"), new ResultColumn("x", null) };
            string script = ResultScriptBuilder.Build(ResultScriptKind.InsertTempTable, columns, new[] { 0, 1 },
                new[] { new object[] { new byte[] { 0, 0, 0, 0, 0, 0, 0x07, 0xD1 }, "a" } }, "\n");
            Assert.Contains("    v binary(8),\n    x sql_variant)", script);
            Assert.Contains("(0x00000000000007D1, N'a')", script);
        }

        [Theory]
        [InlineData(null, "int", "NULL")]
        [InlineData(true, "bit", "1")]
        [InlineData("False", "bit", "0")]
        [InlineData(12.5, "float", "12.5")]
        [InlineData("1234.5600", "decimal(10,4)", "1234.5600")]
        [InlineData("a'b", "nvarchar(10)", "N'a''b'")]
        [InlineData("a", "varchar(10)", "'a'")]
        [InlineData("0xFF", "varbinary(max)", "0xFF")]
        [InlineData("2024-01-31 10:00:00.000", "datetime", "'2024-01-31 10:00:00.000'")]
        public void Literals(object value, string type, string expected) => Assert.Equal(expected, SqlLiteral.Format(value, type));

        [Fact]
        public void Literals_TypedValues()
        {
            var dt = new DateTime(2024, 1, 31, 13, 5, 9, 120);
            Assert.Equal("'2024-01-31 13:05:09.120'", SqlLiteral.Format(dt, "datetime"));
            Assert.Equal("'2024-01-31'", SqlLiteral.Format(dt, "date"));
            Assert.Equal("'2024-01-31 13:05:09.1200000'", SqlLiteral.Format(dt, "datetime2(7)"));
            Assert.Equal("'2024-01-31 13:05:09.1200000 +07:00'", SqlLiteral.Format(new DateTimeOffset(dt, TimeSpan.FromHours(7)), "datetimeoffset"));
            Assert.Equal("'13:05:09'", SqlLiteral.Format(new TimeSpan(13, 5, 9), "time"));
            Assert.Equal("123.45", SqlLiteral.Format(123.45m, "money"));
            Assert.Equal("'0F8FAD5B-D9CB-469F-A165-70867728950E'", SqlLiteral.Format(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), "uniqueidentifier"));
            Assert.Equal("NULL", SqlLiteral.Format(System.Data.SqlTypes.SqlDecimal.Null, "decimal"));
        }
    }
}
