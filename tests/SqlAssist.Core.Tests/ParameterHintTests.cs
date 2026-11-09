using System.Linq;
using SqlAssist.Core.Hints;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Settings;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class ParameterHintTests
    {
        private static readonly SchemaModel Schema = new SchemaModel(new DbObject[0], new ForeignKey[0], new[]
        {
            new DbRoutine("dbo", "fnTax", RoutineKind.ScalarFunction,
                new[] { new DbParameter("@amount", "money"), new DbParameter("@rate", "decimal(5,2)") }, "money", "Tax on an amount."),
            new DbRoutine("dbo", "fnOrdersOf", RoutineKind.TableFunction, new[] { new DbParameter("@customerId", "int") },
                columns: new[] { new DbColumn("OrderID", "int") }),
            new DbRoutine("dbo", "fnNoArgs", RoutineKind.ScalarFunction, new DbParameter[0], "int"),
            new DbRoutine("dbo", "fnRows", RoutineKind.TableFunction, new[] { new DbParameter("@t", "dbo.IdList", isReadOnly: true) }),
            new DbRoutine("dbo", "usp_Save", RoutineKind.Procedure, new[] { new DbParameter("@id", "int") }),
            new DbRoutine("sales", "fnTax", RoutineKind.ScalarFunction, new[] { new DbParameter("@x", "int") }, "int"),
        });

        // The caret is the "|" in the text; returns what the editor would draw, or null.
        private static ParameterHint Hint(string marked, SchemaModel schema = null)
        {
            int caret = marked.IndexOf('|');
            string text = marked.Remove(caret, 1);
            var site = FunctionCallFinder.Find(text, caret);
            var signature = ParameterHints.Resolve(site, schema);
            return signature == null ? null : ParameterHints.Build(signature, site.ArgumentIndex);
        }

        private static string Active(ParameterHint hint) => string.Concat(hint.Line.Where(r => r.IsActive).Select(r => r.Text));

        [Fact]
        public void TheExampleFromTheRequest()
        {
            var hint = Hint("SELECT SUBSTRING(o.CustomerName, |)");
            Assert.Equal("SUBSTRING(expression, start, length)", hint.PlainLine);
            Assert.Equal("start", Active(hint));
            Assert.Equal(1, hint.ActiveParameter);
        }

        [Theory]
        [InlineData("SELECT SUBSTRING(", "expression")]
        [InlineData("SELECT SUBSTRING(a,", "start")]
        [InlineData("SELECT SUBSTRING(a, 1,", "length")]
        [InlineData("SELECT SUBSTRING(a, |)", "start")]
        public void TheActiveParameterFollowsTheCommas(string marked, string active)
        {
            Assert.Equal(active, Active(Hint(marked.Contains("|") ? marked : marked + "|")));
        }

        [Fact]
        public void TheActiveParameterChangesWhenTheCaretMovesWithoutTyping()
        {
            Assert.Equal("expression", Active(Hint("SELECT SUBSTRING(a|, 1, 10)")));
            Assert.Equal("start", Active(Hint("SELECT SUBSTRING(a, 1|, 10)")));
            Assert.Equal("length", Active(Hint("SELECT SUBSTRING(a, 1, 1|0)")));
        }

        [Fact]
        public void TheNestedExample()
        {
            const string sql = "SELECT REPLACE(SUBSTRING(o.Name, 1, 10), N',', N' ')";
            var inner = Hint(sql.Replace("1, 10", "1, |10"));
            Assert.StartsWith("SUBSTRING(", inner.PlainLine);
            Assert.Equal("length", Active(inner));

            var outer = Hint(sql.Replace("N','", "N'|,'"));
            Assert.StartsWith("REPLACE(", outer.PlainLine);
            Assert.Equal("string_pattern", Active(outer));
        }

        [Theory]
        [InlineData("SELECT CHARINDEX(|", "CHARINDEX(expressionToFind, expressionToSearch [, start_location])")]
        [InlineData("SELECT LEFT(|", "LEFT(character_expression, integer_expression)")]
        [InlineData("SELECT RIGHT(|", "RIGHT(character_expression, integer_expression)")]
        [InlineData("SELECT ISNULL(|", "ISNULL(check_expression, replacement_value)")]
        [InlineData("SELECT DATEADD(|", "DATEADD(datepart, number, date)")]
        [InlineData("SELECT DATEDIFF(|", "DATEDIFF(datepart, startdate, enddate)")]
        [InlineData("SELECT ROUND(|", "ROUND(numeric_expression, length [, function])")]
        [InlineData("SELECT REPLACE(|", "REPLACE(string_expression, string_pattern, string_replacement)")]
        [InlineData("SELECT COALESCE(|", "COALESCE(expression [, ...n])")]
        [InlineData("SELECT CONCAT(|", "CONCAT(argument1, argument2 [, argumentN]...)")]
        public void RequestedFunctionsAreSupported(string marked, string line)
        {
            Assert.Equal(line, Hint(marked).PlainLine);
        }

        [Fact]
        public void NamesAreCaseInsensitive()
        {
            Assert.Equal("SUBSTRING(expression, start, length)", Hint("select substring(|").PlainLine);
        }

        [Fact]
        public void OptionalArgumentsMayBeLeftOut()
        {
            var hint = Hint("SELECT CHARINDEX('a', b, |)");
            Assert.Equal("start_location", Active(hint));
            Assert.True(hint.Signature.Parameters[2].IsOptional);
            Assert.Equal("function", Active(Hint("SELECT ROUND(a, 2, |)")));
        }

        [Fact]
        public void VariadicLastParameterTakesEveryLaterArgument()
        {
            Assert.Equal("expression", Active(Hint("SELECT COALESCE(|")));
            Assert.Equal("...n", Active(Hint("SELECT COALESCE(a, |")));
            Assert.Equal("...n", Active(Hint("SELECT COALESCE(a, b, c, d, |")));
            Assert.Equal("argument2", Active(Hint("SELECT CONCAT(a, |")));
            Assert.Equal("argumentN", Active(Hint("SELECT CONCAT(a, b, |")));
            Assert.Equal("argumentN", Active(Hint("SELECT CONCAT(a, b, c, d, e, |")));
            Assert.Equal("val_n", Active(Hint("SELECT CHOOSE(1, 'a', 'b', 'c', |")));
        }

        [Fact]
        public void ArgumentsPastTheEndOfAFixedListPointAtNothing()
        {
            var hint = Hint("SELECT LEFT(a, 2, 3, |)");
            Assert.NotNull(hint);
            Assert.Equal(-1, hint.ActiveParameter);
            Assert.Equal("", Active(hint));
            Assert.Equal("LEFT(character_expression, integer_expression)", hint.PlainLine);
        }

        [Fact]
        public void NoParametersMeansNoActiveOne()
        {
            var hint = Hint("SELECT dbo.fnNoArgs(|)", Schema);
            Assert.Equal("dbo.fnNoArgs()", hint.PlainLine);
            Assert.Equal(-1, hint.ActiveParameter);
        }

        [Fact]
        public void UnknownAndSpecialSyntaxFunctionsShowNothing()
        {
            Assert.Null(Hint("SELECT Frobnicate(a, |)"));
            Assert.Null(Hint("SELECT COUNT(|)"));
            Assert.Null(Hint("SELECT CAST(a AS |)"));
            Assert.Null(Hint("SELECT TRY_CAST(a, |)"));
            Assert.Null(Hint("SELECT TRIM(' ' FROM |)"));
            Assert.Null(Hint("SELECT STRING_AGG(a, |)"));
            Assert.True(BuiltInSignatures.IsSpecialSyntax("cast"));
            Assert.False(BuiltInSignatures.IsSpecialSyntax("SUBSTRING"));
        }

        [Fact]
        public void TheCommaRuleIsNeverAppliedToASpecialSyntaxCallInsideAnotherCall()
        {
            // caret in CAST(...): no hint at all, not DATEADD's third parameter
            Assert.Null(Hint("SELECT DATEADD(day, CAST(a, |"));
            Assert.Equal("date", Active(Hint("SELECT DATEADD(day, CAST(a AS date), |)")));
        }

        [Fact]
        public void ParenthesesThatAreNotCallsShowNothing()
        {
            Assert.Null(Hint("SELECT * FROM t WHERE a IN (1, |2)"));
            Assert.Null(Hint("INSERT INTO t (a, |b) VALUES (1, 2)"));
            Assert.Null(Hint("INSERT INTO t (a, b) VALUES (1, |2)"));
            Assert.Null(Hint("SELECT * FROM (SELECT a, |b FROM t) d"));
            Assert.Null(Hint("SELECT (a + |b) FROM t"));
        }

        [Fact]
        public void TableNamesThatLookLikeBuiltInFunctionsAreNotCalls()
        {
            Assert.Null(Hint("INSERT INTO Year (a, |b) VALUES (1, 2)"));
            Assert.Null(Hint("SELECT * FROM Left (|NOLOCK)"));
            Assert.Null(Hint("CREATE TABLE Format (a int, |b int)"));
            Assert.Null(Hint("CREATE FUNCTION Replace (@a int, |@b int)"));
        }

        [Fact]
        public void UserFunctionsComeFromTheSchema()
        {
            var tax = Hint("SELECT dbo.fnTax(|)", Schema);
            Assert.Equal("dbo.fnTax(@amount money, @rate decimal(5,2))", tax.PlainLine);
            Assert.Equal("@amount money", Active(tax));
            Assert.Equal("@rate decimal(5,2)", Active(Hint("SELECT dbo.fnTax(100, |)", Schema)));
            Assert.Contains("Tax on an amount.", tax.Summary);
            Assert.Contains("Returns money", tax.Summary);
        }

        [Fact]
        public void TheSchemaPartPicksTheFunction()
        {
            Assert.Equal("sales.fnTax(@x int)", Hint("SELECT sales.fnTax(|)", Schema).PlainLine);
            Assert.Equal("dbo.fnTax(@amount money, @rate decimal(5,2))", Hint("SELECT [dbo].[fnTax](|)", Schema).PlainLine);
            Assert.Equal("dbo.fnTax(@amount money, @rate decimal(5,2))", Hint("SELECT DBO.FNTAX(|)", Schema).PlainLine);
        }

        [Fact]
        public void TableValuedFunctionsAndReadOnlyParameters()
        {
            var tvf = Hint("SELECT * FROM dbo.fnOrdersOf(|)", Schema);
            Assert.Equal("dbo.fnOrdersOf(@customerId int)", tvf.PlainLine);
            Assert.Equal("Returns TABLE", tvf.Summary);
            Assert.Equal("dbo.fnRows(@t dbo.IdList READONLY)", Hint("SELECT * FROM dbo.fnRows(|)", Schema).PlainLine);
        }

        [Fact]
        public void TheReturnValueIsNotAnArgument()
        {
            // The loader keeps the return value (parameter 0) out of Parameters; the hint lists only what is passed in.
            Assert.Equal(2, Hint("SELECT dbo.fnTax(|)", Schema).Signature.Parameters.Count);
        }

        [Fact]
        public void NothingWithoutMetadata()
        {
            Assert.Null(Hint("SELECT dbo.fnTax(|)"));                  // schema not loaded yet
            Assert.Null(Hint("SELECT dbo.fnMissing(|)", Schema));      // not in the schema
            Assert.Null(Hint("SELECT other.dbo.fnTax(|)", Schema));    // another database
            Assert.Null(Hint("SELECT fnTax(|)", Schema));              // a user function needs its schema
            Assert.NotNull(Hint("SELECT SUBSTRING(|"));                // a built-in function needs none
        }

        [Fact]
        public void ProceduresAreNotHinted()
        {
            Assert.Null(Hint("SELECT dbo.usp_Save(|)", Schema));
            Assert.Null(Hint("EXEC dbo.usp_Save (|)", Schema));
        }

        [Fact]
        public void NeedsSchemaOnlyForQualifiedNames()
        {
            Assert.True(ParameterHints.NeedsSchema(FunctionCallFinder.Find("SELECT dbo.fnTax(", 17)));
            Assert.False(ParameterHints.NeedsSchema(FunctionCallFinder.Find("SELECT LEFT(", 12)));
            Assert.False(ParameterHints.NeedsSchema(null));
        }

        [Fact]
        public void BoldOnlyTheActivePart()
        {
            var hint = Hint("SELECT SUBSTRING(a, |)");
            Assert.Equal(1, hint.Line.Count(r => r.IsActive));
            Assert.Equal("SUBSTRING(", hint.Line[0].Text);
        }

        [Fact]
        public void EveryBuiltInSignatureIsWellFormed()
        {
            foreach (string name in BuiltInSignatures.Names)
            {
                var signature = BuiltInSignatures.Find(name);
                Assert.True(signature.IsBuiltIn);
                Assert.Equal(name, signature.DisplayName);
                Assert.False(string.IsNullOrWhiteSpace(signature.Summary), name);
                Assert.NotEmpty(signature.Parameters);
                Assert.False(BuiltInSignatures.IsSpecialSyntax(name), name);
                // a variadic parameter is last, and nothing required follows an optional one
                for (int i = 0; i < signature.Parameters.Count; i++)
                {
                    if (signature.Parameters[i].IsVariadic) Assert.Equal(signature.Parameters.Count - 1, i);
                    if (i > 0 && signature.Parameters[i - 1].IsOptional) Assert.True(signature.Parameters[i].IsOptional, name);
                }
                Assert.NotNull(ParameterHints.Build(signature, 0));
            }
        }

        [Fact]
        public void TheOptionIsOnByDefaultAndRoundTrips()
        {
            Assert.True(new SqlAssistSettings().Editor.ShowParameterHints);
            Assert.True(SqlAssistSettings.Parse(null).Editor.ShowParameterHints);

            var settings = new SqlAssistSettings();
            settings.Editor.ShowParameterHints = false;
            Assert.False(SqlAssistSettings.Parse(settings.ToXml()).Editor.ShowParameterHints);
            Assert.True(SqlAssistSettings.Parse(settings.ToXml()).Editor.HighlightInsertColumns);   // the neighbouring option is untouched
            Assert.False(SqlAssistSettings.Parse("<SqlAssistSettings><Editor ShowParameterHints=\"FALSE\"/></SqlAssistSettings>").Editor.ShowParameterHints);
            Assert.True(SqlAssistSettings.Parse("<SqlAssistSettings><Editor ShowParameterHints=\"maybe\"/></SqlAssistSettings>").Editor.ShowParameterHints);
        }
    }
}
