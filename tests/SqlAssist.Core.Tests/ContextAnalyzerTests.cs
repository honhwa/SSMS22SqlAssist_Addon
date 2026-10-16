using System.Linq;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class ContextAnalyzerTests
    {
        // "|" marks the caret.
        private static CaretContext At(string sql)
        {
            int caret = sql.IndexOf('|');
            return SqlContextAnalyzer.Analyze(sql.Remove(caret, 1), caret);
        }

        [Fact]
        public void AfterFrom_IsTableNameWithAlias()
        {
            var c = At("SELECT * FROM Cus|");
            Assert.Equal(CaretContextKind.TableName, c.Kind);
            Assert.True(c.AllowAlias);
            Assert.False(c.IsJoin);
            Assert.Equal("Cus", c.TypedText);
        }

        [Fact]
        public void AfterJoin_IsJoinWithSiblings()
        {
            var c = At("SELECT * FROM Customers c\nJOIN |");
            Assert.Equal(CaretContextKind.TableName, c.Kind);
            Assert.True(c.IsJoin);
            var t = Assert.Single(c.SiblingTables);
            Assert.Equal("Customers", t.Name);
            Assert.Equal("c", t.Alias);
        }

        [Fact]
        public void LeftOuterJoin_IsJoin()
        {
            Assert.True(At("SELECT * FROM Customers c LEFT OUTER JOIN O|").IsJoin);
        }

        [Fact]
        public void CommaInFromList_IsTableName()
        {
            Assert.Equal(CaretContextKind.TableName, At("SELECT * FROM Customers c, |").Kind);
        }

        [Fact]
        public void CommaInSelectList_IsGeneral()
        {
            Assert.Equal(CaretContextKind.General, At("SELECT a, | FROM Customers").Kind);
        }

        [Theory]
        [InlineData("UPDATE |")]
        [InlineData("INSERT INTO |")]
        [InlineData("DELETE FROM |")]
        [InlineData("TRUNCATE TABLE |")]
        public void TablePositionsThatCannotTakeAnAlias(string sql)
        {
            var c = At(sql);
            Assert.Equal(CaretContextKind.TableName, c.Kind);
            Assert.False(c.AllowAlias);
        }

        [Fact]
        public void AliasDot_IsQualifiedColumn()
        {
            var c = At("SELECT c.| FROM Customers c");
            Assert.Equal(CaretContextKind.Qualified, c.Kind);
            Assert.Equal("c", c.Qualifier);
            Assert.False(c.QualifierIsSchema);
            Assert.Contains(c.TablesInScope, t => t.Alias == "c");
        }

        [Fact]
        public void SchemaDotInTablePosition_IsSchemaQualifier()
        {
            var c = At("SELECT * FROM dbo.| WHERE x = 1");
            Assert.Equal(CaretContextKind.Qualified, c.Kind);
            Assert.True(c.QualifierIsSchema);
            Assert.True(c.AllowAlias);
        }

        [Fact]
        public void FromClauseAfterCaret_IsInScope()
        {
            var c = At("SELECT Cu| FROM dbo.Customers AS c JOIN Orders o ON o.CustomerID = c.CustomerID");
            Assert.Equal(CaretContextKind.General, c.Kind);
            Assert.Equal(new[] { "c", "o" }, c.TablesInScope.Select(t => t.Alias).ToArray());
            Assert.Equal("dbo", c.TablesInScope[0].Schema);
        }

        [Fact]
        public void BlankLineSeparatesStatements()
        {
            var c = At("SELECT * FROM Customers c\n\nSELECT | FROM Orders o");
            Assert.Equal(new[] { "o" }, c.TablesInScope.Select(t => t.Alias).ToArray());
        }

        [Fact]
        public void SemicolonAndGoSeparateStatements()
        {
            Assert.Equal(new[] { "o" }, At("SELECT * FROM Customers c; SELECT | FROM Orders o").TablesInScope.Select(t => t.Alias));
            Assert.Equal(new[] { "o" }, At("SELECT * FROM Customers c\nGO\nSELECT | FROM Orders o").TablesInScope.Select(t => t.Alias));
        }

        [Fact]
        public void SubquerySeesItsOwnAndOuterTables_OuterDoesNotSeeInner()
        {
            var inner = At("SELECT * FROM Customers c WHERE EXISTS (SELECT 1 FROM Orders o WHERE o.| = c.CustomerID)");
            Assert.Equal(new[] { "o", "c" }, inner.TablesInScope.Select(t => t.Alias).ToArray());

            var outer = At("SELECT | FROM Customers c WHERE EXISTS (SELECT 1 FROM Orders o)");
            Assert.Equal(new[] { "c" }, outer.TablesInScope.Select(t => t.Alias).ToArray());
        }

        [Fact]
        public void KeywordsAreNotAliases()
        {
            var c = At("SELECT | FROM Customers WITH (NOLOCK) WHERE 1 = 1");
            var t = Assert.Single(c.TablesInScope);
            Assert.Null(t.Alias);
        }

        [Fact]
        public void InsideStringOrComment()
        {
            Assert.True(At("SELECT 'abc|' FROM T").InStringOrComment);
            Assert.True(At("SELECT 1 -- note|").InStringOrComment);
            Assert.True(At("SELECT 1 /* x| */").InStringOrComment);
            Assert.False(At("SELECT 'abc' |").InStringOrComment);
        }

        [Fact]
        public void UnclosedBracketDoesNotSwallowRestOfScript()
        {
            var c = At("SELECT [Cus|\nFROM Customers c");
            Assert.Equal("Cus", c.TypedText);
            Assert.Single(c.TablesInScope);
        }

        [Theory]
        [InlineData("SELECT * FROM Customers c|", true)]
        [InlineData("SELECT * FROM Customers AS c|", true)]
        [InlineData("SELECT * FROM Customers c JOIN Orders o|", true)]
        [InlineData("SELECT * FROM Customers c WHERE c|", false)]
        [InlineData("SELECT c| FROM Customers", false)]
        [InlineData("UPDATE Customers S|", false)]
        public void AliasPosition(string sql, bool expected) => Assert.Equal(expected, At(sql).IsAliasPosition);

        [Fact]
        public void AliasesInStatement()
        {
            var c = At("SELECT * FROM Customers c JOIN Orders o ON 1=1 JOIN |");
            Assert.Equal(new[] { "c", "o" }, c.AliasesInStatement.OrderBy(a => a).ToArray());
        }
    }
}
