using System.Linq;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Formatting;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class GroupByExpanderTests
    {
        private static string L(params string[] lines) => string.Join("\n", lines);

        /// <summary>The caret is written as '|'. Returns the text after the expansion, with '|' where the caret goes after it, or
        /// null when Tab would do nothing.</summary>
        private static string Run(string sqlWithCaret, CommaStyle style = CommaStyle.Trailing, int indent = 4, string newLine = "\n")
        {
            int caret = sqlWithCaret.IndexOf('|');
            string sql = sqlWithCaret.Remove(caret, 1);
            var found = GroupByExpander.Find(sql, caret);
            if (found == null) return null;
            var text = found.Build(new FormatOptions { CommaStyle = style, IndentSize = indent }, newLine);
            string result = sql.Substring(0, found.ReplaceStart) + text.Text + sql.Substring(found.ReplaceEnd);
            return result.Insert(found.ReplaceStart + text.CaretOffset, "|");
        }

        private static string[] Items(string sqlWithCaret)
        {
            int caret = sqlWithCaret.IndexOf('|');
            var found = GroupByExpander.Find(sqlWithCaret.Remove(caret, 1), caret);
            return found?.Expressions.ToArray();
        }

        // ---- the basic case ----

        [Fact]
        public void ColumnsAndScalarExpressionsFollowTheSelectListAndAggregatesAreLeftOut()
        {
            string before = L(
                "SELECT",
                "    o.DepartmentId,",
                "    YEAR(o.OrderDate) AS OrderYear,",
                "    COUNT(*) AS TotalOrders,",
                "    SUM(o.Amount) AS TotalAmount",
                "FROM dbo.Orders o",
                "WHERE o.IsActive = 1",
                "GROUP BY ");
            Assert.Equal(L(
                "SELECT",
                "    o.DepartmentId,",
                "    YEAR(o.OrderDate) AS OrderYear,",
                "    COUNT(*) AS TotalOrders,",
                "    SUM(o.Amount) AS TotalAmount",
                "FROM dbo.Orders o",
                "WHERE o.IsActive = 1",
                "GROUP BY",
                "    o.DepartmentId,",
                "    YEAR(o.OrderDate)|"), Run(before + "|"));
        }

        [Fact]
        public void ASemicolonThatWasThereStaysAfterTheList() =>
            Assert.Equal("SELECT a, COUNT(*) FROM t GROUP BY\n    a|;", Run("SELECT a, COUNT(*) FROM t GROUP BY |;"));

        [Fact]
        public void NoSemicolonIsAddedWhenThereWasNone() =>
            Assert.Equal("SELECT a, COUNT(*) FROM t GROUP BY\n    a|", Run("SELECT a, COUNT(*) FROM t GROUP BY |"));

        [Theory]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY|", "SELECT a, COUNT(*) FROM t GROUP BY\n    a|")]
        [InlineData("select a, count(*) from t group by |", "select a, count(*) from t group by\n    a|")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY   |", "SELECT a, COUNT(*) FROM t GROUP BY\n    a|")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP\tBY |", "SELECT a, COUNT(*) FROM t GROUP\tBY\n    a|")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY\n    |", "SELECT a, COUNT(*) FROM t GROUP BY\n    a|")]   // Enter, then Tab
        public void CaretRightAfterByOrAfterWhiteSpaceWorks(string sqlWithCaret, string expected) =>
            Assert.Equal(expected, Run(sqlWithCaret));

        [Fact]
        public void SelectWithoutAggregatesIsFine() =>
            Assert.Equal("SELECT a, b FROM t GROUP BY\n    a,\n    b|", Run("SELECT a, b FROM t GROUP BY |"));

        [Fact]
        public void DistinctAndTopAreNotItems() =>
            Assert.Equal("SELECT DISTINCT TOP 5 a, COUNT(*) FROM t GROUP BY\n    a|", Run("SELECT DISTINCT TOP 5 a, COUNT(*) FROM t GROUP BY |"));

        [Fact]
        public void ItemCountIsTheNumberOfExpressionsThatWillBeInserted()
        {
            const string sql = "SELECT a, b AS x, COUNT(*), a FROM t GROUP BY ";
            var found = GroupByExpander.Find(sql, sql.Length);
            Assert.Equal(2, found.Count);
            Assert.Equal(found.Count, found.Expressions.Count);
        }

        // ---- scalar functions, CASE, aliases ----

        [Theory]
        [InlineData("SELECT YEAR(o.OrderDate) AS OrderYear, COUNT(*) FROM o GROUP BY |", "YEAR(o.OrderDate)")]
        [InlineData("SELECT YEAR(o.OrderDate) OrderYear, COUNT(*) FROM o GROUP BY |", "YEAR(o.OrderDate)")]
        [InlineData("SELECT OrderYear = YEAR(o.OrderDate), COUNT(*) FROM o GROUP BY |", "YEAR(o.OrderDate)")]
        [InlineData("SELECT [Order Year] = YEAR(o.OrderDate), COUNT(*) FROM o GROUP BY |", "YEAR(o.OrderDate)")]
        [InlineData("SELECT YEAR(o.OrderDate) AS [Order, Year], COUNT(*) FROM o GROUP BY |", "YEAR(o.OrderDate)")]
        [InlineData("SELECT YEAR(o.OrderDate) AS 'y', COUNT(*) FROM o GROUP BY |", "YEAR(o.OrderDate)")]
        [InlineData("SELECT o.DepartmentId AS Dept, COUNT(*) FROM o GROUP BY |", "o.DepartmentId")]
        [InlineData("SELECT o.DepartmentId Dept, COUNT(*) FROM o GROUP BY |", "o.DepartmentId")]
        [InlineData("SELECT o.DepartmentId, COUNT(*) FROM o GROUP BY |", "o.DepartmentId")]
        [InlineData("SELECT o.Price * o.Qty total, COUNT(*) FROM o GROUP BY |", "o.Price * o.Qty")]
        [InlineData("SELECT [dbo].[o].[Dept Id], COUNT(*) FROM o GROUP BY |", "[dbo].[o].[Dept Id]")]
        [InlineData("SELECT CASE WHEN o.Status = 1 THEN N'Active' ELSE N'Other' END AS S, COUNT(*) FROM o GROUP BY |",
                    "CASE WHEN o.Status = 1 THEN N'Active' ELSE N'Other' END")]
        [InlineData("SELECT CASE WHEN o.Status = 1 THEN N'Active' ELSE N'Other' END S, COUNT(*) FROM o GROUP BY |",
                    "CASE WHEN o.Status = 1 THEN N'Active' ELSE N'Other' END")]
        [InlineData("SELECT ISNULL(NULLIF(LTRIM(RTRIM(a)), N''), b) AS v, COUNT(*) FROM t GROUP BY |", "ISNULL(NULLIF(LTRIM(RTRIM(a)), N''), b)")]
        [InlineData("SELECT CONCAT(a, N', ', b) AS Full, COUNT(*) FROM t GROUP BY |", "CONCAT(a, N', ', b)")]
        [InlineData("SELECT CONVERT(date, o.OrderDate) AS d, COUNT(*) FROM o GROUP BY |", "CONVERT(date, o.OrderDate)")]
        [InlineData("SELECT CAST(o.OrderDate AS date) d, COUNT(*) FROM o GROUP BY |", "CAST(o.OrderDate AS date)")]
        [InlineData("SELECT DATEADD(day, 1, o.OrderDate) AS d, COUNT(*) FROM o GROUP BY |", "DATEADD(day, 1, o.OrderDate)")]
        [InlineData("SELECT DATEPART(year, o.OrderDate), COUNT(*) FROM o GROUP BY |", "DATEPART(year, o.OrderDate)")]
        [InlineData("SELECT LEFT(o.Code, 2) AS c, COUNT(*) FROM o GROUP BY |", "LEFT(o.Code, 2)")]
        [InlineData("SELECT -o.Amount AS neg, COUNT(*) FROM o GROUP BY |", "-o.Amount")]
        [InlineData("SELECT (o.A + o.B) AS s, COUNT(*) FROM o GROUP BY |", "(o.A + o.B)")]
        [InlineData("SELECT CASE WHEN o.A IN (1, 2) THEN o.B END AS s, COUNT(*) FROM o GROUP BY |", "CASE WHEN o.A IN (1, 2) THEN o.B END")]
        public void TheSelectItemWithoutItsAlias(string sqlWithCaret, string expected) =>
            Assert.Equal(new[] { expected }, Items(sqlWithCaret));

        [Fact]
        public void TheOrderIsTheOrderOfTheSelectList()
        {
            Assert.Equal(new[] { "c", "a", "YEAR(d)", "b" }, Items("SELECT c, COUNT(*), a, YEAR(d) AS y, SUM(x), b FROM t GROUP BY |"));
        }

        // ---- what is not a grouping expression ----

        [Fact]
        public void ConstantsVariablesAndExpressionsOfOnlyThoseAreLeftOut()
        {
            const string sql =
                "SELECT 1 AS one, N'Total' AS label, @ReportDate AS dt, 1 + 2 AS three, NULL AS nothing, GETDATE() AS now, " +
                "CAST(@ReportDate AS date) AS d2, CONVERT(varchar(10), GETDATE(), 120) AS d3, DATEADD(day, 1, GETDATE()) AS d4, " +
                "N'x' + @Name AS n, CURRENT_TIMESTAMP AS ts, o.Id, COUNT(*) FROM o GROUP BY |";
            Assert.Equal(new[] { "o.Id" }, Items(sql));
        }

        [Fact]
        public void AColumnNextToTheConstantsStillCounts() =>
            Assert.Equal(new[] { "DATEDIFF(day, o.D, GETDATE())", "o.X + 1" },
                Items("SELECT DATEDIFF(day, o.D, GETDATE()) AS age, o.X + 1 AS y, COUNT(*) FROM o GROUP BY |"));

        [Theory]
        [InlineData("SELECT COUNT(*) AS n, SUM(o.Amount) AS total FROM o GROUP BY |")]
        [InlineData("SELECT COUNT(*), COALESCE(SUM(o.Amount), 0) FROM o GROUP BY |")]
        [InlineData("SELECT 1 AS one, N'x' AS label FROM o GROUP BY |")]
        [InlineData("SELECT COUNT(DISTINCT o.A) / COUNT(*) AS ratio, SUM(o.A) * 2 FROM o GROUP BY |")]
        [InlineData("SELECT CASE WHEN COUNT(*) > 1 THEN N'many' ELSE N'one' END AS k FROM o GROUP BY |")]
        [InlineData("SELECT STRING_AGG(o.Name, N', ') WITHIN GROUP (ORDER BY o.Name) AS names FROM o GROUP BY |")]
        public void NothingToInsertMeansTabDoesNothing(string sqlWithCaret) => Assert.Null(Run(sqlWithCaret));

        [Fact]
        public void AggregatesInsideScalarFunctionsAreStillAggregates() =>
            Assert.Equal(new[] { "o.A" }, Items("SELECT o.A, ISNULL(SUM(o.B), 0) AS s, MAX(o.C) - MIN(o.C) AS r FROM o GROUP BY |"));

        // ---- duplicates ----

        [Fact]
        public void OnlyTokenForTokenIdenticalExpressionsAreMerged()
        {
            Assert.Equal(new[] { "o.A", "o.a" }, Items("SELECT o.A, o.A AS A2, o.a, COUNT(*) FROM o GROUP BY |"));   // identifiers may be case sensitive
            Assert.Equal(new[] { "o.A" }, Items("SELECT o.A, o . A AS x, COUNT(*) FROM o GROUP BY |"));              // same tokens, different spacing
            Assert.Equal(new[] { "YEAR(d)", "year(d)" }, Items("SELECT YEAR(d), year(d), COUNT(*) FROM o GROUP BY |"));
            Assert.Equal(new[] { "CASE WHEN a = N'x' THEN 1 END", "CASE WHEN a = N'X' THEN 1 END" },
                Items("SELECT CASE WHEN a = N'x' THEN 1 END p, CASE WHEN a = N'X' THEN 1 END q, COUNT(*) FROM o GROUP BY |"));
            Assert.Equal(new[] { "CONCAT(a, N' ', b)", "CONCAT(a, N'  ', b)" },
                Items("SELECT CONCAT(a, N' ', b), CONCAT(a, N'  ', b), COUNT(*) FROM o GROUP BY |"));
        }

        // ---- commas that are not separators, comments, multi-line items ----

        [Fact]
        public void CommasInStringsFunctionsAndCommentsDoNotSplitItems()
        {
            const string sql =
                "SELECT a /* x, y */, -- c, d\n" +
                "       COALESCE(b, N'p, q', (c + 1)) AS v, [we,ird] , COUNT(*) FROM t GROUP BY |";
            Assert.Equal(new[] { "a", "COALESCE(b, N'p, q', (c + 1))", "[we,ird]" }, Items(sql));
        }

        [Fact]
        public void AMultiLineItemIsJoinedOnOneLine() =>
            Assert.Equal("SELECT CASE\n        WHEN a = 1 THEN 1\n        ELSE 0\n    END AS f, COUNT(*)\nFROM t\nGROUP BY\n    CASE WHEN a = 1 THEN 1 ELSE 0 END|",
                Run("SELECT CASE\n        WHEN a = 1 THEN 1\n        ELSE 0\n    END AS f, COUNT(*)\nFROM t\nGROUP BY |"));

        [Fact]
        public void ALineBreakInsideAStringLiteralIsKept() =>
            Assert.Equal(new[] { "CONCAT(x, N'a\nb')" }, Items("SELECT CONCAT(x,\n  N'a\nb') AS c, COUNT(*) FROM t GROUP BY |"));

        [Fact]
        public void AnItemWithACommentIsCopiedAsWritten() =>
            Assert.Equal(new[] { "CASE -- c\n WHEN a = 1 THEN 1 ELSE 0 END" },
                Items("SELECT CASE -- c\n WHEN a = 1 THEN 1 ELSE 0 END AS f, COUNT(*) FROM t GROUP BY |"));

        // ---- which query block ----

        [Fact]
        public void OnlyTheSelectOfTheBlockThatOwnsTheGroupByIsRead()
        {
            Assert.Equal("SELECT 1 AS x;\nSELECT a, b FROM t GROUP BY\n    a,\n    b|", Run("SELECT 1 AS x;\nSELECT a, b FROM t GROUP BY |"));
            Assert.Equal("SELECT p, q FROM y\nSELECT c, COUNT(*) FROM u GROUP BY\n    c|", Run("SELECT p, q FROM y\nSELECT c, COUNT(*) FROM u GROUP BY |"));
        }

        [Fact]
        public void CteDerivedTableAndSubqueryEachUseTheirOwnSelect()
        {
            // outer block, after a CTE
            Assert.Equal("WITH c AS (SELECT p, q FROM y)\nSELECT c.p, COUNT(*) FROM c GROUP BY\n    c.p|",
                Run("WITH c AS (SELECT p, q FROM y)\nSELECT c.p, COUNT(*) FROM c GROUP BY |"));
            // the CTE's own GROUP BY
            Assert.Equal("WITH c AS (SELECT p, COUNT(*) AS n FROM y GROUP BY\n    p|)\nSELECT * FROM c",
                Run("WITH c AS (SELECT p, COUNT(*) AS n FROM y GROUP BY |)\nSELECT * FROM c"));
            // outer block over a derived table
            Assert.Equal("SELECT d.k, SUM(d.n) FROM (SELECT k, n FROM y) d GROUP BY\n    d.k|",
                Run("SELECT d.k, SUM(d.n) FROM (SELECT k, n FROM y) d GROUP BY |"));
            // the derived table's own GROUP BY
            Assert.Equal("SELECT d.k FROM (SELECT k, SUM(n) AS s FROM y GROUP BY\n    k|) d",
                Run("SELECT d.k FROM (SELECT k, SUM(n) AS s FROM y GROUP BY |) d"));
            // a subquery in WHERE
            Assert.Equal("SELECT a, COUNT(*) FROM t WHERE a IN (SELECT k FROM z GROUP BY\n    k|)",
                Run("SELECT a, COUNT(*) FROM t WHERE a IN (SELECT k FROM z GROUP BY |)"));
            Assert.Equal("SELECT a, COUNT(*) FROM t WHERE a IN (SELECT k FROM z) GROUP BY\n    a|",
                Run("SELECT a, COUNT(*) FROM t WHERE a IN (SELECT k FROM z) GROUP BY |"));
        }

        [Fact]
        public void InsertSelectIsFine() =>
            Assert.Equal("INSERT INTO d (a, n) SELECT a, COUNT(*) FROM t GROUP BY\n    a|", Run("INSERT INTO d (a, n) SELECT a, COUNT(*) FROM t GROUP BY |"));

        [Fact]
        public void JoinsAndWhereBetweenSelectAndGroupByAreFine() =>
            Assert.Equal("SELECT o.A, c.B, COUNT(*) FROM o INNER JOIN c ON c.Id = o.Cid AND c.X IN (1, 2) WHERE o.Z = 1 GROUP BY\n    o.A,\n    c.B|",
                Run("SELECT o.A, c.B, COUNT(*) FROM o INNER JOIN c ON c.Id = o.Cid AND c.X IN (1, 2) WHERE o.Z = 1 GROUP BY |"));

        // ---- what comes after the list ----

        [Fact]
        public void HavingOrderByAndOptionStayWhereTheyWere()
        {
            Assert.Equal(L("SELECT a, COUNT(*) FROM t", "GROUP BY", "    a|", "HAVING COUNT(*) > 1", "ORDER BY a", "OPTION (RECOMPILE);"),
                Run(L("SELECT a, COUNT(*) FROM t", "GROUP BY |", "HAVING COUNT(*) > 1", "ORDER BY a", "OPTION (RECOMPILE);")));
            Assert.Equal(L("SELECT a, COUNT(*) FROM t", "GROUP BY", "    a|", "ORDER BY a"),
                Run(L("SELECT a, COUNT(*) FROM t", "GROUP BY |", "ORDER BY a")));
            Assert.Equal(L("SELECT a, COUNT(*) FROM t", "GROUP BY", "    a|", "OPTION (MAXDOP 1)"),
                Run(L("SELECT a, COUNT(*) FROM t", "GROUP BY |", "OPTION (MAXDOP 1)")));
        }

        [Fact]
        public void AClauseOnTheSameLineIsNotGluedToTheList()
        {
            Assert.Equal("SELECT a, COUNT(*) FROM t GROUP BY\n    a| HAVING COUNT(*) > 1", Run("SELECT a, COUNT(*) FROM t GROUP BY | HAVING COUNT(*) > 1"));
            // nothing between the caret and the keyword: a line break keeps them apart
            Assert.Equal("SELECT a, COUNT(*) FROM t GROUP BY\n    a|\nHAVING COUNT(*) > 1", Run("SELECT a, COUNT(*) FROM t GROUP BY |HAVING COUNT(*) > 1"));
            Assert.Equal("SELECT a, COUNT(*) FROM t GROUP BY\n    a|\nORDER BY a", Run("SELECT a, COUNT(*) FROM t GROUP BY |ORDER BY a"));
        }

        [Fact]
        public void SpacesAfterTheCaretAtTheEndOfTheLineAreNotLeftBehind() =>
            Assert.Equal("SELECT a, COUNT(*) FROM t GROUP BY\n    a|\nHAVING COUNT(*) > 1", Run("SELECT a, COUNT(*) FROM t GROUP BY |   \nHAVING COUNT(*) > 1"));

        [Fact]
        public void TheNextStatementIsNotTouched()
        {
            Assert.Equal("SELECT a, COUNT(*) FROM t GROUP BY\n    a|\nSELECT 2", Run("SELECT a, COUNT(*) FROM t GROUP BY |\nSELECT 2"));
            Assert.Equal("SELECT a, COUNT(*) FROM t GROUP BY\n    a|;\nSELECT 2", Run("SELECT a, COUNT(*) FROM t GROUP BY |;\nSELECT 2"));
            Assert.Equal("SELECT a, COUNT(*) FROM t GROUP BY\n    a|\nGO\nSELECT 2", Run("SELECT a, COUNT(*) FROM t GROUP BY |\nGO\nSELECT 2"));
            Assert.Equal("SELECT a, COUNT(*) FROM t GROUP BY\n    a|\nUPDATE u SET v = 1", Run("SELECT a, COUNT(*) FROM t GROUP BY |\nUPDATE u SET v = 1"));
        }

        // ---- layout follows the format settings ----

        [Fact]
        public void LeadingCommasLikeTheFormatter() =>
            Assert.Equal("SELECT a, b, COUNT(*) FROM t GROUP BY\n    a\n  , b|", Run("SELECT a, b, COUNT(*) FROM t GROUP BY |", CommaStyle.Leading));

        [Fact]
        public void IndentSizeOfTwoWithLeadingCommas() =>
            Assert.Equal("SELECT a, b FROM t GROUP BY\n  a\n, b|", Run("SELECT a, b FROM t GROUP BY |", CommaStyle.Leading, 2));

        [Fact]
        public void TheIndentOfTheGroupByLineIsKept()
        {
            const string sql = "    SELECT a, b, COUNT(*)\n    FROM t\n    GROUP BY |";
            Assert.Equal("    SELECT a, b, COUNT(*)\n    FROM t\n    GROUP BY\n        a,\n        b|", Run(sql));
            Assert.Equal("    SELECT a, b, COUNT(*)\n    FROM t\n    GROUP BY\n        a\n      , b|", Run(sql, CommaStyle.Leading));
            Assert.Equal("\tSELECT a, COUNT(*)\n\tFROM t\n\tGROUP BY\n\t    a|", Run("\tSELECT a, COUNT(*)\n\tFROM t\n\tGROUP BY |"));
        }

        [Fact]
        public void LineBreaksFollowTheTextWhenAskedTo()
        {
            const string sql = "SELECT a, b, COUNT(*)\r\nFROM t\r\nGROUP BY |\r\nHAVING COUNT(*) > 1";
            Assert.Equal("SELECT a, b, COUNT(*)\r\nFROM t\r\nGROUP BY\r\n    a,\r\n    b|\r\nHAVING COUNT(*) > 1", Run(sql, newLine: "\r\n"));
        }

        [Fact]
        public void TheEditIsOneContiguousReplacement()
        {
            const string sql = "SELECT a, b, COUNT(*) FROM t GROUP BY   ";
            var found = GroupByExpander.Find(sql, sql.Length);
            Assert.Equal(sql.IndexOf("BY") + 2, found.ReplaceStart);
            Assert.Equal(sql.Length, found.ReplaceEnd);
        }

        // ---- not in the first version: Tab does what it always did ----

        [Theory]
        [InlineData("SELECT *, COUNT(*) FROM t GROUP BY |")]
        [InlineData("SELECT a, * FROM t GROUP BY |")]
        [InlineData("SELECT o.*, COUNT(*) FROM o GROUP BY |")]
        [InlineData("SELECT a, ROW_NUMBER() OVER (ORDER BY b) AS rn FROM t GROUP BY |")]
        [InlineData("SELECT a, SUM(b) OVER (PARTITION BY c) AS s FROM t GROUP BY |")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY | UNION SELECT b, 1 FROM u")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY |\nUNION ALL\nSELECT b, 1 FROM u")]
        [InlineData("SELECT b, 1 FROM u UNION SELECT a, COUNT(*) FROM t GROUP BY |")]
        [InlineData("SELECT b, 1 FROM u UNION ALL SELECT a, COUNT(*) FROM t GROUP BY |")]
        [InlineData("SELECT b, 1 FROM u EXCEPT SELECT a, COUNT(*) FROM t GROUP BY |")]
        [InlineData("SELECT b, 1 FROM u INTERSECT SELECT a, COUNT(*) FROM t GROUP BY |")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY | WITH ROLLUP")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY | WITH CUBE")]
        [InlineData("SELECT a, GROUPING(a) AS g, COUNT(*) FROM t GROUP BY |")]
        [InlineData("SELECT a, (SELECT MAX(x) FROM u WHERE u.k = t.k) AS m FROM t GROUP BY |")]
        [InlineData("SELECT a, EXISTS (SELECT 1) AS e FROM t GROUP BY |")]
        [InlineData("SELECT a, FROM t GROUP BY |")]                 // empty item
        [InlineData("SELECT a, COUNT( FROM t GROUP BY |")]          // parenthesis left open
        [InlineData("SELECT GROUP BY |")]
        [InlineData("SELECT dbo.fn(a), COUNT(*) FROM t GROUP BY |")]        // schema-qualified function: scalar or aggregate?
        [InlineData("SELECT MyFunction(a), COUNT(*) FROM t GROUP BY |")]    // unknown function
        [InlineData("SELECT o.Price * SUM(o.Qty) AS x FROM o GROUP BY |")]
        [InlineData("SELECT o.A, o.Price * SUM(o.Qty) AS x FROM o GROUP BY |")]
        [InlineData("SELECT o.A, CASE WHEN SUM(o.Qty) > 1 THEN o.B ELSE o.C END AS x FROM o GROUP BY |")]
        [InlineData("SELECT o.A, x.value('(a)[1]', 'int') AS v FROM o GROUP BY |")]
        public void OutOfScopeGivesNothing(string sqlWithCaret) => Assert.Null(Run(sqlWithCaret));

        [Theory]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY a|")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY | a")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY |\n    a")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY a, |")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY ROLLUP|(a)")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY | ALL a")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY | GROUPING SETS ((a), ())")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP| BY ")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP B|Y ")]
        [InlineData("SELECT a, COUNT(*) FROM t ORDER BY |")]
        [InlineData("SELECT a, COUNT(*) FROM t WHERE |")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY a HAVING |")]
        [InlineData("SELECT a, COUNT(*) FROM t PARTITION BY |")]
        [InlineData("SELECT a, COUNT(*) FROM t GROUP BY /* x */ |")]
        public void AnExistingListOrAnotherPlaceIsLeftAlone(string sqlWithCaret) => Assert.Null(Run(sqlWithCaret));

        [Theory]
        [InlineData("SELECT a, COUNT(*) FROM t -- GROUP BY |")]
        [InlineData("SELECT a, COUNT(*) FROM t /* GROUP BY | */")]
        [InlineData("SELECT a, COUNT(*) FROM t WHERE x = N'GROUP BY |'")]
        [InlineData("SELECT a, COUNT(*) FROM t WHERE x = [GROUP BY |")]
        public void NotInsideAStringOrComment(string sqlWithCaret) => Assert.Null(Run(sqlWithCaret));

        [Fact]
        public void AGroupByWithNoSelectBeforeItIsLeftAlone()
        {
            Assert.Null(Run("GROUP BY |"));
            Assert.Null(Run("UPDATE t SET a = 1 GROUP BY |"));
            Assert.Null(Run("SELECT a FROM t;\nGROUP BY |"));
        }

        [Fact]
        public void ASelectInAnEarlierStatementIsNotTheOwner()
        {
            Assert.Null(Run("SELECT a, COUNT(*) FROM t;\nDECLARE @x int\nGROUP BY |"));
            Assert.Null(Run("SELECT a, COUNT(*) FROM t\nGO\nGROUP BY |"));
        }
    }
}
