using SqlAssist.Core.Ai;
using SqlAssist.Core.Schema;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class SchemaDigestTests
    {
        [Fact]
        public void Lists_only_tables_named_in_the_sql()
        {
            var d = SchemaDigest.Build("SELECT * FROM dbo.Orders o JOIN [Customers] c ON c.CustomerId = o.CustomerId", TestSchema.Build(), true);
            Assert.Equal(2, d.TableCount);
            Assert.False(d.Truncated);
            Assert.Contains("dbo.Orders (table):", d.Text);
            Assert.Contains("dbo.Customers (table):", d.Text);
            Assert.Equal(2, d.Text.Split('\n').Length);
            Assert.Contains("CustomerID int NULL -> dbo.Customers.CustomerID", d.Text);
        }

        [Fact]
        public void Unknown_tables_null_schema_and_names_in_strings_give_nothing()
        {
            Assert.Equal("", SchemaDigest.Build("SELECT * FROM #tmp JOIN other.dbo.Nope n ON 1 = 1", TestSchema.Build(), true).Text);
            Assert.Equal(0, SchemaDigest.Build("SELECT * FROM dbo.Orders", null, true).TableCount);
            Assert.Equal(0, SchemaDigest.Build("SELECT 'Orders' -- Orders", TestSchema.Build(), true).TableCount);
            Assert.Equal(0, SchemaDigest.Build("", TestSchema.Build(), true).TableCount);
        }

        [Fact]
        public void A_schema_prefix_picks_that_schema()
        {
            Assert.Equal(1, SchemaDigest.Build("SELECT * FROM sales.Products", TestSchema.Build(), true).TableCount);
            Assert.Equal(0, SchemaDigest.Build("SELECT * FROM dbo.Products", TestSchema.Build(), true).TableCount);
        }

        [Fact]
        public void Stops_at_max_tables_and_says_so()
        {
            var d = SchemaDigest.Build("SELECT * FROM dbo.Orders, dbo.Customers", TestSchema.Build(), true, maxTables: 1);
            Assert.Equal(1, d.TableCount);
            Assert.True(d.Truncated);
        }

        [Fact]
        public void Descriptions_keys_and_identity_are_written_and_can_be_left_out()
        {
            var t = new DbObject("dbo", "Notes", false, new[]
            {
                new DbColumn("Id", "int", isNullable: false, isIdentity: true, isPrimaryKey: true),
                new DbColumn("Body", "nvarchar(200)", description: "ข้อความ\r\nหลายบรรทัด"),
            }, description: "บันทึก");
            var schema = new SchemaModel(new[] { t }, new ForeignKey[0]);
            string with = SchemaDigest.Build("SELECT * FROM Notes", schema, true).Text;
            string without = SchemaDigest.Build("SELECT * FROM Notes", schema, false).Text;
            Assert.Equal("dbo.Notes (table): -- บันทึก; Id int NOT NULL PK IDENTITY, Body nvarchar(200) NULL -- ข้อความ หลายบรรทัด", with);
            Assert.Equal("dbo.Notes (table): Id int NOT NULL PK IDENTITY, Body nvarchar(200) NULL", without);
        }
    }
}
