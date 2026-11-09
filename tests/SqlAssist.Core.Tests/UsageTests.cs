using System.Linq;
using SqlAssist.Core.Search;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class UsageTests
    {
        private const string Db = "Sales";

        private static SearchItem Table(string schema, string name) => new SearchItem(Db, schema, name, SearchObjectType.Table, script: "CREATE TABLE ...");
        private static SearchItem Proc(string schema, string name, string definition) => new SearchItem(Db, schema, name, SearchObjectType.Procedure, definition: definition);
        private static SearchItem View(string schema, string name, string definition) => new SearchItem(Db, schema, name, SearchObjectType.View, definition: definition);
        private static SearchItem Column(string schema, string table, string name) =>
            new SearchItem(Db, schema, name, SearchObjectType.Column, table, null, "CREATE TABLE ...", SearchObjectType.Table);

        private static UsageTarget OrdersTarget => new UsageTarget(Table("dbo", "Orders"));

        [Fact]
        public void CanFind_covers_tables_views_routines_columns_and_types_only()
        {
            foreach (var type in new[] { SearchObjectType.Table, SearchObjectType.View, SearchObjectType.Procedure, SearchObjectType.Function, SearchObjectType.UserDefinedType })
                Assert.True(UsageTarget.CanFind(new SearchItem(Db, "dbo", "x", type)), type.ToString());
            Assert.True(UsageTarget.CanFind(Column("dbo", "Orders", "Note")));
            foreach (var type in new[] { SearchObjectType.Schema, SearchObjectType.Trigger, SearchObjectType.Index, SearchObjectType.Constraint })
                Assert.False(UsageTarget.CanFind(new SearchItem(Db, "dbo", "x", type)), type.ToString());
            Assert.False(UsageTarget.CanFind(null));
            Assert.Throws<System.ArgumentException>(() => new UsageTarget(new SearchItem(Db, "dbo", "ix", SearchObjectType.Index)));
        }

        [Fact]
        public void A_column_target_is_the_column_of_its_owner()
        {
            var column = new UsageTarget(Column("dbo", "Orders", "Note"));
            Assert.True(column.IsColumn);
            Assert.Equal("dbo", column.Schema);
            Assert.Equal("Orders", column.Name);
            Assert.Equal("Note", column.Column);
            Assert.Equal(SearchObjectType.Table, column.Type);
            Assert.Equal("[dbo].[Orders].[Note]", column.DisplayName);

            var table = OrdersTarget;
            Assert.False(table.IsColumn);
            Assert.Equal("[dbo].[Orders]", table.DisplayName);
        }

        [Fact]
        public void TypeOf_maps_sys_objects_type_codes()
        {
            Assert.Equal(SearchObjectType.Table, UsageFinder.TypeOf("U "));
            Assert.Equal(SearchObjectType.View, UsageFinder.TypeOf("V"));
            Assert.Equal(SearchObjectType.Procedure, UsageFinder.TypeOf("P"));
            Assert.Equal(SearchObjectType.Procedure, UsageFinder.TypeOf("PC"));
            foreach (var code in new[] { "FN", "IF", "TF", "FS", "FT" }) Assert.Equal(SearchObjectType.Function, UsageFinder.TypeOf(code));
            Assert.Equal(SearchObjectType.Trigger, UsageFinder.TypeOf("TR"));
            Assert.Equal(SearchObjectType.None, UsageFinder.TypeOf("SN"));
            Assert.Equal(SearchObjectType.None, UsageFinder.TypeOf(null));
        }

        [Fact]
        public void One_usage_per_referrer_and_the_strongest_evidence_wins()
        {
            var rows = new[]
            {
                new RawUsage("dbo", "usp_A", "P", UsageKind.NameOnly),
                new RawUsage("dbo", "usp_A", "P", UsageKind.Dependency),
                new RawUsage("dbo", "usp_A", "P", UsageKind.Dependency),
                new RawUsage("dbo", "OrderLines", "U", UsageKind.ForeignKey),
                new RawUsage("dbo", "trg_Orders", "TR", UsageKind.Trigger),
                new RawUsage("dbo", "trg_Orders", "TR", UsageKind.Dependency),
            };
            var usages = UsageFinder.Merge(OrdersTarget, rows, new SearchItem[0]);
            Assert.Equal(3, usages.Count);
            Assert.Equal(UsageKind.Dependency, usages.Single(u => u.Referrer.Name == "usp_A").Kind);
            Assert.Equal(UsageKind.ForeignKey, usages.Single(u => u.Referrer.Name == "OrderLines").Kind);
            Assert.Equal(UsageKind.Dependency, usages.Single(u => u.Referrer.Name == "trg_Orders").Kind);
        }

        [Fact]
        public void Kind_strength_order_is_dependency_foreign_key_trigger_synonym_name_only_text()
        {
            Assert.True(UsageKind.Dependency < UsageKind.ForeignKey);
            Assert.True(UsageKind.ForeignKey < UsageKind.Trigger);
            Assert.True(UsageKind.Trigger < UsageKind.Synonym);
            Assert.True(UsageKind.Synonym < UsageKind.NameOnly);
            Assert.True(UsageKind.NameOnly < UsageKind.TextMatch);
        }

        [Fact]
        public void Referrers_are_ordered_by_evidence_then_schema_and_name()
        {
            var rows = new[]
            {
                new RawUsage("dbo", "z", "P", UsageKind.NameOnly),
                new RawUsage("dbo", "b", "P", UsageKind.Dependency),
                new RawUsage("app", "c", "V", UsageKind.Dependency),
                new RawUsage("dbo", "a", "P", UsageKind.Dependency),
            };
            var names = UsageFinder.Merge(OrdersTarget, rows, new SearchItem[0]).Select(u => u.Referrer.Schema + "." + u.Referrer.Name).ToArray();
            Assert.Equal(new[] { "app.c", "dbo.a", "dbo.b", "dbo.z" }, names);
        }

        [Fact]
        public void The_referrer_is_the_index_item_so_it_has_its_definition_and_hits_for_every_way_to_write_the_name()
        {
            string definition = "SELECT * FROM Orders; SELECT * FROM [Orders]; SELECT * FROM dbo.Orders; SELECT * FROM [dbo].[Orders]; SELECT * FROM OrdersArchive;";
            var proc = Proc("dbo", "usp_A", definition);
            var usages = UsageFinder.Merge(OrdersTarget, new[] { new RawUsage("dbo", "usp_A", "P", UsageKind.Dependency) }, new[] { proc });
            var usage = usages.Single();
            Assert.Same(proc, usage.Referrer);
            Assert.Equal(4, usage.Hits.Count);
            Assert.All(usage.Hits, h => Assert.Equal("Orders", definition.Substring(h.Start, h.Length)));
        }

        [Fact]
        public void Hits_ignore_case_and_stop_at_longer_words()
        {
            string definition = "select * from ORDERS o join Orders_Old x join MyOrders y";
            var usage = UsageFinder.Merge(OrdersTarget, new[] { new RawUsage("dbo", "v", "V", UsageKind.Dependency) }, new[] { View("dbo", "v", definition) }).Single();
            Assert.Single(usage.Hits);
            Assert.Equal("ORDERS", definition.Substring(usage.Hits[0].Start, usage.Hits[0].Length));
        }

        [Fact]
        public void A_referrer_the_index_does_not_know_is_still_listed_without_hits()
        {
            var usage = UsageFinder.Merge(OrdersTarget, new[] { new RawUsage("dbo", "usp_New", "P", UsageKind.Dependency) }, new SearchItem[0]).Single();
            Assert.Equal(Db, usage.Referrer.Database);
            Assert.Equal("dbo", usage.Referrer.Schema);
            Assert.Equal("usp_New", usage.Referrer.Name);
            Assert.Equal(SearchObjectType.Procedure, usage.Referrer.Type);
            Assert.Null(usage.Referrer.Definition);
            Assert.Empty(usage.Hits);
        }

        [Fact]
        public void A_synonym_is_listed_as_one_and_has_no_search_type()
        {
            var usage = UsageFinder.Merge(OrdersTarget, new[] { new RawUsage("dbo", "OrdersSyn", "SN", UsageKind.Synonym) }, new SearchItem[0]).Single();
            Assert.Equal(SearchObjectType.None, usage.Referrer.Type);
            Assert.Equal("Synonym", usage.Referrer.SubKind);
            Assert.Equal(UsageKind.Synonym, usage.Kind);
        }

        [Fact]
        public void A_synonym_found_as_a_plain_dependency_is_still_a_synonym()
        {
            // sys.sql_expression_dependencies lists synonyms too, with type SN; the same synonym can also arrive from sys.synonyms.
            var rows = new[] { new RawUsage("dbo", "OrdersSyn", "SN", UsageKind.Dependency), new RawUsage("dbo", "OrdersSyn", "SN ", UsageKind.Synonym) };
            var usage = UsageFinder.Merge(OrdersTarget, rows, new SearchItem[0]).Single();
            Assert.Equal(UsageKind.Synonym, usage.Kind);
            Assert.Equal("Synonym", usage.Referrer.SubKind);
        }

        [Fact]
        public void Index_items_of_other_kinds_with_the_same_name_are_not_mistaken_for_the_referrer()
        {
            var column = new SearchItem(Db, "dbo", "usp_A", SearchObjectType.Column, "T", null, "CREATE TABLE ...", SearchObjectType.Table);
            var proc = Proc("dbo", "usp_A", "SELECT 1 FROM Orders");
            var usage = UsageFinder.Merge(OrdersTarget, new[] { new RawUsage("dbo", "usp_A", "P", UsageKind.Dependency) }, new SearchItem[] { column, proc }).Single();
            Assert.Same(proc, usage.Referrer);
        }

        [Fact]
        public void Names_are_matched_without_regard_to_case()
        {
            var proc = Proc("DBO", "USP_a", "SELECT 1 FROM Orders");
            var usage = UsageFinder.Merge(OrdersTarget, new[] { new RawUsage("dbo", "usp_A", "P", UsageKind.Dependency) }, new[] { proc }).Single();
            Assert.Same(proc, usage.Referrer);
        }

        [Fact]
        public void An_object_does_not_use_itself()
        {
            var rows = new[] { new RawUsage("dbo", "Orders", "U", UsageKind.ForeignKey), new RawUsage("dbo", "Other", "U", UsageKind.ForeignKey) };
            var usages = UsageFinder.Merge(OrdersTarget, rows, new SearchItem[0]);
            Assert.Equal("Other", usages.Single().Referrer.Name);
        }

        [Fact]
        public void A_name_too_short_to_search_for_is_listed_without_hits()
        {
            var target = new UsageTarget(Table("dbo", "T"));
            var usage = UsageFinder.Merge(target, new[] { new RawUsage("dbo", "p", "P", UsageKind.Dependency) }, new[] { Proc("dbo", "p", "SELECT * FROM T") }).Single();
            Assert.Empty(usage.Hits);
        }

        [Fact]
        public void No_rows_means_no_usages()
        {
            Assert.Empty(UsageFinder.Merge(OrdersTarget, new RawUsage[0], new SearchItem[0]));
        }

        [Fact]
        public void A_column_keeps_only_the_referrers_whose_text_names_it()
        {
            var column = new UsageTarget(Column("dbo", "Orders", "Note"));
            string uses = "SELECT o.Note, o.Id FROM Orders o";
            var a = Proc("dbo", "usp_Uses", uses);
            var b = Proc("dbo", "usp_NotesOnly", "SELECT o.Notebook, o.NoteId FROM Orders o");
            var table = Table("dbo", "OrderLines");
            var parentUsages = UsageFinder.Merge(OrdersTarget, new[]
            {
                new RawUsage("dbo", "usp_Uses", "P", UsageKind.Dependency),
                new RawUsage("dbo", "usp_NotesOnly", "P", UsageKind.Dependency),
                new RawUsage("dbo", "OrderLines", "U", UsageKind.ForeignKey),
                new RawUsage("dbo", "usp_Unknown", "P", UsageKind.Dependency),
            }, new SearchItem[] { a, b, table });

            var usages = UsageFinder.ForColumn(column, parentUsages);
            var usage = usages.Single();
            Assert.Same(a, usage.Referrer);
            Assert.Equal(UsageKind.TextMatch, usage.Kind);
            Assert.Equal("Note", uses.Substring(usage.Hits.Single().Start, usage.Hits.Single().Length));
        }

        [Fact]
        public void ForColumn_rejects_a_target_that_is_not_a_column()
        {
            Assert.Throws<System.ArgumentException>(() => UsageFinder.ForColumn(OrdersTarget, new Usage[0]));
        }
    }
}
