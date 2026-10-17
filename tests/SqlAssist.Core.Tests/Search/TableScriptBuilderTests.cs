using SqlAssist.Core.Schema;
using SqlAssist.Core.Search;
using Xunit;

namespace SqlAssist.Core.Tests.Search
{
    public class TableScriptBuilderTests
    {
        [Fact]
        public void ColumnsAndPrimaryKey()
        {
            var script = TableScriptBuilder.Build("dbo", "Orders",
                new[]
                {
                    new TableColumn("Id", "int", false),
                    new TableColumn("Note", SqlTypeFormatter.Format("nvarchar", 400, 0, 0), true),
                    new TableColumn("Total", SqlTypeFormatter.Format("decimal", 9, 18, 2), false),
                },
                new PrimaryKey("PK_Orders", true, new[] { "Id" }), "\n");

            Assert.Equal(
                "CREATE TABLE [dbo].[Orders] (\n" +
                "    [Id] int NOT NULL,\n" +
                "    [Note] nvarchar(200) NULL,\n" +
                "    [Total] decimal(18,2) NOT NULL,\n" +
                "    CONSTRAINT [PK_Orders] PRIMARY KEY CLUSTERED ([Id])\n" +
                ");", script);
        }

        [Fact]
        public void NoPrimaryKey_CompositeKeyAndOddNames()
        {
            Assert.Equal("CREATE TABLE [s]]x].[a b] (\n    [c] int NULL\n);",
                TableScriptBuilder.Build("s]x", "a b", new[] { new TableColumn("c", "int", true) }, null, "\n"));

            var composite = TableScriptBuilder.Build("dbo", "Line",
                new[] { new TableColumn("OrderId", "int", false), new TableColumn("LineNo", "int", false) },
                new PrimaryKey("PK_Line", false, new[] { "OrderId", "LineNo" }), "\n");
            Assert.Contains("PRIMARY KEY NONCLUSTERED ([OrderId], [LineNo])", composite);
        }

        [Theory]
        [InlineData("varchar", -1, "varchar(max)")]
        [InlineData("nvarchar", -1, "nvarchar(max)")]
        [InlineData("nchar", 20, "nchar(10)")]
        [InlineData("int", 4, "int")]
        public void TypeFormatting(string type, short maxLength, string expected) =>
            Assert.Equal(expected, SqlTypeFormatter.Format(type, maxLength, 0, 0));
    }
}
