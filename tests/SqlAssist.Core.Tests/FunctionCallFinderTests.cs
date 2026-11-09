using SqlAssist.Core.Hints;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class FunctionCallFinderTests
    {
        // The caret is the "|" in the text.
        private static CallSite Find(string marked)
        {
            int caret = marked.IndexOf('|');
            if (caret < 0) return FunctionCallFinder.Find(marked, marked.Length);   // no marker: the caret is at the end
            return FunctionCallFinder.Find(marked.Remove(caret, 1), caret);
        }

        private static void Expect(string marked, string name, int argument)
        {
            var site = Find(marked);
            Assert.NotNull(site);
            Assert.Equal(name, string.Join(".", site.NameParts));
            Assert.Equal(argument, site.ArgumentIndex);
        }

        [Fact]
        public void RightAfterTheOpeningParenIsTheFirstArgument() => Expect("SELECT SUBSTRING(|", "SUBSTRING", 0);

        [Fact]
        public void CommasMoveTheArgument()
        {
            Expect("SELECT SUBSTRING(o.CustomerName, |)", "SUBSTRING", 1);
            Expect("SELECT SUBSTRING(o.CustomerName, 1, |)", "SUBSTRING", 2);
            Expect("SELECT SUBSTRING(o.CustomerName,| 1, 2)", "SUBSTRING", 1);
            Expect("SELECT SUBSTRING(o.CustomerName|, 1, 2)", "SUBSTRING", 0);
        }

        [Fact]
        public void CaretMovedBackIntoAnExistingCall()
        {
            Expect("SELECT SUBSTRING(o.Name, 1|, 10) FROM Orders o", "SUBSTRING", 1);
            Expect("SELECT SUBSTRING(o.Name, 1, 10|) FROM Orders o", "SUBSTRING", 2);
        }

        [Fact]
        public void NotInsideOnceTheCallIsClosed()
        {
            Assert.Null(Find("SELECT SUBSTRING(o.Name, 1, 10)| FROM Orders o"));
            Assert.Null(Find("SELECT SUBSTRING(o.Name, 1, 10) FROM Orders o WHERE |"));
            Assert.Null(Find("SELECT |"));
        }

        [Fact]
        public void CommasInStringsDoNotCount()
        {
            Expect("SELECT REPLACE(a, ',', |)", "REPLACE", 2);
            Expect("SELECT REPLACE(a, N',', |)", "REPLACE", 2);
            Expect("SELECT REPLACE(N'a,b,c', |", "REPLACE", 1);
        }

        [Fact]
        public void EscapedQuoteDoesNotEndTheString()
        {
            Expect("SELECT ISNULL(N'O''Brien, Smith', |)", "ISNULL", 1);
            Expect("SELECT ISNULL(N'O''Brien, Smith'|, 'x')", "ISNULL", 0);
        }

        [Fact]
        public void CaretInsideAStringArgumentStillBelongsToTheCall()
        {
            Expect("SELECT REPLACE(a, N'x, y|', 'z')", "REPLACE", 1);
            Expect("SELECT REPLACE(a, N'|', 'z')", "REPLACE", 1);
            // between the two quotes of an escaped quote
            Expect("SELECT ISNULL(N'O'|'Brien, Smith', 1)", "ISNULL", 0);
        }

        [Fact]
        public void CommasInCommentsDoNotCount()
        {
            Expect("SELECT SUBSTRING(a, /* x, y */ |", "SUBSTRING", 1);
            Expect("SELECT SUBSTRING(a, -- one, two\n |", "SUBSTRING", 1);
        }

        [Fact]
        public void NothingInsideAComment()
        {
            Assert.Null(Find("SELECT SUBSTRING(a, /* note |*/ 1, 2)"));
            Assert.Null(Find("SELECT SUBSTRING(a, -- note |\n 1, 2)"));
            Assert.Null(Find("SELECT SUBSTRING(a, 1, 2) -- a(|"));
            Assert.Null(Find("/* SUBSTRING(|"));
        }

        [Fact]
        public void CaretRightAfterAClosedCommentIsNotInIt()
        {
            Expect("SELECT SUBSTRING(a, /* x */| 1)", "SUBSTRING", 1);
        }

        [Fact]
        public void InnermostCallWins()
        {
            Expect("SELECT REPLACE(SUBSTRING(o.Name, 1, |), N',', N' ')", "SUBSTRING", 2);
            Expect("SELECT REPLACE(SUBSTRING(o.Name, |1, 10), N',', N' ')", "SUBSTRING", 1);
        }

        [Fact]
        public void BackToTheOuterCallAfterTheInnerOneCloses()
        {
            Expect("SELECT REPLACE(SUBSTRING(o.Name, 1, 10), |N',', N' ')", "REPLACE", 1);
            Expect("SELECT REPLACE(SUBSTRING(o.Name, 1, 10), N'|,', N' ')", "REPLACE", 1);
            Expect("SELECT REPLACE(SUBSTRING(o.Name, 1, 10), N',', |N' ')", "REPLACE", 2);
            Expect("SELECT REPLACE(SUBSTRING(o.Name, 1, 10)|, N',', N' ')", "REPLACE", 0);
        }

        [Fact]
        public void AnInnerCallsCommasAreNotTheOuterCallsArguments()
        {
            Expect("SELECT DATEADD(day, DATEDIFF(day, 0, a), |)", "DATEADD", 2);
            // an unknown inner call is still not counted into the outer one: the finder stops at the innermost call it sees
            Expect("SELECT ISNULL(Unknown(1, 2), |)", "ISNULL", 1);
        }

        [Fact]
        public void InsideAnUnknownInnerCallTheInnermostCallIsReturnedSoNoMisleadingOuterHintFollows()
        {
            Expect("SELECT ISNULL(Unknown(1, |2), 0)", "Unknown", 1);
        }

        [Fact]
        public void GroupingParenthesesAreLookedThrough()
        {
            Expect("SELECT SUBSTRING((a + b), |)", "SUBSTRING", 1);
            Expect("SELECT SUBSTRING((a + |b), 2, 3)", "SUBSTRING", 0);
            Expect("SELECT ISNULL(a * (b + c), |)", "ISNULL", 1);
        }

        [Fact]
        public void InValuesAndOverAreNotCalls()
        {
            Assert.Null(Find("SELECT * FROM t WHERE a IN (1, |2)"));
            Assert.Null(Find("INSERT INTO t (a, b) VALUES (1, |2)"));
            Assert.Null(Find("SELECT ROW_NUMBER() OVER (PARTITION BY a, |b)"));
            Expect("SELECT ISNULL(a IN (1, |2), 0)", "ISNULL", 0);
            Expect("SELECT ISNULL(SUM(x) OVER (PARTITION BY a, |b), 0)", "ISNULL", 0);
        }

        [Fact]
        public void ASubqueryIsAWall()
        {
            Assert.Null(Find("SELECT ISNULL((SELECT MAX(x) FROM t WHERE a = |), 0)"));
            Assert.Null(Find("SELECT * FROM (SELECT a, |b FROM t) d"));
            Assert.Null(Find("SELECT ISNULL((WITH c AS (SELECT 1) SELECT |), 0)"));
        }

        [Fact]
        public void ATableNameFollowedByParenthesesIsReportedAsACallLikeNameForTheResolverToReject()
        {
            // The finder reports the name and what comes before it; ParameterHints.Resolve decides it is a table, not a function.
            var site = Find("INSERT INTO dbo.Orders (a, |b) VALUES (1, 2)");
            Assert.NotNull(site);
            Assert.Equal("INTO", site.Leader);
        }

        [Fact]
        public void QualifiedAndBracketedNames()
        {
            Expect("SELECT dbo.fnTax(|)", "dbo.fnTax", 0);
            Expect("SELECT [dbo].[fnTax](1, |)", "dbo.fnTax", 1);
            Expect("SELECT [my schema].[fn ,x](1, |)", "my schema.fn ,x", 1);
            Expect("SELECT [SUBSTRING](a, |)", "SUBSTRING", 1);
        }

        [Fact]
        public void ThreePartNamesKeepAllParts()
        {
            Expect("SELECT other.dbo.fnTax(1, |)", "other.dbo.fnTax", 1);
        }

        [Fact]
        public void IncompleteSql()
        {
            Expect("SELECT SUBSTRING(", "SUBSTRING", 0);                  // caret at the end
            Expect("SELECT SUBSTRING(a,", "SUBSTRING", 1);
            Expect("SELECT SUBSTRING(a, , |", "SUBSTRING", 2);          // an empty argument
            Expect("SELECT LEFT(|", "LEFT", 0);
            Assert.Null(Find("SELECT SUBSTRING|"));
        }

        [Fact]
        public void OpenCallsAreForgottenAtTheNextStatement()
        {
            Assert.Null(Find("SELECT LEFT(a, 2;\nSELECT b, |"));
            Assert.Null(Find("SELECT LEFT(a, 2\nGO\nSELECT b, |"));
            Assert.Null(Find("SELECT LEFT(a, 2\nSELECT b, |"));
            Assert.Null(Find("SELECT LEFT(a, 2\nFROM t WHERE x = |"));
            Assert.Null(Find("SELECT LEFT(a, 2\nUPDATE t SET b = |"));
        }

        [Fact]
        public void GoNeedsALineOfItsOwn()
        {
            Expect("SELECT ISNULL(go, |)", "ISNULL", 1);
        }

        [Fact]
        public void FromInsideTrimDoesNotEndTheCall()
        {
            Expect("SELECT TRIM(' ' FROM |", "TRIM", 0);
        }

        [Fact]
        public void OnlyTheStatementAroundTheCaretMatters()
        {
            Expect("SELECT 1;\nSELECT SUBSTRING(a, |", "SUBSTRING", 1);
            Expect("SELECT ISNULL(a, 1)\nSELECT ISNULL(b, |", "ISNULL", 1);
        }

        [Fact]
        public void AFarAwayOpenParenIsNotLookedFor()
        {
            string filler = new string('x', 100) + "\n";
            string text = "SELECT SUBSTRING(a" + new string('\n', 1) + string.Concat(System.Linq.Enumerable.Repeat(filler, 100)) + ", ";
            Assert.Null(FunctionCallFinder.Find(text, text.Length));
        }

        [Fact]
        public void EmptyAndEdgeInputs()
        {
            Assert.Null(FunctionCallFinder.Find(null, 0));
            Assert.Null(FunctionCallFinder.Find("", 0));
            Assert.Null(FunctionCallFinder.Find("abc", 5));
            Assert.Null(FunctionCallFinder.Find("(", 0));
        }

        [Fact]
        public void OffsetsPointIntoTheGivenText()
        {
            string text = "SELECT x, REPLACE(a, ',', b) FROM t";
            int caret = text.IndexOf("b)");
            var site = FunctionCallFinder.Find(text, caret);
            Assert.NotNull(site);
            Assert.Equal(text.IndexOf("REPLACE"), site.NameStart);
            Assert.Equal(text.IndexOf("(a"), site.OpenParen);
            Assert.Equal(2, site.ArgumentIndex);
        }

        [Fact]
        public void NeverThrowsAtAnyCaretOfAnyHalfTypedScript()
        {
            string[] scripts =
            {
                "SELECT REPLACE(SUBSTRING(o.Name, 1, 10), N',', N' '), [a,(b] AS 'x''(' /* c(, */ FROM dbo.fn(1, (2, 3)) -- t(\nGO\nINSERT INTO t (a) VALUES (ISNULL(@x, 'y'));",
                "((((,,,,))))'\"[]/*--",
                "SELECT ISNULL((SELECT MAX(x) FROM t WHERE a IN (1, 2)), CASE WHEN a = 1 THEN LEFT(b, 2) END)",
            };
            foreach (string script in scripts)
                for (int caret = 0; caret <= script.Length; caret++)
                    FunctionCallFinder.Find(script, caret);   // only the absence of an exception is asserted

            var random = new System.Random(12345);
            const string alphabet = "(),;'\"[]-/* \nNnGgOoSsEeLlCcTt@.#1";
            for (int run = 0; run < 400; run++)
            {
                var chars = new char[random.Next(1, 60)];
                for (int i = 0; i < chars.Length; i++) chars[i] = alphabet[random.Next(alphabet.Length)];
                string text = new string(chars);
                for (int caret = 0; caret <= text.Length; caret++)
                {
                    var site = FunctionCallFinder.Find(text, caret);
                    if (site == null) continue;
                    Assert.InRange(site.OpenParen, 0, caret - 1);
                    Assert.Equal('(', text[site.OpenParen]);
                    Assert.True(site.NameStart <= site.OpenParen);
                    Assert.True(site.ArgumentIndex >= 0);
                }
            }
        }

        [Fact]
        public void AVeryLongScriptIsReadOnlyNearTheCaret()
        {
            string line = "SELECT a, ISNULL(b, 0), 'x,y' FROM t WHERE c IN (1, 2, 3);\n";
            string text = string.Concat(System.Linq.Enumerable.Repeat(line, 50000)) + "SELECT SUBSTRING(a, ";
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var site = FunctionCallFinder.Find(text, text.Length);
            watch.Stop();
            Assert.Equal("SUBSTRING", site.Name);
            Assert.Equal(1, site.ArgumentIndex);
            Assert.True(watch.ElapsedMilliseconds < 1000, "took " + watch.ElapsedMilliseconds + " ms for a 3 MB script");
        }

        [Fact]
        public void LeaderIsTheWordBeforeTheName()
        {
            Assert.Equal("FROM", Find("SELECT * FROM dbo.fnRows(|)").Leader);
            Assert.Null(Find("SELECT 1 + dbo.fnTax(|)").Leader);
            Assert.Equal("SELECT", Find("SELECT ISNULL(|)").Leader);
        }
    }
}
