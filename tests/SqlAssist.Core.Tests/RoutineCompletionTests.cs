using System.Linq;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class RoutineCompletionTests
    {
        private static CaretContext At(string sqlWithCaret)
        {
            int caret = sqlWithCaret.IndexOf('|');
            return SqlContextAnalyzer.Analyze(sqlWithCaret.Remove(caret, 1), caret);
        }

        [Theory]
        [InlineData("EXEC |")]
        [InlineData("EXEC USP_|")]
        [InlineData("use db\ngo\nexecute |")]
        [InlineData("EXEC @rc = |")]
        public void AfterExec_ExpectsARoutine(string sql)
        {
            var ctx = At(sql);
            Assert.True(ctx.IsRoutineName);
            Assert.Equal(CaretContextKind.General, ctx.Kind);
        }

        [Fact]
        public void ExecSchemaDot_IsTheSchemasRoutines()
        {
            var ctx = At("EXEC dbo.USP_|");
            Assert.True(ctx.IsRoutineName);
            Assert.Equal(CaretContextKind.Qualified, ctx.Kind);
            Assert.True(ctx.QualifierIsSchema);
            Assert.Equal("dbo", ctx.Qualifier);
        }

        [Theory]
        [InlineData("SELECT |")]
        [InlineData("EXEC dbo.p @a = |")]
        [InlineData("SELECT * FROM dbo.|")]
        [InlineData("EXEC ('SELECT 1') |")]
        public void ElsewhereIsNot(string sql) => Assert.False(At(sql).IsRoutineName);

        [Fact]
        public void Details_ForFunction_ShowParametersAndReturnType()
        {
            var f = new DbRoutine("dbo", "ufnGetProductDealerPrice", RoutineKind.ScalarFunction, new[]
            {
                new DbParameter("@ProductID", "int"),
                new DbParameter("@OrderDate", "datetime"),
            }, returnType: "money", description: " Scalar function returning the dealer price. ");

            var d = TableDetails.Build(f);
            Assert.Equal(("Function", "MONEY", "Scalar function returning the dealer price."), (d.Kind, d.Returns, d.Description));
            Assert.Equal(new[] { ("@ProductID", "INT", "INPUT"), ("@OrderDate", "DATETIME", "INPUT") },
                d.Columns.Select(c => (c.Name, c.Type, c.Flags)));
            Assert.All(d.Columns, c => Assert.Equal(ColumnKeyKind.Parameter, c.Key));
            Assert.Null(d.RowCount);
        }

        [Fact]
        public void Details_ForProcedure_MarksOutputAndHasNoReturnType()
        {
            var p = new DbRoutine("dbo", "uspGetList", RoutineKind.Procedure, new[]
            {
                new DbParameter("@MaxPrice", "money"),
                new DbParameter("@ListPrice", "money", isOutput: true),
                new DbParameter("@Rows", "dbo.IdList", isReadOnly: true),
            });
            var d = TableDetails.Build(p);
            Assert.Equal("Procedure", d.Kind);
            Assert.Null(d.Returns);
            Assert.Equal(new[] { "INPUT", "OUTPUT", "READONLY" }, d.Columns.Select(c => c.Flags));
        }

        [Fact]
        public void TableFunctionReturnsTable() =>
            Assert.Equal("TABLE", new DbRoutine("dbo", "tvf", RoutineKind.TableFunction, new DbParameter[0]).ReturnType);
    }
}
