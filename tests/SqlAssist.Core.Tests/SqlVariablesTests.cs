using System.Linq;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class SqlVariablesTests
    {
        private static SqlVariable[] At(string textWithCaret)
        {
            int caret = textWithCaret.IndexOf('|');
            return SqlVariables.DeclaredBefore(textWithCaret.Remove(caret, 1), caret).ToArray();
        }

        [Fact]
        public void ProcedureParameters_WithTypesAndDirection()
        {
            var vars = At(@"CREATE PROCEDURE uspGetList @Product varchar(40)
    , @MaxPrice money
    , @ComparePrice money OUTPUT
    , @ListPrice money OUT
AS
    SET NOCOUNT ON;
    SELECT p.Name FROM Production.Product AS p WHERE p.ListPrice < |");

            Assert.Equal(new[] { "@Product", "@MaxPrice", "@ComparePrice", "@ListPrice" }, vars.Select(v => v.Name));
            Assert.All(vars, v => Assert.Equal(SqlVariableKind.Parameter, v.Kind));
            Assert.All(vars, v => Assert.Equal("uspGetList", v.Owner));
            Assert.Equal("VARCHAR(40)", vars[0].TypeName);
            Assert.Equal(("MONEY", "INPUT"), (vars[1].TypeName, vars[1].Direction));
            Assert.Equal("OUTPUT", vars[2].Direction);
            Assert.Equal("OUTPUT", vars[3].Direction);
        }

        [Fact]
        public void FunctionParametersInParens_AndReturnsTable()
        {
            var vars = At(@"CREATE OR ALTER FUNCTION dbo.fn_Orders (@CustomerId INT, @From DATE = NULL)
RETURNS @result TABLE (Id INT)
AS BEGIN
    INSERT @result SELECT 1 WHERE |");
            Assert.Equal(new[] { "@CustomerId", "@From", "@result" }, vars.Select(v => v.Name));
            Assert.Equal("dbo.fn_Orders", vars[0].Owner);
            Assert.Equal("DATE", vars[1].TypeName);
            Assert.Equal(SqlVariableKind.TableVariable, vars[2].Kind);
        }

        [Fact]
        public void Declare_ListsWithDefaultsAndTableVariables()
        {
            var vars = At(@"DECLARE @a INT = (SELECT MAX(x) FROM t), @b AS decimal(18,2), @c nvarchar(max) = CASE WHEN 1=1 THEN 'a' END
DECLARE @t TABLE (Id INT, Name NVARCHAR(50));
DECLARE @d datetime = GETDATE()
SELECT * FROM t WHERE x = |");
            Assert.Equal(new[] { "@a", "@b", "@c", "@t", "@d" }, vars.Select(v => v.Name));
            Assert.Equal(new[] { "INT", "DECIMAL(18, 2)", "NVARCHAR(MAX)", "TABLE", "DATETIME" }, vars.Select(v => v.TypeName));
            Assert.Equal(SqlVariableKind.TableVariable, vars[3].Kind);
        }

        [Fact]
        public void OnlyDeclarationsAboveTheCaretInTheSameBatch()
        {
            var vars = At(@"DECLARE @old INT
GO
DECLARE @x INT
SELECT |
DECLARE @later INT");
            Assert.Equal(new[] { "@x" }, vars.Select(v => v.Name));
        }

        [Fact]
        public void IgnoresCommentsStringsAndTheNameBeingTyped()
        {
            var vars = At("-- DECLARE @c INT\nSELECT 'DECLARE @s INT'\nDECLARE @Ma|");
            Assert.Empty(vars);
        }

        [Fact]
        public void FuzzyMatcherFindsVariablesFromTheAtSign()
        {
            Assert.True(FuzzyMatcher.Score("@", "@MaxPrice") > 0);
            Assert.True(FuzzyMatcher.Score("@max", "@MaxPrice") > 0);
            Assert.True(FuzzyMatcher.Score("@mp", "@MaxPrice") > 0);
        }
    }
}
