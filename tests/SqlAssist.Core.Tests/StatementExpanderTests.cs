using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class StatementExpanderTests
    {
        private static readonly DbObject Department = new DbObject("HumanResources", "Department", false, new[]
        {
            new DbColumn("DepartmentID", "smallint", isNullable: false, isIdentity: true, isPrimaryKey: true),
            new DbColumn("Name", "nvarchar(50)", isNullable: false),
            new DbColumn("GroupName", "nvarchar(50)"),
            new DbColumn("ModifiedDate", "datetime", isNullable: false),
            new DbColumn("NameLength", "int", isComputed: true),
            new DbColumn("RowVer", "timestamp", isNullable: false),
        });

        private static readonly DbRoutine UpdatePerson = new DbRoutine("Person", "sp_UpdatePerson", true, new[]
        {
            new DbParameter("@BusinessEntityID", "int"),
            new DbParameter("@FirstName", "nvarchar(50)"),
            new DbParameter("@Result", "int", isOutput: true),
        });

        private static ExpansionTarget At(string sqlWithCaret)
        {
            int caret = sqlWithCaret.IndexOf('|');
            return StatementExpander.Find(sqlWithCaret.Remove(caret, 1), caret);
        }

        [Theory]
        [InlineData("INSERT INTO HumanResources.Department|", ExpansionKind.Insert, "HumanResources", "Department")]
        [InlineData("insert Department|", ExpansionKind.Insert, null, "Department")]
        [InlineData("UPDATE [dbo].[Customers]|", ExpansionKind.Update, "dbo", "Customers")]
        [InlineData("EXEC Person.sp_UpdatePerson|", ExpansionKind.Exec, "Person", "sp_UpdatePerson")]
        [InlineData("EXECUTE @rc = dbo.usp_Do|", ExpansionKind.Exec, "dbo", "usp_Do")]
        [InlineData("ALTER PROCEDURE dbo.usp_Do|", ExpansionKind.Alter, "dbo", "usp_Do")]
        [InlineData("ALTER VIEW v_Orders|   ", ExpansionKind.Alter, null, "v_Orders")]
        [InlineData("GO\n  EXEC sp_who|", ExpansionKind.Exec, null, "sp_who")]
        public void Finds(string sql, ExpansionKind kind, string schema, string name)
        {
            var t = At(sql);
            Assert.NotNull(t);
            Assert.Equal((kind, schema, name), (t.Kind, t.Schema, t.Name));
        }

        [Theory]
        [InlineData("INSERT INTO Department| (Name) VALUES ('x')")] // template already there
        [InlineData("INSERT INTO|")]
        [InlineData("SELECT * FROM Department|")]
        [InlineData("-- EXEC dbo.p|")]
        [InlineData("SELECT 'EXEC dbo.p|")]
        [InlineData("ALTER TABLE Department|")]
        [InlineData("CREATE OR ALTER PROCEDURE dbo.p|")]
        [InlineData("FOREIGN KEY (a) REFERENCES t (a) ON UPDATE CASCADE|")]
        [InlineData("INSERT INTO Depart|ment")]
        public void IgnoresOtherPlaces(string sql) => Assert.Null(At(sql));

        [Fact]
        public void Find_ReportsStatementStart()
        {
            var t = At("  EXEC @rc = dbo.p|");
            Assert.Equal(2, t.StatementStart);
        }

        [Fact]
        public void Insert_SkipsIdentityComputedAndRowversion()
        {
            var e = StatementExpander.BuildInsert(Department, "", "\n");
            Assert.Equal(
                "\n(" +
                "\n    Name" +
                "\n   ,GroupName" +
                "\n   ,ModifiedDate" +
                "\n)" +
                "\nVALUES" +
                "\n(" +
                "\n    N'' -- Name nvarchar(50) NOT NULL" +
                "\n   ,N'' -- GroupName nvarchar(50)" +
                "\n   ,GETDATE() -- ModifiedDate datetime NOT NULL" +
                "\n);", e.Text);
            Assert.Equal("N''", e.Text.Substring(e.CaretOffset, 3));
        }

        [Fact]
        public void Update_SetsNonKeyColumnsAndFiltersOnThePrimaryKey()
        {
            var e = StatementExpander.BuildUpdate(Department, "\t", "\n");
            Assert.Equal(
                "\n\tSET Name = N'' -- nvarchar(50) NOT NULL" +
                "\n\t   ,GroupName = N'' -- nvarchar(50)" +
                "\n\t   ,ModifiedDate = GETDATE() -- datetime NOT NULL" +
                "\n\tWHERE DepartmentID = 0 -- smallint NOT NULL (PK)", e.Text);
        }

        [Fact]
        public void Update_WithoutPrimaryKeyLeavesWhereEmpty()
        {
            var t = new DbObject("dbo", "Log", false, new[] { new DbColumn("Msg", "varchar(100)") });
            Assert.EndsWith("\nWHERE ", StatementExpander.BuildUpdate(t, "", "\n").Text);
        }

        [Fact]
        public void Exec_AlignsParametersAndDeclaresOutputs()
        {
            const string line = "EXEC Person.sp_UpdatePerson";
            var e = StatementExpander.BuildExec(UpdatePerson, line, "", "\n");
            string pad = new string(' ', line.Length); // the comma sits under the space, so the @s line up
            Assert.Equal(
                " @BusinessEntityID = 0 -- int" +
                "\n" + pad + ",@FirstName = N'' -- nvarchar(50)" +
                "\n" + pad + ",@Result = @Result OUTPUT -- int", e.Text);
            Assert.Equal("DECLARE @Result int;\n", e.Prefix);
            Assert.Equal("0", e.Text.Substring(e.CaretOffset, 1));
        }

        [Fact]
        public void Exec_NoParametersIsNothingToExpand() =>
            Assert.Null(StatementExpander.BuildExec(new DbRoutine("dbo", "p", true, new DbParameter[0]), "EXEC p", "", "\n"));

        [Theory]
        [InlineData("CREATE PROCEDURE dbo.p AS SELECT 1\r\n", "ALTER PROCEDURE dbo.p AS SELECT 1")]
        [InlineData("-- header\r\n/* note */\r\ncreate   view v AS SELECT 1", "-- header\r\n/* note */\r\nALTER   view v AS SELECT 1")]
        [InlineData("CREATE OR ALTER FUNCTION f() RETURNS int AS BEGIN RETURN 1 END", "ALTER FUNCTION f() RETURNS int AS BEGIN RETURN 1 END")]
        [InlineData("SELECT 1", null)]
        public void AlterScript(string definition, string expected) =>
            Assert.Equal(expected, StatementExpander.AlterScript(definition));

        [Fact]
        public void SchemaModel_FindsRoutinesLikeTables()
        {
            var schema = new SchemaModel(new DbObject[0], new ForeignKey[0], new[] { UpdatePerson });
            Assert.Same(UpdatePerson, schema.FindRoutine(null, "SP_UPDATEPERSON"));
            Assert.Same(UpdatePerson, schema.FindRoutine("Person", "sp_UpdatePerson"));
            Assert.Null(schema.FindRoutine("dbo", "sp_UpdatePerson"));
        }
    }
}
