using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Formatting;
using Xunit;

namespace SqlAssist.Core.Tests
{
    /// <summary>The formatter has to stay safe on text nobody planned for: whatever it is given, the answer is either text that
    /// formats the same again, or an error with the original text untouched.</summary>
    public class SqlFormatterFuzzTests
    {
        private static readonly string[] Seeds =
        {
            "select a.id,a.name,count(*) as cnt from dbo.Orders a inner join dbo.Customers c on c.id=a.cid and c.active=1 where a.status in (1,2,3) and (a.x is null or a.y>5) group by a.id,a.name having count(*)>1 order by a.id desc",
            "with a as (select 1 as x),b(y) as (select 2) select case when x=1 then 'a' when x=2 then 'b' else 'c' end from a,b union all select 3,4",
            "update t set a=1,b=2 from t join u on u.id=t.id where t.x in (select y from z) -- note\n/* c */ delete from t where id=1\ngo\ninsert into t(a,b) values(1,'x'),(2,'y')",
            "if exists(select 1 from t) begin update t set a=1 end else begin print 'x' end\nwhile @i<3 begin set @i+=1 end\nbegin try select 1 end try begin catch throw end catch",
            "create procedure dbo.p @a int, @b varchar(10) = 'x' as begin declare @t table (a int, b varchar(10)); select * from @t where a=@a and b like @b + '%' end",
            "merge into t as x using s on x.id=s.id when matched then update set a=1 when not matched then insert (id) values (s.id) output $action;\nselect top (5) with ties a, -b, - -c, $5, 1.5e-3, 0x1F from t order by a offset 5 rows fetch next 10 rows only",
        };

        private static readonly string[] Pieces =
        {
            "(", ")", "'", "\"", "[", "]", "--", "/*", "*/", "\n", "\r\n", ",", ";", " ", "\t", "select", "from", "where", "and", "or", "case",
            "end", "when", "then", "else", "begin", "go", "join", "on", "with", "as", "union", "insert", "update", "delete", "set", "values", "if",
            "@x", "#t", "1", "-", "*", "=", ".", "N'", "$", "::",
        };

        [Fact]
        public void RandomDamageNeverBreaksTheRules()
        {
            var random = new Random(20261021);
            var failures = new List<string>();
            int succeeded = 0, refused = 0;

            for (int n = 0; n < 6000; n++)
            {
                string text = Damage(random, Seeds[random.Next(Seeds.Length)]);
                var options = new FormatOptions
                {
                    CommaStyle = random.Next(2) == 0 ? CommaStyle.Leading : CommaStyle.Trailing,
                    KeywordCase = (KeywordCase)random.Next(3),
                    IndentSize = random.Next(2, 9),
                };

                FormatResult first;
                try
                {
                    first = SqlFormatter.Format(text, options);
                }
                catch (Exception ex)
                {
                    failures.Add("EXCEPTION " + ex.GetType().Name + " on: " + Show(text));
                    continue;
                }

                if (!first.IsSuccess)
                {
                    refused++;
                    if (first.Text != text) failures.Add("TEXT CHANGED despite error on: " + Show(text));
                    continue;
                }
                succeeded++;
                var second = SqlFormatter.Format(first.Text, options);
                if (!second.IsSuccess) failures.Add("OUTPUT NOT FORMATTABLE (" + second.Error + ") from: " + Show(text) + " => " + Show(first.Text));
                else if (second.Text != first.Text) failures.Add("NOT IDEMPOTENT on: " + Show(text) + "\n--first--\n" + first.Text + "\n--second--\n" + second.Text);
                if (failures.Count >= 5) break;
            }

            Assert.True(failures.Count == 0, string.Join("\n=====\n", failures) + "\n(succeeded " + succeeded + ", refused " + refused + ")");
            Assert.True(succeeded > 500, "too few texts were formattable to say anything: " + succeeded);
        }

        private static string Damage(Random random, string text)
        {
            int edits = random.Next(0, 4);
            for (int i = 0; i < edits; i++)
            {
                int at = random.Next(text.Length + 1);
                switch (random.Next(4))
                {
                    case 0 when text.Length > 0:
                        text = text.Remove(Math.Min(at, text.Length - 1), 1);
                        break;
                    case 1:
                        text = text.Insert(at, Pieces[random.Next(Pieces.Length)]);
                        break;
                    case 2:
                        text = text.Insert(at, " " + Pieces[random.Next(Pieces.Length)] + " ");
                        break;
                    default:
                        // cut a stretch out, which leaves things half written
                        int length = Math.Min(random.Next(1, 20), text.Length - at);
                        if (length > 0) text = text.Remove(at, length);
                        break;
                }
            }
            return text;
        }

        private static string Show(string text) => "\"" + text.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }
}
