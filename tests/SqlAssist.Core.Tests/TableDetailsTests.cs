using System.Linq;
using SqlAssist.Core.Schema;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class TableDetailsTests
    {
        private static readonly DbObject Request = new DbObject("dbo", "LOG_Work_Request", false, new[]
        {
            new DbColumn("LogId", "int", isNullable: false, isIdentity: true, isPrimaryKey: true),
            new DbColumn("ReqId", "int", isNullable: false),
            new DbColumn("Code", "nvarchar(50)"),
            new DbColumn("WorkDateFrom", SqlTypeFormatter.Format("datetime2", 7, 23, 3)),
        }, rowCount: 60533);

        private static readonly DbObject Parent = new DbObject("dbo", "Work_Request", false, new[] { new DbColumn("ReqId", "int", false, false, true) });

        private static readonly SchemaModel Schema = new SchemaModel(new[] { Request, Parent },
            new[] { new ForeignKey("FK_Log_Req", Request, Parent, new[] { ("ReqId", "ReqId") }) });

        [Fact]
        public void HeaderAndRowCount()
        {
            var d = TableDetails.Build(Schema, Request);
            Assert.Equal("dbo", d.Schema);
            Assert.Equal("LOG_Work_Request", d.Name);
            Assert.Equal("Table", d.Kind);
            Assert.Equal("60,533", d.RowCount);
        }

        [Fact]
        public void Columns_TypeUpperCase_FlagsAndKeys()
        {
            var c = TableDetails.Build(Schema, Request).Columns;
            Assert.Equal(("INT", "IDENTITY", ColumnKeyKind.PrimaryKey), (c[0].Type, c[0].Flags, c[0].Key));
            Assert.Equal(("INT", "NOT NULL", ColumnKeyKind.ForeignKey), (c[1].Type, c[1].Flags, c[1].Key));
            Assert.Equal(("NVARCHAR(50)", "", ColumnKeyKind.None), (c[2].Type, c[2].Flags, c[2].Key));
            Assert.Equal("DATETIME2(3)", c[3].Type);
        }

        [Fact]
        public void ViewHasNoRowCount()
        {
            var view = new DbObject("dbo", "v", true, new[] { new DbColumn("a", "int") }, rowCount: 5);
            var d = TableDetails.Build(new SchemaModel(new[] { view }, new ForeignKey[0]), view);
            Assert.Equal("View", d.Kind);
            Assert.Null(d.RowCount);
        }

        [Fact]
        public void LongTablesAreCapped()
        {
            var wide = new DbObject("dbo", "w", false, Enumerable.Range(1, 70).Select(i => new DbColumn("c" + i, "int")).ToArray());
            var d = TableDetails.Build(new SchemaModel(new[] { wide }, new ForeignKey[0]), wide);
            Assert.Equal(60, d.Columns.Count);
            Assert.Equal(10, d.HiddenColumns);
        }

        [Theory]
        [InlineData("datetime2", 7, "datetime2")]
        [InlineData("datetime2", 3, "datetime2(3)")]
        [InlineData("time", 0, "time(0)")]
        public void FractionalSecondPrecision(string type, byte scale, string expected) =>
            Assert.Equal(expected, SqlTypeFormatter.Format(type, 8, 0, scale));
    }
}
