using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using SqlAssist.Core.Formatting;
using SqlAssist.Core.Risk;
using Xunit;

namespace SqlAssist.Core.Tests
{
    /// <summary>The formatter and the risk check run on the UI thread of SSMS, on scripts of any size, so they have to stay roughly
    /// linear. The limits are loose (a slow build machine must not fail them); they exist to catch a quadratic loop.</summary>
    public class PerformanceTests
    {
        private static string BigScript(int statements)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < statements; i++)
                sb.Append("select a.id, a.name, count(*) as cnt from dbo.Orders a inner join dbo.Customers c on c.id = a.cid and c.active = 1 ")
                  .Append("where a.status in (1, 2, 3) and (a.x is null or a.y > ").Append(i).Append(") -- row ").Append(i).Append('\n')
                  .Append("group by a.id, a.name order by a.id desc\ndelete from dbo.Log; update dbo.T set a = case when b = 1 then 2 when b = 2 then 3 else 4 end\n")
                  .Append(i % 50 == 49 ? "go\n" : "");
            return sb.ToString();
        }

        [Fact]
        public void FormattingAHalfMegabyteScriptTakesSeconds()
        {
            string script = BigScript(1500);
            Assert.True(script.Length > 400000);
            var watch = Stopwatch.StartNew();
            var result = SqlFormatter.Format(script);
            watch.Stop();
            Assert.True(result.IsSuccess, result.Error);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), "took " + watch.Elapsed);
        }

        [Fact]
        public void CheckingAHalfMegabyteScriptTakesSeconds()
        {
            string script = BigScript(1500);
            var watch = Stopwatch.StartNew();
            var report = RiskAnalyzer.Analyze(script, new RiskOptions());
            watch.Stop();
            Assert.Equal(3000, report.Findings.Count);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "took " + watch.Elapsed);
        }

        [Fact]
        public void ManyFindingsDoNotMakeTheLocationLookupQuadratic()
        {
            string script = string.Concat(Enumerable.Repeat("delete from t;\n", 60000));
            var watch = Stopwatch.StartNew();
            var report = RiskAnalyzer.Analyze(script, new RiskOptions());
            watch.Stop();
            Assert.Equal(60000, report.Findings.Count);
            Assert.Equal(60000, report.Findings[59999].Line);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "took " + watch.Elapsed);
        }
    }
}
