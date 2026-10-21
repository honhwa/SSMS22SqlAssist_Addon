using System.Linq;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class StatementScannerTests
    {
        private static StatementKind[] Kinds(string sql) =>
            SqlStatementScanner.Scan(sql).Batches.SelectMany(b => b.Statements).Select(s => s.Kind).ToArray();

        private static string[] Texts(string sql)
        {
            var scan = SqlStatementScanner.Scan(sql);
            return scan.Batches.SelectMany(b => b.Statements).Select(scan.TextOf).ToArray();
        }

        [Fact]
        public void SplitsAtSemicolons()
        {
            Assert.Equal(new[] { "SELECT 1;", "SELECT 2;" }, Texts("SELECT 1; SELECT 2;"));
        }

        [Fact]
        public void SplitsWithoutSemicolonsWhenAStatementKeywordStarts()
        {
            Assert.Equal(new[] { "SELECT 1", "SELECT 2" }, Texts("SELECT 1 SELECT 2"));
            Assert.Equal(
                new[] { StatementKind.Declare, StatementKind.Set, StatementKind.Select },
                Kinds("DECLARE @a INT SET @a = 1 SELECT @a"));
        }

        [Fact]
        public void KindsOfTheFirstKeyword()
        {
            Assert.Equal(StatementKind.Select, Kinds("select 1").Single());
            Assert.Equal(StatementKind.Insert, Kinds("INSERT INTO t VALUES (1)").Single());
            Assert.Equal(StatementKind.Update, Kinds("UPDATE t SET a = 1").Single());
            Assert.Equal(StatementKind.Delete, Kinds("DELETE FROM t").Single());
            Assert.Equal(StatementKind.Merge, Kinds("MERGE t USING s ON 1 = 1 WHEN MATCHED THEN DELETE;").Single());
            Assert.Equal(StatementKind.Truncate, Kinds("TRUNCATE TABLE t").Single());
            Assert.Equal(StatementKind.Drop, Kinds("DROP TABLE t").Single());
            Assert.Equal(StatementKind.Create, Kinds("CREATE TABLE t (a INT)").Single());
            Assert.Equal(StatementKind.Alter, Kinds("ALTER TABLE t ADD b INT").Single());
            Assert.Equal(StatementKind.Exec, Kinds("EXEC sp_who").Single());
            Assert.Equal(StatementKind.Exec, Kinds("execute sp_who").Single());
            Assert.Equal(StatementKind.Other, Kinds("GRANT SELECT ON t TO u").Single());
        }

        [Fact]
        public void ClausesOfOneStatementStayTogether()
        {
            Assert.Single(Kinds("SELECT a FROM t WITH (NOLOCK) WHERE a IN (SELECT b FROM u) ORDER BY a"));
            Assert.Single(Kinds("SELECT 1 UNION ALL SELECT 2 EXCEPT SELECT 3 INTERSECT SELECT 4"));
            Assert.Single(Kinds("INSERT INTO t (a) SELECT a FROM u"));
            Assert.Single(Kinds("INSERT INTO t EXEC dbo.p"));
            Assert.Single(Kinds("UPDATE t SET a = 1 FROM t JOIN u ON u.id = t.id WHERE t.x = 2"));
            Assert.Single(Kinds("DELETE t FROM t JOIN u ON u.id = t.id WHERE u.x = 2"));
            Assert.Single(Kinds("SELECT CASE WHEN a = 1 THEN 'x' ELSE 'y' END FROM t"));
            Assert.Single(Kinds("SELECT a INTO #t FROM u"));
        }

        [Fact]
        public void CteBelongsToItsStatement()
        {
            var scan = SqlStatementScanner.Scan("WITH c AS (SELECT 1 AS x), d AS (SELECT 2 AS y) SELECT * FROM c, d");
            var statement = scan.Batches.Single().Statements.Single();
            Assert.Equal(StatementKind.Select, statement.Kind);
            Assert.True(statement.HasCte);

            var delete = SqlStatementScanner.Scan("WITH c AS (SELECT 1 AS x) DELETE FROM c WHERE x = 1").Batches.Single().Statements.Single();
            Assert.Equal(StatementKind.Delete, delete.Kind);
            Assert.True(delete.HasCte);
        }

        [Fact]
        public void WithThatIsAHintOrOptionDoesNotStartAStatement()
        {
            Assert.Single(Kinds("SELECT * FROM t WITH (NOLOCK)"));
            Assert.Equal(new[] { StatementKind.Create, StatementKind.Select }, Kinds("CREATE VIEW v WITH SCHEMABINDING AS SELECT 1 AS a"));
            Assert.Equal(new[] { StatementKind.Create, StatementKind.Select }, Kinds("CREATE PROC p WITH RECOMPILE, EXECUTE AS OWNER AS SELECT 1"));
            Assert.Single(Kinds("SELECT a FROM t GROUP BY a WITH ROLLUP"));
            Assert.Single(Kinds("SELECT TOP 5 WITH TIES a FROM t ORDER BY a"));
        }

        [Fact]
        public void TriggerEventKeywordsAndForeignKeyActionsAreNotStatements()
        {
            Assert.Equal(new[] { StatementKind.Create, StatementKind.Select },
                Kinds("CREATE TRIGGER x ON t AFTER INSERT, UPDATE, DELETE AS SELECT 1"));
            Assert.Single(Kinds("ALTER TABLE t ADD CONSTRAINT fk FOREIGN KEY (a) REFERENCES u (b) ON DELETE CASCADE ON UPDATE NO ACTION"));
            Assert.Single(Kinds("ALTER TABLE t DROP COLUMN c"));
            Assert.Equal(StatementKind.Other, Kinds("UPDATE STATISTICS dbo.t").Single());
        }

        [Fact]
        public void MergeActionsStayInTheMerge()
        {
            var kinds = Kinds("MERGE t AS x USING s ON x.id = s.id WHEN MATCHED THEN UPDATE SET a = s.a " +
                              "WHEN NOT MATCHED THEN INSERT (a) VALUES (s.a) WHEN NOT MATCHED BY SOURCE THEN DELETE;");
            Assert.Single(kinds);
        }

        [Fact]
        public void ControlFlowHeadersAreStatementsOfTheirOwn()
        {
            Assert.Equal(
                new[] { StatementKind.If, StatementKind.Select, StatementKind.Else, StatementKind.Select },
                Kinds("IF @a = 1 SELECT 1 ELSE SELECT 2"));
            Assert.Equal(
                new[] { StatementKind.If, StatementKind.Begin, StatementKind.Update, StatementKind.End },
                Kinds("IF EXISTS (SELECT 1 FROM t) BEGIN UPDATE t SET a = 1 END"));
            Assert.Equal(
                new[] { StatementKind.If, StatementKind.Print, StatementKind.Else, StatementKind.Print },
                Kinds("IF @a = 1 PRINT 'a' ELSE PRINT 'b'"));
            Assert.Equal(
                new[] { StatementKind.While, StatementKind.Begin, StatementKind.Set, StatementKind.End },
                Kinds("WHILE @i < 3 BEGIN SET @i += 1 END"));
        }

        [Fact]
        public void ElseIfIsOneHeader()
        {
            Assert.Equal(
                new[] { StatementKind.If, StatementKind.Select, StatementKind.Else, StatementKind.Select, StatementKind.Else, StatementKind.Select },
                Kinds("IF @a = 1 SELECT 1 ELSE IF @a = 2 SELECT 2 ELSE SELECT 3"));
            Assert.Equal("ELSE IF @a = 2", Texts("IF @a = 1 SELECT 1 ELSE IF @a = 2 SELECT 2")[2]);
        }

        [Fact]
        public void TriggerFunctionUpdateInAnIfIsNotAStatement()
        {
            Assert.Equal(new[] { StatementKind.If, StatementKind.Print }, Kinds("IF UPDATE(a) PRINT 'x'"));
        }

        [Fact]
        public void TryCatchBlocks()
        {
            Assert.Equal(
                new[] { StatementKind.Begin, StatementKind.Select, StatementKind.End, StatementKind.Begin, StatementKind.Select, StatementKind.End },
                Kinds("BEGIN TRY SELECT 1 END TRY BEGIN CATCH SELECT 2 END CATCH"));
            Assert.Equal("END TRY", Texts("BEGIN TRY SELECT 1 END TRY")[2]);
        }

        [Fact]
        public void CaseEndIsNotAnEndStatement()
        {
            Assert.Equal(new[] { StatementKind.Select, StatementKind.Select },
                Kinds("SELECT CASE WHEN 1 = 1 THEN CASE WHEN 2 = 2 THEN 1 END ELSE 0 END SELECT 2"));
        }

        [Fact]
        public void ProcedureHeaderAndBody()
        {
            var kinds = Kinds("CREATE PROCEDURE dbo.p @a INT AS BEGIN SET NOCOUNT ON SELECT @a END");
            Assert.Equal(new[] { StatementKind.Create, StatementKind.Begin, StatementKind.Set, StatementKind.Select, StatementKind.End }, kinds);
        }

        [Fact]
        public void FetchAfterOffsetIsPartOfTheQuery()
        {
            Assert.Single(Kinds("SELECT a FROM t ORDER BY a OFFSET 5 ROWS FETCH NEXT 10 ROWS ONLY"));
            Assert.Equal(new[] { StatementKind.Other, StatementKind.Other }, Kinds("FETCH NEXT FROM c INTO @a CLOSE c"));
        }

        [Fact]
        public void GoLinesSplitBatches()
        {
            var scan = SqlStatementScanner.Scan("SELECT 1\nGO\nSELECT 2\ngo 3\nSELECT 3 -- end\n");
            Assert.Equal(3, scan.Batches.Count);
            Assert.True(scan.Batches[0].HasGo);
            Assert.True(scan.Batches[1].HasGo);
            Assert.False(scan.Batches[2].HasGo);
            Assert.Equal("SELECT 1", scan.TextOf(scan.Batches[0].Statements.Single()));
            Assert.Equal("GO", scan.Tokens[scan.Batches[0].GoToken].Text);
            Assert.Equal(scan.Batches[1].GoCountToken, scan.Batches[1].GoToken + 1);
            Assert.Equal("3", scan.Tokens[scan.Batches[1].GoCountToken].Text);
            Assert.Equal(-1, scan.Batches[0].GoCountToken);
        }

        [Fact]
        public void GoNeedsALineOfItsOwn()
        {
            Assert.Single(SqlStatementScanner.Scan("SELECT 1 GO").Batches);
            Assert.Single(SqlStatementScanner.Scan("SELECT 'a\nGO\nb'").Batches);
            Assert.Single(SqlStatementScanner.Scan("/* a\nGO\nb */ SELECT 1").Batches);
            Assert.Single(SqlStatementScanner.Scan("SELECT a AS go FROM t").Batches);
            Assert.Equal(2, SqlStatementScanner.Scan("SELECT 1\n  GO  -- done\nSELECT 2").Batches.Count);
        }

        [Fact]
        public void CommentsGoWithTheStatementTheyAreNextTo()
        {
            var scan = SqlStatementScanner.Scan("-- first\nSELECT 1 -- one\n-- second\nSELECT 2 /* two */");
            var statements = scan.Batches.Single().Statements;
            Assert.Equal("-- first\nSELECT 1 -- one", scan.TextOf(statements[0]));
            Assert.Equal("-- second\nSELECT 2 /* two */", scan.TextOf(statements[1]));
        }

        [Fact]
        public void CommentOnlyBatchIsAStatementWithNoKeyword()
        {
            var scan = SqlStatementScanner.Scan("-- just a note");
            var statement = scan.Batches.Single().Statements.Single();
            Assert.Equal(StatementKind.Other, statement.Kind);
            Assert.Equal(-1, statement.FirstSignificant);
        }

        [Fact]
        public void MergesOperatorsNumbersAndMoney()
        {
            var tokens = SqlStatementScanner.Scan("a <= 1.5E-3 AND b <> $5 AND c != 2 AND d >= 0 SET @x += 1 SELECT x::y").Tokens.Select(t => t.Text).ToArray();
            Assert.Contains("<=", tokens);
            Assert.Contains("1.5E-3", tokens);
            Assert.Contains("<>", tokens);
            Assert.Contains("$5", tokens);
            Assert.Contains("!=", tokens);
            Assert.Contains(">=", tokens);
            Assert.Contains("+=", tokens);
            Assert.Contains("::", tokens);
        }

        [Fact]
        public void DoesNotMergeWhatWasWrittenApart()
        {
            var tokens = SqlStatementScanner.Scan("a < > b - - 1 < = c").Tokens.Select(t => t.Text).ToArray();
            Assert.DoesNotContain("<>", tokens);
            Assert.DoesNotContain("<=", tokens);
            Assert.DoesNotContain("--", tokens);
            Assert.Equal(new[] { "a", "<", ">", "b", "-", "-", "1", "<", "=", "c" }, tokens);
        }

        [Fact]
        public void ReportsWhatItCannotRead()
        {
            Assert.Contains("string", SqlStatementScanner.Scan("SELECT 'abc").Problems.Single().Message);
            Assert.Contains("comment", SqlStatementScanner.Scan("SELECT 1 /* abc").Problems.Single().Message);
            Assert.Contains("[", SqlStatementScanner.Scan("SELECT [abc").Problems.Single().Message);
            var open = SqlStatementScanner.Scan("SELECT (1 + 2");
            Assert.Single(open.Problems);
            Assert.Equal(7, open.Problems[0].Offset);
            var close = SqlStatementScanner.Scan("SELECT 1)");
            Assert.Equal(8, close.Problems.Single().Offset);
            Assert.Empty(SqlStatementScanner.Scan("SELECT (1 + 2) -- ok").Problems);
        }

        [Fact]
        public void ParenthesesAreCountedPerBatch()
        {
            var scan = SqlStatementScanner.Scan("SELECT (1\nGO\nSELECT 2)");
            Assert.Equal(2, scan.Problems.Count);
        }

        [Fact]
        public void LineAndColumnOfAnOffset()
        {
            var scan = SqlStatementScanner.Scan("SELECT 1\r\nFROM t\r\nWHERE x");
            Assert.Equal((3, 1), scan.LineAndColumn(scan.Tokens.First(t => t.Text == "WHERE").Start));
            Assert.Equal((1, 1), scan.LineAndColumn(0));
            Assert.Equal((2, 6), scan.LineAndColumn(scan.Tokens.First(t => t.Text == "t").Start));
        }

        [Fact]
        public void EmptyAndWhitespaceTextHaveNoStatements()
        {
            Assert.Empty(SqlStatementScanner.Scan("").Batches.SelectMany(b => b.Statements));
            Assert.Empty(SqlStatementScanner.Scan("  \r\n ").Batches.SelectMany(b => b.Statements));
        }
    }
}
