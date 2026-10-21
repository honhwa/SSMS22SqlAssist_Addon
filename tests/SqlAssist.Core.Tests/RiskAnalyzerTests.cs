using System.Linq;
using SqlAssist.Core.Risk;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class RiskAnalyzerTests
    {
        private static RiskFinding[] Find(string sql, RiskOptions options = null) =>
            RiskAnalyzer.Analyze(sql, options ?? new RiskOptions()).Findings.ToArray();

        private static RiskRule[] Rules(string sql, RiskOptions options = null) => Find(sql, options).Select(f => f.Rule).ToArray();

        // ---- UPDATE / DELETE without WHERE ----

        [Fact]
        public void UpdateWithoutWhere()
        {
            Assert.Equal(new[] { RiskRule.UpdateWithoutWhere }, Rules("UPDATE dbo.Orders SET Status = 1"));
            Assert.Empty(Find("UPDATE dbo.Orders SET Status = 1 WHERE Id = 5"));
        }

        [Fact]
        public void DeleteWithoutWhere()
        {
            Assert.Equal(new[] { RiskRule.DeleteWithoutWhere }, Rules("DELETE FROM dbo.Orders"));
            Assert.Equal(new[] { RiskRule.DeleteWithoutWhere }, Rules("DELETE dbo.Orders"));
            Assert.Empty(Find("DELETE FROM dbo.Orders WHERE Id = 5"));
        }

        [Fact]
        public void AWhereInsideASubqueryIsNotTheStatementsWhere()
        {
            Assert.Equal(new[] { RiskRule.UpdateWithoutWhere }, Rules("UPDATE t SET a = (SELECT b FROM u WHERE u.id = 1)"));
            Assert.Equal(new[] { RiskRule.DeleteWithoutWhere }, Rules("DELETE FROM t OUTPUT deleted.* INTO (SELECT 1 WHERE 1 = 1)"));
            Assert.Empty(Find("DELETE FROM t WHERE id IN (SELECT id FROM u WHERE x = 1)"));
        }

        [Fact]
        public void AWhereInACteIsNotTheStatementsWhere()
        {
            Assert.Equal(new[] { RiskRule.DeleteWithoutWhere }, Rules("WITH c AS (SELECT * FROM t WHERE x = 1) DELETE FROM c"));
            Assert.Empty(Find("WITH c AS (SELECT * FROM t WHERE x = 1) DELETE FROM c WHERE id = 2"));
        }

        [Fact]
        public void TheWordWhereInAStringOrCommentIsNotAWhere()
        {
            Assert.Equal(new[] { RiskRule.UpdateWithoutWhere }, Rules("UPDATE t SET note = 'WHERE id = 1'"));
            Assert.Equal(new[] { RiskRule.UpdateWithoutWhere }, Rules("UPDATE t SET a = 1 -- WHERE id = 1"));
            Assert.Equal(new[] { RiskRule.DeleteWithoutWhere }, Rules("DELETE FROM t /* WHERE id = 1 */"));
            Assert.Equal(new[] { RiskRule.UpdateWithoutWhere }, Rules("UPDATE t SET [where] = 1"));
        }

        [Fact]
        public void UpdateAndDeleteWithFromAndAliases()
        {
            Assert.Equal(new[] { RiskRule.UpdateWithoutWhere }, Rules("UPDATE a SET a.x = b.x FROM dbo.A a JOIN dbo.B b ON b.id = a.id"));
            Assert.Empty(Find("UPDATE a SET a.x = b.x FROM dbo.A a JOIN dbo.B b ON b.id = a.id WHERE b.y = 1"));
            Assert.Equal(new[] { RiskRule.DeleteWithoutWhere }, Rules("DELETE a FROM dbo.A a JOIN dbo.B b ON b.id = a.id"));
            Assert.Empty(Find("DELETE a FROM dbo.A a JOIN dbo.B b ON b.id = a.id WHERE b.y = 1"));
        }

        [Fact]
        public void WhereCurrentOfCounts()
        {
            Assert.Empty(Find("UPDATE t SET a = 1 WHERE CURRENT OF cur"));
            Assert.Empty(Find("DELETE FROM t WHERE CURRENT OF cur"));
        }

        [Fact]
        public void TopDoesNotMakeItSafe()
        {
            var finding = Find("DELETE TOP (10) FROM t").Single();
            Assert.Equal(RiskRule.DeleteWithoutWhere, finding.Rule);
            Assert.Contains("TOP", finding.Message);
        }

        [Fact]
        public void CaseWhenIsNotAWhere()
        {
            Assert.Equal(new[] { RiskRule.UpdateWithoutWhere }, Rules("UPDATE t SET a = CASE WHEN b = 1 THEN 1 ELSE 2 END"));
        }

        // ---- TRUNCATE / DROP TABLE ----

        [Fact]
        public void TruncateTable()
        {
            var finding = Find("TRUNCATE TABLE dbo.Orders").Single();
            Assert.Equal(RiskRule.TruncateTable, finding.Rule);
            Assert.Contains("dbo.Orders", finding.Message);
        }

        [Fact]
        public void DropTable()
        {
            var finding = Find("DROP TABLE dbo.Orders").Single();
            Assert.Equal(RiskRule.DropTable, finding.Rule);
            Assert.Contains("dbo.Orders", finding.Message);
            Assert.Equal(new[] { RiskRule.DropTable }, Rules("drop table if exists dbo.A, dbo.B"));
        }

        [Fact]
        public void OtherDropsAndAlterDropsAreNotDropTable()
        {
            Assert.Empty(Find("DROP VIEW v"));
            Assert.Empty(Find("DROP INDEX ix ON t"));
            Assert.Empty(Find("DROP PROCEDURE p"));
            Assert.Empty(Find("ALTER TABLE t DROP COLUMN c"));
            Assert.Empty(Find("ALTER TABLE t DROP CONSTRAINT pk"));
        }

        // ---- what is looked at ----

        [Fact]
        public void StatementsInsideBlocksAndAfterIfAreFound()
        {
            Assert.Equal(new[] { RiskRule.DeleteWithoutWhere }, Rules("IF @x = 1 DELETE FROM t"));
            Assert.Equal(new[] { RiskRule.UpdateWithoutWhere, RiskRule.TruncateTable },
                Rules("BEGIN TRY BEGIN TRANSACTION; UPDATE t SET a = 1; TRUNCATE TABLE u; COMMIT; END TRY BEGIN CATCH ROLLBACK; END CATCH"));
        }

        [Fact]
        public void EachStatementIsJudgedOnItsOwn()
        {
            Assert.Equal(new[] { RiskRule.DeleteWithoutWhere },
                Rules("UPDATE t SET a = 1 WHERE id = 1; DELETE FROM t; SELECT * FROM t WHERE id = 1"));
            Assert.Equal(new[] { RiskRule.UpdateWithoutWhere },
                Rules("DELETE FROM t WHERE id = 1\nUPDATE t SET a = 1\nSELECT * FROM t WHERE id = 1"));
        }

        [Fact]
        public void GoSeparatesBatches()
        {
            Assert.Equal(new[] { RiskRule.DeleteWithoutWhere, RiskRule.UpdateWithoutWhere },
                Rules("DELETE FROM a\nGO\nUPDATE b SET x = 1\nGO"));
        }

        [Fact]
        public void MergeAndDynamicSqlAreNotLookedInto()
        {
            Assert.Empty(Find("MERGE t USING s ON t.id = s.id WHEN MATCHED THEN DELETE;"));
            Assert.Empty(Find("EXEC('DELETE FROM t')"));
            Assert.Empty(Find("EXEC sp_executesql N'TRUNCATE TABLE t'"));
            Assert.Empty(Find("SELECT 'DROP TABLE t'"));
        }

        [Fact]
        public void SelectAndInsertAreNotRisks()
        {
            Assert.Empty(Find("SELECT * FROM t; INSERT INTO t (a) VALUES (1)"));
        }

        // ---- where it is ----

        [Fact]
        public void PositionIsTheKeywordNotTheLeadingComment()
        {
            string sql = "SELECT 1;\r\n-- clean up\r\n  DELETE FROM t;\r\nSELECT 2";
            var finding = Find(sql).Single();
            Assert.Equal(sql.IndexOf("DELETE"), finding.Offset);
            Assert.Equal(3, finding.Line);
            Assert.Equal(3, finding.Column);
        }

        [Fact]
        public void LengthCoversTheWholeStatement()
        {
            string sql = "SELECT 1; UPDATE t\n SET a = 1\n; SELECT 2";
            var finding = Find(sql).Single();
            Assert.Equal("UPDATE t\n SET a = 1\n;", sql.Substring(finding.Offset, finding.Length));
        }

        [Fact]
        public void TheStatementIsShownOnOneShortLine()
        {
            var finding = Find("UPDATE   dbo.Orders\n   SET Status = 1 -- all of them\n").Single();
            Assert.Equal("UPDATE dbo.Orders SET Status = 1", finding.Statement);
            string longStatement = "DELETE FROM dbo.T OUTPUT " + string.Join(", ", Enumerable.Range(1, 60).Select(i => "deleted.c" + i)) + " INTO @x";
            var shown = Find(longStatement).Single().Statement;
            Assert.True(shown.Length <= RiskAnalyzer.MaxStatementLength + 1);
            Assert.EndsWith("…", shown);
        }

        // ---- settings ----

        [Fact]
        public void EachRuleCanBeTurnedOff()
        {
            const string sql = "UPDATE a SET x = 1; DELETE FROM b; TRUNCATE TABLE c; DROP TABLE d";
            Assert.Equal(4, Find(sql).Length);
            Assert.Equal(new[] { RiskRule.DeleteWithoutWhere, RiskRule.TruncateTable, RiskRule.DropTable }, Rules(sql, new RiskOptions { UpdateWithoutWhere = false }));
            Assert.Equal(new[] { RiskRule.UpdateWithoutWhere, RiskRule.TruncateTable, RiskRule.DropTable }, Rules(sql, new RiskOptions { DeleteWithoutWhere = false }));
            Assert.Equal(new[] { RiskRule.UpdateWithoutWhere, RiskRule.DeleteWithoutWhere, RiskRule.DropTable }, Rules(sql, new RiskOptions { TruncateTable = false }));
            Assert.Equal(new[] { RiskRule.UpdateWithoutWhere, RiskRule.DeleteWithoutWhere, RiskRule.TruncateTable }, Rules(sql, new RiskOptions { DropTable = false }));
            Assert.Empty(Find(sql, new RiskOptions { UpdateWithoutWhere = false, DeleteWithoutWhere = false, TruncateTable = false, DropTable = false }));
        }

        [Fact]
        public void TempTablesCanBeLeftOut()
        {
            const string sql = "DELETE FROM #t; TRUNCATE TABLE ##g; DROP TABLE #t; UPDATE @v SET a = 1; DELETE FROM dbo.Real; DROP TABLE #a, dbo.B";
            Assert.Equal(6, Find(sql).Length);
            var skipped = Find(sql, new RiskOptions { SkipTempTables = true });
            Assert.Equal(new[] { RiskRule.DeleteWithoutWhere, RiskRule.DropTable }, skipped.Select(f => f.Rule).ToArray());
            Assert.Contains("dbo.Real", skipped[0].Message);
            Assert.Contains("dbo.B", skipped[1].Message);
        }

        // ---- unfinished text ----

        [Fact]
        public void AnUnfinishedScriptIsAnalysedAsFarAsItReadsAndSaysSo()
        {
            var report = RiskAnalyzer.Analyze("DELETE FROM t;\nUPDATE u SET a = 'unfinished", new RiskOptions());
            Assert.Contains(report.Findings, f => f.Rule == RiskRule.DeleteWithoutWhere);
            Assert.NotEmpty(report.Notes);
            Assert.Contains("string", report.Notes[0]);
        }

        [Fact]
        public void EmptyTextHasNothing()
        {
            var report = RiskAnalyzer.Analyze("", new RiskOptions());
            Assert.Empty(report.Findings);
            Assert.Empty(report.Notes);
            Assert.Empty(RiskAnalyzer.Analyze(null, new RiskOptions()).Findings);
        }

        [Fact]
        public void TheReportCarriesTheLimitsInsteadOfClaimingSafety()
        {
            Assert.Contains("WHERE", RiskAnalyzer.Disclaimer);
            Assert.Contains("ปลอดภัย", RiskAnalyzer.Disclaimer);
        }
    }
}
