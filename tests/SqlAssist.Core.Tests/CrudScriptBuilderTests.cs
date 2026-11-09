using System;
using System.Linq;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class CrudScriptBuilderTests
    {
        private static readonly DbObject Orders = new DbObject("dbo", "Orders", false, new[]
        {
            new DbColumn("OrderID", "int", isNullable: false, isIdentity: true, isPrimaryKey: true),
            new DbColumn("CustomerID", "int", isNullable: false),
            new DbColumn("Note", "nvarchar(200)"),
        });

        private static CrudOptions Opt(CrudActions actions = CrudActions.All) => new CrudOptions { Actions = actions, NewLine = "\n" };

        [Fact]
        public void Insert_skips_identity_and_returns_it_through_an_output_parameter()
        {
            var script = CrudScriptBuilder.Build(Orders, Opt(CrudActions.Insert));
            Assert.Equal(
                "CREATE OR ALTER PROCEDURE [dbo].[usp_Orders_Insert]\n" +
                "    @CustomerID int,\n" +
                "    @Note nvarchar(200) = NULL,\n" +
                "    @OrderID int OUTPUT\n" +
                "AS\n" +
                "BEGIN\n" +
                "    SET NOCOUNT ON;\n" +
                "\n" +
                "    INSERT INTO [dbo].[Orders] ([CustomerID], [Note])\n" +
                "    VALUES (@CustomerID, @Note);\n" +
                "\n" +
                "    SET @OrderID = SCOPE_IDENTITY();\n" +
                "END\n" +
                "GO\n", script.Text);
            Assert.Empty(script.Skipped);
        }

        [Fact]
        public void Select_takes_the_key_as_an_optional_filter_and_lists_every_column()
        {
            string text = CrudScriptBuilder.Build(Orders, Opt(CrudActions.Select)).Text;
            Assert.Contains("[dbo].[usp_Orders_Select]\n    @OrderID int = NULL\nAS", text);
            Assert.Contains("    SELECT [OrderID], [CustomerID], [Note]\n    FROM [dbo].[Orders]\n    WHERE (@OrderID IS NULL OR [OrderID] = @OrderID);", text);
        }

        [Fact]
        public void Update_sets_non_key_columns_and_filters_on_the_key_without_defaults()
        {
            string text = CrudScriptBuilder.Build(Orders, Opt(CrudActions.Update)).Text;
            Assert.Contains("[dbo].[usp_Orders_Update]\n    @OrderID int,\n    @CustomerID int,\n    @Note nvarchar(200)\nAS", text);
            Assert.Contains("    UPDATE [dbo].[Orders]\n    SET [CustomerID] = @CustomerID,\n        [Note] = @Note\n    WHERE [OrderID] = @OrderID;", text);
        }

        [Fact]
        public void Delete_filters_on_the_key_only()
        {
            string text = CrudScriptBuilder.Build(Orders, Opt(CrudActions.Delete)).Text;
            Assert.Contains("[dbo].[usp_Orders_Delete]\n    @OrderID int\nAS", text);
            Assert.Contains("    DELETE FROM [dbo].[Orders]\n    WHERE [OrderID] = @OrderID;", text);
        }

        [Fact]
        public void All_actions_are_separated_by_GO_in_select_insert_update_delete_order()
        {
            string text = CrudScriptBuilder.Build(Orders, Opt()).Text;
            int select = text.IndexOf("usp_Orders_Select"), insert = text.IndexOf("usp_Orders_Insert"),
                update = text.IndexOf("usp_Orders_Update"), delete = text.IndexOf("usp_Orders_Delete");
            Assert.True(select < insert && insert < update && update < delete);
            Assert.Equal(4, text.Split('\n').Count(l => l == "GO"));
        }

        [Fact]
        public void Composite_key_joins_the_where_with_AND_and_is_never_set()
        {
            var table = new DbObject("dbo", "OrderLines", false, new[]
            {
                new DbColumn("OrderID", "int", isNullable: false, isPrimaryKey: true),
                new DbColumn("LineNo", "int", isNullable: false, isPrimaryKey: true),
                new DbColumn("Qty", "int", isNullable: false),
            });
            string update = CrudScriptBuilder.Build(table, Opt(CrudActions.Update)).Text;
            Assert.Contains("    SET [Qty] = @Qty\n    WHERE [OrderID] = @OrderID\n      AND [LineNo] = @LineNo;", update);
            Assert.DoesNotContain("[OrderID] = @OrderID,", update);
            string delete = CrudScriptBuilder.Build(table, Opt(CrudActions.Delete)).Text;
            Assert.Contains("WHERE [OrderID] = @OrderID\n      AND [LineNo] = @LineNo;", delete);
            string insert = CrudScriptBuilder.Build(table, Opt(CrudActions.Insert)).Text;
            Assert.Contains("INSERT INTO [dbo].[OrderLines] ([OrderID], [LineNo], [Qty])", insert);
            Assert.DoesNotContain("OUTPUT", insert);
            Assert.DoesNotContain("SCOPE_IDENTITY", insert);
        }

        [Fact]
        public void Table_without_a_primary_key_gets_select_and_insert_and_says_why_the_rest_is_missing()
        {
            var heap = new DbObject("dbo", "Log", false, new[] { new DbColumn("Msg", "nvarchar(max)"), new DbColumn("At", "datetime", isNullable: false) });
            var script = CrudScriptBuilder.Build(heap, Opt());
            Assert.Contains("usp_Log_Select", script.Text);
            Assert.Contains("usp_Log_Insert", script.Text);
            Assert.DoesNotContain("usp_Log_Update", script.Text);
            Assert.DoesNotContain("usp_Log_Delete", script.Text);
            Assert.Equal(2, script.Skipped.Count);
            Assert.Contains(script.Skipped, s => s.StartsWith("Update") && s.Contains("primary key"));
            Assert.Contains(script.Skipped, s => s.StartsWith("Delete") && s.Contains("primary key"));
            Assert.StartsWith("-- Skipped", script.Text);
            // No key: the select has no parameter and no WHERE.
            Assert.Contains("[dbo].[usp_Log_Select]\nAS", script.Text);
            Assert.DoesNotContain("WHERE", script.Text);
        }

        [Fact]
        public void Table_with_only_a_key_cannot_be_updated()
        {
            var table = new DbObject("dbo", "Tag", false, new[] { new DbColumn("TagID", "int", isNullable: false, isPrimaryKey: true) });
            var script = CrudScriptBuilder.Build(table, Opt(CrudActions.Update | CrudActions.Delete));
            Assert.DoesNotContain("usp_Tag_Update", script.Text);
            Assert.Contains("usp_Tag_Delete", script.Text);
            Assert.Single(script.Skipped);
            Assert.Contains("no column to set", script.Skipped[0]);
        }

        [Fact]
        public void Table_with_only_an_identity_column_inserts_default_values()
        {
            var table = new DbObject("dbo", "Seq", false, new[] { new DbColumn("ID", "int", isNullable: false, isIdentity: true, isPrimaryKey: true) });
            string text = CrudScriptBuilder.Build(table, Opt(CrudActions.Insert)).Text;
            Assert.Contains("    @ID int OUTPUT\nAS", text);
            Assert.Contains("    INSERT INTO [dbo].[Seq] DEFAULT VALUES;", text);
        }

        [Fact]
        public void Rowversion_and_computed_columns_are_selected_but_never_written()
        {
            var table = new DbObject("dbo", "T", false, new[]
            {
                new DbColumn("ID", "int", isNullable: false, isPrimaryKey: true),
                new DbColumn("Name", "nvarchar(10)", isNullable: false),
                new DbColumn("Total", "int", isComputed: true),
                new DbColumn("RowVer", "timestamp", isNullable: false),
            });
            Assert.Contains("SELECT [ID], [Name], [Total], [RowVer]", CrudScriptBuilder.Build(table, Opt(CrudActions.Select)).Text);
            string insert = CrudScriptBuilder.Build(table, Opt(CrudActions.Insert)).Text;
            Assert.Contains("INSERT INTO [dbo].[T] ([ID], [Name])", insert);
            Assert.DoesNotContain("@Total", insert);
            string update = CrudScriptBuilder.Build(table, Opt(CrudActions.Update)).Text;
            Assert.DoesNotContain("@RowVer", update);
            Assert.DoesNotContain("@Total", update);
        }

        [Fact]
        public void Awkward_names_are_bracketed_and_get_valid_parameter_names()
        {
            var table = new DbObject("dbo", "Order Lines", false, new[]
            {
                new DbColumn("Order Date", "datetime", isNullable: false, isPrimaryKey: true),
                new DbColumn("a]b", "int", isNullable: false),
                new DbColumn("a_b", "int", isNullable: false),
                new DbColumn("1st", "int", isNullable: false),
            });
            string update = CrudScriptBuilder.Build(table, Opt(CrudActions.Update)).Text;
            Assert.Contains("[dbo].[usp_Order Lines_Update]", update);
            Assert.Contains("@Order_Date datetime,", update);
            Assert.Contains("[a]]b] = @a_b,", update);
            Assert.Contains("[a_b] = @a_b2,", update);
            Assert.Contains("[1st] = @_1st", update);
            Assert.Contains("WHERE [Order Date] = @Order_Date;", update);
        }

        [Fact]
        public void A_column_without_a_known_type_is_declared_sql_variant()
        {
            var table = new DbObject("dbo", "T", false, new[] { new DbColumn("ID", "", isNullable: false, isPrimaryKey: true) });
            Assert.Contains("@ID sql_variant", CrudScriptBuilder.Build(table, Opt(CrudActions.Delete)).Text);
        }

        [Fact]
        public void CREATE_can_replace_CREATE_OR_ALTER_and_the_name_pattern_is_applied()
        {
            var options = Opt(CrudActions.Delete);
            options.CreateOrAlter = false;
            options.NamePattern = "{Schema}_{Table}_Remove";
            string text = CrudScriptBuilder.Build(Orders, options).Text;
            Assert.StartsWith("CREATE PROCEDURE [dbo].[dbo_Orders_Remove]\n", text);
        }

        [Theory]
        [InlineData("usp_{Table}_{Action}", "usp_Orders_Insert")]
        [InlineData("{Schema}.{Table}{Action}", "dbo.OrdersInsert")]
        [InlineData("p_{table}_{ACTION}", "p_Orders_Insert")]
        [InlineData("", "usp_Orders_Insert")]
        [InlineData("   ", "usp_Orders_Insert")]
        public void ProcedureName_fills_the_placeholders_and_falls_back_to_the_default(string pattern, string expected) =>
            Assert.Equal(expected, CrudScriptBuilder.ProcedureName(pattern, "dbo", "Orders", CrudActions.Insert));

        [Fact]
        public void Views_and_missing_tables_are_rejected()
        {
            var view = new DbObject("dbo", "v", true, new[] { new DbColumn("a", "int") });
            Assert.Throws<ArgumentException>(() => CrudScriptBuilder.Build(view, Opt()));
            Assert.Throws<ArgumentNullException>(() => CrudScriptBuilder.Build(null, Opt()));
        }

        [Fact]
        public void No_action_selected_gives_empty_text()
        {
            var script = CrudScriptBuilder.Build(Orders, Opt(CrudActions.None));
            Assert.Equal("", script.Text);
            Assert.Empty(script.Skipped);
        }

        [Fact]
        public void Default_newline_is_CRLF()
        {
            string text = CrudScriptBuilder.Build(Orders, new CrudOptions { Actions = CrudActions.Delete }).Text;
            Assert.Contains("\r\n", text);
            Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        }
    }
}
