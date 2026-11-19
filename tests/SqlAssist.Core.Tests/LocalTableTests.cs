using SqlAssist.Core.Sql;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class LocalTableTests
    {
        [Fact]
        public void LocalScopeSurvivesGoOnlyForTempTables()
        {
            const string sql = "CREATE TABLE #t ([Order ID] int, Amount decimal(18,2)); DECLARE @t TABLE (Id int);\nGO\nSELECT * FROM #t;";
            var index = LocalTableIndex.Parse(sql);
            Assert.Collection(index.At(sql.Length), t => {
                Assert.Equal("#t", t.Name);
                Assert.Equal("Order ID", t.Columns[0].Name);
                Assert.Equal("decimal(18,2)", t.Columns[1].TypeName);
            });
        }

        [Fact]
        public void DropAndRecreateUsesNewestColumns()
        {
            const string sql = "CREATE TABLE #t (Old int); DROP TABLE IF EXISTS #t; CREATE TABLE #t (New int);";
            var index = LocalTableIndex.Parse(sql);
            Assert.Empty(index.At(sql.LastIndexOf("CREATE")));
            Assert.Equal("New", Assert.Single(Assert.Single(index.At(sql.Length)).Columns).Name);
        }

        [Theory]
        [InlineData("CREATE TABLE #t (Old int); ALTER TABLE #t ADD New int;")]
        [InlineData("-- CREATE TABLE #t (Id int);\nSELECT 1;")]
        [InlineData("CREATE TABLE #t (Id int")]
        [InlineData("SELECT 1 AS Id INTO #t;")]
        public void DoesNotGuessUnknownLocalColumns(string sql)
        {
            Assert.Empty(LocalTableIndex.Parse(sql).At(sql.Length));
        }

        [Theory]
        [InlineData("CREATE TABLE #t (Id int, CONSTRAINT CK CHECK (Id > 0));\nGO\nSELECT x.* FROM #t AS x;")]
        [InlineData("DECLARE @t AS TABLE (Id int PRIMARY KEY);\nSELECT x.* FROM @t AS x;")]
        public void ExpandsLocalAliasWithoutDatabaseSchema(string sql)
        {
            int caret = sql.LastIndexOf('*') + 1;
            var star = SqlContextAnalyzer.FindStar(sql, caret);
            var model = SchemaModel.Empty.WithLocalTables(LocalTableIndex.Parse(sql).At(caret));
            Assert.Equal("x.Id", StarExpander.Expand(star, model));
            Assert.Equal(1, StarExpander.Count(star, model));
        }

    }
}
