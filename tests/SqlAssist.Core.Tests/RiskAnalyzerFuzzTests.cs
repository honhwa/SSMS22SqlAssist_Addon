using System;
using SqlAssist.Core.Risk;
using Xunit;

namespace SqlAssist.Core.Tests
{
    /// <summary>The check runs on whatever is in the query window, finished or not: it may say less, but it must not fail,
    /// and every finding must point at real text.</summary>
    public class RiskAnalyzerFuzzTests
    {
        private static readonly string[] Seeds =
        {
            "update t set a=1, b=(select x from u where u.id=1) from t join v on v.id=t.id where t.k in (1,2)\ngo\ndelete from t output deleted.* into h\ntruncate table dbo.log; drop table if exists #a, dbo.b",
            "with c as (select * from t where x=1) delete from c where id=2; if @a=1 begin update t set a=1 end else delete top (5) from t -- note\n/* c */ merge t using s on 1=1 when matched then delete;",
            "exec('delete from t'); declare @t table(a int); update @t set a=1; drop view v; alter table t drop column c; drop table [dbo].[x y]",
        };

        private static readonly string[] Pieces =
        {
            "(", ")", "'", "\"", "[", "]", "--", "/*", "*/", "\n", ";", " ", "where", "update", "delete", "truncate", "table", "drop", "from", "go",
            "with", "as", "top", "case", "when", "end", "#t", "@v", ".", ",",
        };

        [Fact]
        public void RandomDamageNeverBreaksTheCheck()
        {
            var random = new Random(31415);
            for (int n = 0; n < 8000; n++)
            {
                string text = Seeds[random.Next(Seeds.Length)];
                for (int edits = random.Next(0, 5); edits > 0; edits--)
                {
                    int at = random.Next(text.Length + 1);
                    if (random.Next(3) == 0 && text.Length > 0) text = text.Remove(Math.Min(at, text.Length - 1), 1);
                    else text = text.Insert(at, " " + Pieces[random.Next(Pieces.Length)] + " ");
                }

                var options = new RiskOptions
                {
                    UpdateWithoutWhere = random.Next(4) != 0,
                    DeleteWithoutWhere = random.Next(4) != 0,
                    TruncateTable = random.Next(4) != 0,
                    DropTable = random.Next(4) != 0,
                    SkipTempTables = random.Next(2) == 0,
                };

                RiskReport report;
                try
                {
                    report = RiskAnalyzer.Analyze(text, options);
                }
                catch (Exception ex)
                {
                    throw new Xunit.Sdk.XunitException("EXCEPTION " + ex.GetType().Name + " on \"" + text.Replace("\n", "\\n") + "\"");
                }
                int previous = -1;
                foreach (var finding in report.Findings)
                {
                    Assert.True(finding.Offset >= 0 && finding.Length > 0 && finding.Offset + finding.Length <= text.Length, "range outside the text for \"" + text + "\"");
                    Assert.True(finding.Offset > previous, "findings out of order for \"" + text + "\"");
                    Assert.True(finding.Line >= 1 && finding.Column >= 1);
                    Assert.False(string.IsNullOrWhiteSpace(finding.Message));
                    Assert.True(options.IsEnabled(finding.Rule));
                    previous = finding.Offset;
                }
            }
        }
    }
}
