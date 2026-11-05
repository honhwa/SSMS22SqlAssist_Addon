using System.Diagnostics;
using System.Linq;
using System.Text;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class InsertSelectMapTests
    {
        private const string Customers =
            "INSERT INTO dbo.Customers\n" +
            "(\n" +
            "    Code,\n" +
            "    Name,\n" +
            "    CreatedAt\n" +
            ")\n" +
            "SELECT\n" +
            "    s.CustomerCode,\n" +
            "    CONCAT(s.FirstName, N' ', s.LastName),\n" +
            "    GETDATE()\n" +
            "FROM dbo.SourceCustomers s;";

        /// <summary>The caret is written as '|'. Returns "Column=item;item" or null.</summary>
        private static string Highlight(string sqlWithCaret)
        {
            int caret = sqlWithCaret.IndexOf('|');
            string sql = sqlWithCaret.Remove(caret, 1);
            var h = InsertMaps.Find(sql, caret)?.At(caret);
            if (h == null) return null;
            return sql.Substring(h.Column.Start, h.Column.Length) + "=" +
                   string.Join(";", h.Values.Select(v => sql.Substring(v.Start, v.Length)));
        }

        private static string Caret(string sql, string marker, int offsetInMarker = 0, int occurrence = 0)
        {
            int at = -1;
            for (int i = 0; i <= occurrence; i++) at = sql.IndexOf(marker, at + 1, System.StringComparison.Ordinal);
            Assert.True(at >= 0, "marker not found: " + marker);
            return sql.Insert(at + offsetInMarker, "|");
        }

        // ---- both directions ----

        [Theory]
        [InlineData("Code,", 2, "Code=s.CustomerCode")]
        [InlineData("Name,", 0, "Name=CONCAT(s.FirstName, N' ', s.LastName)")]
        [InlineData("CreatedAt\n", 5, "CreatedAt=GETDATE()")]
        [InlineData("CreatedAt\n", 9, "CreatedAt=GETDATE()")]   // right after the name
        public void ColumnGivesTheSelectItemAtTheSamePosition(string marker, int offset, string expected) =>
            Assert.Equal(expected, Highlight(Caret(Customers, marker, offset)));

        [Theory]
        [InlineData("s.CustomerCode", 3, "Code=s.CustomerCode")]
        [InlineData("s.CustomerCode", 14, "Code=s.CustomerCode")]       // right after the item
        [InlineData("CONCAT(", 2, "Name=CONCAT(s.FirstName, N' ', s.LastName)")]
        [InlineData("s.FirstName", 3, "Name=CONCAT(s.FirstName, N' ', s.LastName)")]   // inside the function, between its own commas
        [InlineData("N' '", 2, "Name=CONCAT(s.FirstName, N' ', s.LastName)")]
        [InlineData("s.LastName", 4, "Name=CONCAT(s.FirstName, N' ', s.LastName)")]
        [InlineData("GETDATE()", 3, "CreatedAt=GETDATE()")]
        [InlineData("GETDATE()", 8, "CreatedAt=GETDATE()")]            // between the parentheses
        public void SelectItemGivesItsColumn(string marker, int offset, string expected) =>
            Assert.Equal(expected, Highlight(Caret(Customers, marker, offset)));

        [Theory]
        [InlineData("INSERT", 2)]
        [InlineData("SELECT\n", 2)]
        [InlineData("FROM dbo", 1)]
        [InlineData("SourceCustomers", 3)]
        [InlineData("dbo.Customers", 5)]
        public void KeywordsAndTheTargetAndFromClauseHighlightNothing(string marker, int offset) =>
            Assert.Null(Highlight(Caret(Customers, marker, offset)));

        [Fact]
        public void CaretRightAfterTheSelectKeywordIsNotAnItem() =>
            Assert.Null(Highlight("INSERT INTO t (a, b) SELECT| 1, 2"));

        [Fact]
        public void CaretBeforeFromOnTheNextLineIsNotTheLastItem() =>
            Assert.Null(Highlight("INSERT INTO t (a, b)\nSELECT 1, 2\n|FROM x"));

        [Fact]
        public void CaretAtTheEndOfTheLastItemOrAfterSpacesOnTheSameLineStillBelongsToIt()
        {
            Assert.Equal("b=2", Highlight("INSERT INTO t (a, b) SELECT 1, 2| FROM x"));
            Assert.Equal("b=2", Highlight("INSERT INTO t (a, b) SELECT 1, 2 | FROM x"));
            Assert.Equal("b=2", Highlight("INSERT INTO t (a, b) SELECT 1, 2|;"));
        }

        // ---- position, not text ----

        [Fact]
        public void PairsByPositionNotByName()
        {
            const string sql = "INSERT INTO t (a, b) SELECT b, a FROM s";
            Assert.Equal("a=b", Highlight(Caret(sql, "a, b", 0)));          // column a  -> first item "b"
            Assert.Equal("b=a", Highlight(Caret(sql, "b, a FROM", 4)));      // column... caret on item "a" -> column b
            Assert.Equal("a=b", Highlight(Caret(sql, "b, a FROM", 0)));      // item "b" -> column a
        }

        [Fact]
        public void TheSameTextTwiceIsStillTheItemUnderTheCaret()
        {
            const string sql = "INSERT INTO t (a, b, c) SELECT x, x, x FROM s";
            Assert.Equal("a=x", Highlight(Caret(sql, "x, x, x", 0)));
            Assert.Equal("b=x", Highlight(Caret(sql, "x, x, x", 3)));
            Assert.Equal("c=x", Highlight(Caret(sql, "x, x, x", 6)));
        }

        // ---- aliases ----

        [Theory]
        [InlineData("INSERT INTO t (a, b) SELECT s.x AS xx|, s.y AS yy FROM s", "a=s.x AS xx")]
        [InlineData("INSERT INTO t (a, b) SELECT s.x AS xx, s.y A|S yy FROM s", "b=s.y AS yy")]
        [InlineData("INSERT INTO t (a, b) SELECT xx = s.x|, yy = s.y FROM s", "a=xx = s.x")]
        [InlineData("INSERT INTO t (a, b) SELECT s.x xx, s.y| yy FROM s", "b=s.y yy")]
        [InlineData("INSERT INTO t (a, b) SELECT CAST(s.x AS int) AS [My, Alias]|, 2 FROM s", "a=CAST(s.x AS int) AS [My, Alias]")]
        public void AnAliasIsPartOfTheItem(string sqlWithCaret, string expected) =>
            Assert.Equal(expected, Highlight(sqlWithCaret));

        [Fact]
        public void ColumnWithAnAliasedItemMarksTheWholeItem() =>
            Assert.Equal("b=s.y AS yy", Highlight("INSERT INTO t (a, |b) SELECT s.x AS xx, s.y AS yy FROM s"));

        // ---- names, comments, layout, DISTINCT / TOP ----

        [Fact]
        public void SchemaBracketedNamesAndQuotedColumns()
        {
            const string sql = "INSERT [dbo].[My Table] ([Order Id], \"Qty,x\", [Sel]]ect])\nSELECT o.[Id,1], o.Qty, o.[Sel]]ect] FROM [dbo].[Src] o";
            int caret = sql.IndexOf("Qty,x") + 1;
            var h = InsertMaps.Find(sql, caret).At(caret);
            Assert.Equal("\"Qty,x\"", sql.Substring(h.Column.Start, h.Column.Length));
            Assert.Equal("o.Qty", sql.Substring(h.Values[0].Start, h.Values[0].Length));
            Assert.Equal("[Order Id]=o.[Id,1]", Highlight(Caret(sql, "[Id,1]", 2)));
        }

        [Fact]
        public void CommentsBetweenItemsAreSkippedAndAreNotHighlighted()
        {
            const string sql =
                "INSERT INTO t (a, -- first, second\n" +
                "   /* x, y */ b)\n" +
                "SELECT 1, -- note, with comma\n" +
                "   2 /* tail, */ FROM s";
            Assert.Equal("b=2", Highlight(sql.Insert(sql.IndexOf("2 /*"), "|")));
            Assert.Equal("a=1", Highlight(sql.Insert(sql.IndexOf("1,"), "|")));
            Assert.Null(Highlight(sql.Insert(sql.IndexOf("with comma") + 2, "|")));   // in a comment
            Assert.Null(Highlight(sql.Insert(sql.IndexOf("x, y") + 1, "|")));          // in a comment
            Assert.Null(Highlight(sql.Insert(sql.IndexOf("tail") + 1, "|")));          // in a comment
            Assert.Equal("a=1", Highlight(sql.Insert(sql.IndexOf("a,") + 1, "|")));
        }

        [Fact]
        public void ACommentInsideAnItemStaysInsideTheItem() =>
            Assert.Equal("a=CONCAT(x, /* c, d */ y)", Highlight("INSERT INTO t (a, b) SELECT CONCAT(|x, /* c, d */ y), 2 FROM s"));

        [Theory]
        [InlineData("INSERT INTO t (a, b) SELECT DISTINCT s.x|, s.y FROM s", "a=s.x")]
        [InlineData("INSERT INTO t (a, b) SELECT DISTINCT s.x, s.y| FROM s", "b=s.y")]
        [InlineData("INSERT INTO t (a, b) SELECT ALL s.x|, s.y FROM s", "a=s.x")]
        [InlineData("INSERT INTO t (a, b) SELECT TOP 10 s.x|, s.y FROM s", "a=s.x")]
        [InlineData("INSERT INTO t (a, b) SELECT TOP (10) PERCENT s.x, s.y| FROM s", "b=s.y")]
        [InlineData("INSERT INTO t (a, b) SELECT TOP (@n) WITH TIES s.x|, s.y FROM s ORDER BY s.z", "a=s.x")]
        [InlineData("INSERT INTO t (a, b) SELECT DISTINCT TOP 5 s.x, s.y| FROM s", "b=s.y")]
        [InlineData("INSERT INTO t (a, b) SELECT TOP (SELECT COUNT(*) FROM q) s.x|, s.y FROM s", "a=s.x")]
        public void DistinctAndTopAreNotItems(string sqlWithCaret, string expected) =>
            Assert.Equal(expected, Highlight(sqlWithCaret));

        [Fact]
        public void ColumnWithDistinctAndTop() =>
            Assert.Equal("b=s.y", Highlight("INSERT INTO t (a, |b) SELECT DISTINCT TOP (5) s.x, s.y FROM s"));

        [Fact]
        public void WorksWithoutAnyFromAndWithoutIntoKeyword()
        {
            Assert.Equal("b=2", Highlight("INSERT t (a, b) SELECT 1, 2|"));
            Assert.Equal("a=1", Highlight("INSERT t (|a, b) SELECT 1, 2"));
        }

        // ---- commas that are not separators ----

        [Theory]
        [InlineData("INSERT INTO t (a, b, c) SELECT N'x,|y', 2, 3", "a=N'x,y'")]
        [InlineData("INSERT INTO t (a, b) SELECT N'O''Brien, Pat|', 2", "a=N'O''Brien, Pat'")]
        [InlineData("INSERT INTO t (a, b) SELECT N'O''Brien', 2|", "b=2")]
        [InlineData("INSERT INTO t (a, b) SELECT COALESCE(x, y, z|), 2 FROM s", "a=COALESCE(x, y, z)")]
        [InlineData("INSERT INTO t (a, b) SELECT 1, (SELECT TOP 1 x, y| FROM q WHERE z IN (1, 2)) FROM s", "b=(SELECT TOP 1 x, y FROM q WHERE z IN (1, 2))")]
        [InlineData("INSERT INTO t (a, b) SELECT CASE WHEN x = 1 THEN 'a' ELSE 'b|' END, 2 FROM s", "a=CASE WHEN x = 1 THEN 'a' ELSE 'b' END")]
        [InlineData("INSERT INTO t (a, b) SELECT CASE WHEN x IN (1, 2) THEN COALESCE(p, q) ELSE r END AS k, 2| FROM s", "b=2")]
        [InlineData("INSERT INTO t (a, b) SELECT ISNULL(NULLIF(LTRIM(RTRIM(x)), ''), y|), 2 FROM s", "a=ISNULL(NULLIF(LTRIM(RTRIM(x)), ''), y)")]
        [InlineData("INSERT INTO t (a, b) SELECT -1.5|, 0x1F", "a=-1.5")]
        [InlineData("INSERT INTO t (a, b) SELECT @x + @y|, [col,x] FROM s", "a=@x + @y")]
        [InlineData("INSERT INTO t (a, b) SELECT x * y|, 2 FROM s", "a=x * y")]                // multiplication is not a star item
        [InlineData("INSERT INTO t (a, b) SELECT COUNT(*)|, 2 FROM s", "a=COUNT(*)")]
        [InlineData("INSERT INTO t (a, b) SELECT CONVERT(varchar(10), d, 120)|, 2 FROM s", "a=CONVERT(varchar(10), d, 120)")]
        [InlineData("INSERT INTO t (a, b) SELECT SUBSTRING(x, 1, 2)|, TRIM('a' FROM y) FROM s", "a=SUBSTRING(x, 1, 2)")]
        [InlineData("INSERT INTO t (a, b) SELECT SUBSTRING(x, 1, 2), TRIM('a' FROM y|) FROM s", "b=TRIM('a' FROM y)")]   // FROM inside parentheses
        [InlineData("INSERT INTO t (a, b) SELECT (SELECT MAX(v) FROM w WHERE w.k = s.k)|, 2 FROM s", "a=(SELECT MAX(v) FROM w WHERE w.k = s.k)")]
        public void CommasInsideItemsDoNotSplitThem(string sqlWithCaret, string expected) =>
            Assert.Equal(expected, Highlight(sqlWithCaret));

        [Fact]
        public void ColumnGivesTheWholeNestedExpression() =>
            Assert.Equal("b=CASE WHEN x IN (1, 2) THEN COALESCE(p, q) ELSE r END",
                Highlight("INSERT INTO t (a, |b) SELECT s.a, CASE WHEN x IN (1, 2) THEN COALESCE(p, q) ELSE r END FROM s"));

        // ---- the outer SELECT only ----

        [Fact]
        public void CteSelectIsNotTheInsertSelect()
        {
            const string sql =
                "WITH x AS (SELECT p, q FROM y)\n" +
                "INSERT INTO t (a, b)\n" +
                "SELECT x.p, x.q FROM x";
            Assert.Null(Highlight(Caret(sql, "p, q", 0)));                 // inside the CTE
            Assert.Null(Highlight(Caret(sql, "q FROM y", 0)));
            Assert.Equal("a=x.p", Highlight(Caret(sql, "x.p", 2)));
            Assert.Equal("b=x.q", Highlight(Caret(sql, "x.q", 2)));
            Assert.Equal("b=x.q", Highlight(Caret(sql, "b)", 0)));
        }

        [Fact]
        public void DerivedTableAndSubqueriesInFromAndWhereAreNotItems()
        {
            const string sql =
                "INSERT INTO t (a, b)\n" +
                "SELECT d.m, d.n\n" +
                "FROM (SELECT 1 AS m, 2 AS n) d\n" +
                "WHERE d.m IN (SELECT k FROM z)";
            Assert.Null(Highlight(Caret(sql, "1 AS m", 0)));
            Assert.Null(Highlight(Caret(sql, "2 AS n", 0)));
            Assert.Null(Highlight(Caret(sql, "k FROM z", 0)));
            Assert.Null(Highlight(Caret(sql, "d.m IN", 1)));
            Assert.Equal("a=d.m", Highlight(Caret(sql, "d.m,", 2)));
            Assert.Equal("b=d.n", Highlight(Caret(sql, "d.n\n", 2)));
        }

        [Fact]
        public void ASelectInsideAnItemBelongsToThatItem()
        {
            const string sql = "INSERT INTO t (a, b) SELECT s.x, (SELECT TOP 1 w.v, w.k FROM w) FROM s";
            Assert.Equal("b=(SELECT TOP 1 w.v, w.k FROM w)", Highlight(Caret(sql, "w.k", 2)));
        }

        [Fact]
        public void WhereOrderGroupHavingAndOptionAfterTheListEndIt()
        {
            const string sql =
                "INSERT INTO t (a, b) SELECT s.x, s.y FROM s WHERE s.p = 1 AND s.q IN (1, 2) GROUP BY s.x, s.y HAVING COUNT(*) > 1 ORDER BY s.x, s.y OPTION (RECOMPILE)";
            Assert.Equal("a=s.x", Highlight(Caret(sql, "s.x, s.y FROM", 2)));
            Assert.Null(Highlight(Caret(sql, "GROUP BY s.x, s.y", 12)));
            Assert.Null(Highlight(Caret(sql, "ORDER BY s.x, s.y", 12)));
            Assert.Null(Highlight(Caret(sql, "s.q IN", 2)));
        }

        [Fact]
        public void TableHintsAndOtherWithInFromDoNotEndTheStatement() =>
            Assert.Equal("b=s.y", Highlight("INSERT INTO t (a, b) SELECT s.x, s.y| FROM s WITH (NOLOCK) WHERE s.k = 1"));

        // ---- several INSERTs in one script ----

        [Fact]
        public void OnlyTheInsertTheCaretIsInIsUsed()
        {
            const string sql =
                "INSERT INTO a (x, y) SELECT 1, 2;\n" +
                "SELECT 9;\n" +
                "INSERT INTO b (p, q) VALUES (3, 4), (5, 6);\n" +
                "INSERT INTO c (m, n) SELECT s.m, s.n FROM s\n" +
                "INSERT INTO d (u) SELECT 7";
            Assert.Equal("y=2", Highlight(Caret(sql, "2;", 0)));
            Assert.Equal("x=1", Highlight(Caret(sql, "x,", 0)));
            Assert.Equal("q=4;6", Highlight(Caret(sql, "q)", 0)));        // VALUES keeps working beside SELECT
            Assert.Equal("p=5", Highlight(Caret(sql, "5", 0)));
            Assert.Equal("n=s.n", Highlight(Caret(sql, "s.n", 2)));
            Assert.Equal("m=s.m", Highlight(Caret(sql, "m,", 0)));
            Assert.Equal("u=7", Highlight(Caret(sql, "7", 1)));
            Assert.Null(Highlight(Caret(sql, "SELECT 9", 2)));
            Assert.Null(Highlight(Caret(sql, "FROM s", 2)));
        }

        [Fact]
        public void AnInsertAfterAnUnsupportedOneIsStillMapped()
        {
            const string sql =
                "INSERT INTO a (x) SELECT 1 UNION ALL SELECT 2;\n" +
                "INSERT INTO b (y, z) SELECT 3, 4";
            Assert.Null(Highlight(Caret(sql, "1 UNION", 0)));
            Assert.Equal("z=4", Highlight(Caret(sql, "4", 1)));
        }

        // ---- not understood: nothing is highlighted ----

        [Theory]
        [InlineData("INSERT INTO t (a, b) SELECT 1|, 2 UNION SELECT 3, 4")]
        [InlineData("INSERT INTO t (a, b) SELECT 1, 2| UNION ALL SELECT 3, 4")]
        [InlineData("INSERT INTO t (a, b) SELECT s.x|, s.y FROM s UNION SELECT 3, 4")]
        [InlineData("INSERT INTO t (a, b) SELECT s.x|, s.y FROM s WHERE 1 = 1 EXCEPT SELECT p, q FROM z")]
        [InlineData("INSERT INTO t (a, b) SELECT s.x|, s.y FROM s INTERSECT SELECT p, q FROM z")]
        [InlineData("INSERT INTO t (a, b) EXEC p 1|, 2")]
        [InlineData("INSERT INTO t (a, b) OUTPUT inserted.a SELECT 1|, 2")]
        [InlineData("INSERT INTO t (a, b) (SELECT 1|, 2)")]
        [InlineData("INSERT INTO t SELECT 1|, 2")]               // no column list
        [InlineData("INSERT INTO t (a, b + 1) SELECT 1|, 2")]    // not a plain name
        [InlineData("INSERT INTO t (a, b SELECT 1|, 2")]         // column list still open
        [InlineData("INSERT INTO t (a, b) SELECT TOP|")]         // head still being typed
        [InlineData("INSERT INTO t (a, b) SELECT|")]
        public void WhatIsNotUnderstoodHighlightsNothing(string sqlWithCaret) => Assert.Null(Highlight(sqlWithCaret));

        [Fact]
        public void UnionAfterAWhereInsideParenthesesIsFine() =>
            Assert.Equal("a=s.x", Highlight("INSERT INTO t (a, b) SELECT s.x|, s.y FROM s WHERE s.k IN (SELECT 1 UNION SELECT 2)"));

        [Fact]
        public void UnionColumnStillClearsWhenCaretIsOnAColumn() =>
            Assert.Null(Highlight("INSERT INTO t (|a, b) SELECT 1, 2 UNION SELECT 3, 4"));

        // ---- * is not expanded ----

        [Theory]
        [InlineData("INSERT INTO t (a, b) SELECT *| FROM s")]
        [InlineData("INSERT INTO t (|a, b) SELECT * FROM s")]
        [InlineData("INSERT INTO t (a, |b) SELECT s.* FROM s")]
        [InlineData("INSERT INTO t (a, b) SELECT s.*| FROM s")]
        [InlineData("INSERT INTO t (a, b) SELECT [dbo].[s].*| FROM s")]
        public void StarGivesNothing(string sqlWithCaret) => Assert.Null(Highlight(sqlWithCaret));

        [Fact]
        public void OnlyItemsBeforeTheFirstStarAreStillCertain()
        {
            const string sql = "INSERT INTO t (a, b, c, d) SELECT s.x, s.*, s.z, 4 FROM s";
            Assert.Equal("a=s.x", Highlight(Caret(sql, "s.x", 2)));
            Assert.Equal("a=s.x", Highlight(Caret(sql, "a,", 0)));
            Assert.Null(Highlight(Caret(sql, "s.*", 3)));               // the star itself
            Assert.Null(Highlight(Caret(sql, "s.z", 2)));               // position unknown after a star
            Assert.Null(Highlight(Caret(sql, "4 FROM", 0)));
            Assert.Null(Highlight(Caret(sql, "b,", 0)));
            Assert.Null(Highlight(Caret(sql, "c,", 0)));
            Assert.Null(Highlight(Caret(sql, "d)", 0)));
        }

        // ---- different numbers of columns and items ----

        [Fact]
        public void ColumnsAndItemsOfDifferentLengthOnlyPairWhatMatches()
        {
            // Three items for two columns: the third has nothing to pair with.
            Assert.Equal("b=2", Highlight("INSERT INTO t (a, b) SELECT 1, 2|, 3"));
            Assert.Null(Highlight("INSERT INTO t (a, b) SELECT 1, 2, 3|"));
            // Two items for three columns: the column with no item finds none.
            Assert.Equal("b=2", Highlight("INSERT INTO t (a, b, c) SELECT 1, 2|"));
            Assert.Null(Highlight("INSERT INTO t (a, b, c|) SELECT 1, 2"));
            Assert.Equal("b=2", Highlight("INSERT INTO t (a, |b, c) SELECT 1, 2"));
        }

        [Theory]
        [InlineData("INSERT INTO t (a, b) SELECT 1, N'x|", "b=N'x")]                       // string still open
        [InlineData("INSERT INTO t (a, b) SELECT 1, 2|", "b=2")]
        [InlineData("INSERT INTO t (a, b) SELECT 1|", "a=1")]
        [InlineData("INSERT INTO t (a, b) SELECT 1,| ", "b=")]                           // item just started: the column, no value yet
        [InlineData("INSERT INTO t (a, b) SELECT 1, |", "b=")]
        [InlineData("INSERT INTO t (a, b) SELECT 1, CONVERT(int, |", "b=CONVERT(int,")]   // parenthesis still open
        [InlineData("INSERT INTO t (a, b) SELECT 1, 2| FROM", "b=2")]
        [InlineData("INSERT INTO t (a, b) SELECT 1, 2| FROM s WHERE", "b=2")]
        public void StatementsStillBeingTypedKeepWhatIsCertain(string sqlWithCaret, string expected) =>
            Assert.Equal(expected, Highlight(sqlWithCaret));

        [Fact]
        public void AnEmptyItemNamesItsColumnButMarksNoValue()
        {
            const string sql = "INSERT INTO t (a, b, c) SELECT 1, , 3 FROM s";
            int caret = sql.IndexOf(", ,") + 2;
            var h = InsertMaps.Find(sql, caret).At(caret);
            Assert.Equal("b", sql.Substring(h.Column.Start, h.Column.Length));
            Assert.Empty(h.Values);
        }

        [Fact]
        public void ASelectLeftOpenDoesNotSwallowTheNextStatement()
        {
            const string sql = "INSERT INTO t (a, b) SELECT 1, \nSELECT * FROM x WHERE y = 2\n";
            Assert.Null(Highlight(Caret(sql, "FROM", 1)));
            Assert.Null(Highlight(Caret(sql, "y = 2", 1)));
            Assert.Equal("a=1", Highlight(Caret(sql, "1,", 1)));
        }

        [Theory]
        [InlineData("INSERT INTO t (a, b) SELECT 1, (2 + \nUPDATE u SET v = 3", "SET v")]
        [InlineData("INSERT INTO t (a, b) SELECT 1, 2 FROM s\nDELETE FROM u WHERE v = 3", "v = 3")]
        [InlineData("INSERT INTO t (a, b) SELECT 1, 2 FROM s;\nSELECT p, q FROM z", "q FROM z")]
        [InlineData("INSERT INTO t (a, b) SELECT 1, 2 FROM s\nGO\nSELECT p, q FROM z", "q FROM z")]
        [InlineData("INSERT INTO t (a, b) SELECT 1, 2 FROM s\nSELECT p, q FROM z", "q FROM z")]
        [InlineData("INSERT INTO t (a, b) SELECT 1, 2 FROM s\nWITH c AS (SELECT p, q FROM z) SELECT * FROM c", "q FROM z")]
        public void TheNextStatementIsNeverPartOfTheInsert(string sql, string marker) =>
            Assert.Null(Highlight(Caret(sql, marker, 1)));

        [Fact]
        public void CaretOnlyInTheStatementItsInsertStartsAfter()
        {
            const string sql = "INSERT INTO t (a, b) SELECT 1, 2;\n-- INSERT INTO q (z) SELECT 9\n";
            Assert.Null(Highlight(Caret(sql, "9", 0)));
        }

        [Fact]
        public void AnInsertWordInsideAStringOrCommentIsNotAnInsert()
        {
            const string sql = "INSERT INTO t (a, b) SELECT N'INSERT INTO z (k) SELECT 5', 2 FROM s";
            Assert.Equal("a=N'INSERT INTO z (k) SELECT 5'", Highlight(Caret(sql, "z (k)", 0)));
        }

        // ---- VALUES keeps working through the shared entry point ----

        [Fact]
        public void InsertValuesStillMapsThroughTheSharedEntryPoint()
        {
            const string sql = "INSERT INTO t (Id, Code) VALUES (1, N'A'), (2, N'B')";
            Assert.Equal("Code=N'B'", Highlight(Caret(sql, "N'B'", 2)));
            Assert.Equal("Id=1;2", Highlight(Caret(sql, "Id,", 1)));
        }

        // ---- Parse / structure ----

        [Fact]
        public void ParseGivesTheStatementWhateverTheCaret()
        {
            const string sql = "SELECT 1;\nINSERT INTO a (x, y) SELECT 1, 2 FROM s;\nINSERT INTO b (p) SELECT 5";
            int first = sql.IndexOf("INSERT INTO a");
            var map = InsertMaps.Parse(sql, first);
            Assert.NotNull(map);
            Assert.IsType<InsertSelectMap>(map);
            Assert.Equal(first, map.StatementStart);
            Assert.Equal("y", sql.Substring(map.At(sql.IndexOf("2 FROM")).Column.Start, 1));
            Assert.Null(map.At(sql.IndexOf("5") + 1));          // belongs to the next INSERT
            Assert.Null(InsertMaps.Parse(sql, first + 1));      // not at an INSERT keyword
            Assert.Null(InsertMaps.Parse(sql, 0));
        }

        [Fact]
        public void ParseOfAValuesInsertGivesTheValuesMap()
        {
            const string sql = "INSERT INTO a (x) VALUES (1)";
            Assert.IsType<InsertValueMap>(InsertMaps.Parse(sql, 0));
        }

        [Fact]
        public void StatementEndStopsBeforeTheNextStatement()
        {
            const string sql = "INSERT INTO a (x) SELECT 1 FROM s;\nSELECT 2";
            var map = InsertMaps.Parse(sql, 0);
            Assert.Equal(sql.IndexOf(';'), map.StatementEnd);
        }

        [Fact]
        public void AStatementCutOffByTheEndOfTheTextReachesTheEnd()
        {
            const string sql = "INSERT INTO a (x) SELECT 1 FROM s WHERE y IN (1, 2";
            Assert.Equal(sql.Length, InsertMaps.Parse(sql, 0).StatementEnd);
        }

        [Fact]
        public void LargeSelectListIsReadOnceAndLookedUpQuickly()
        {
            var cols = new StringBuilder("INSERT INTO dbo.Big (");
            var items = new StringBuilder("SELECT\n");
            for (int i = 0; i < 400; i++)
            {
                cols.Append(i == 0 ? "" : ", ").Append("C").Append(i);
                items.Append(i == 0 ? "    " : "   ,").Append("COALESCE(s.V").Append(i).Append(", N'd, ").Append(i).Append("') AS A").Append(i).Append('\n');
            }
            string sql = cols + ")\n" + items + "FROM dbo.S s";
            int caret = sql.IndexOf("N'd, 277'") + 3;

            var watch = Stopwatch.StartNew();
            var map = InsertMaps.Find(sql, caret);
            var h = map.At(caret);
            watch.Stop();

            Assert.Equal("C277", sql.Substring(h.Column.Start, h.Column.Length));
            Assert.Equal("COALESCE(s.V277, N'd, 277') AS A277", sql.Substring(h.Values[0].Start, h.Values[0].Length));
            Assert.True(watch.ElapsedMilliseconds < 1000, "took " + watch.ElapsedMilliseconds + " ms");
            watch.Restart();
            for (int i = 0; i < 1000; i++) map.At(caret + i);
            Assert.True(watch.ElapsedMilliseconds < 500, "At took " + watch.ElapsedMilliseconds + " ms for 1000 lookups");
        }
    }
}
