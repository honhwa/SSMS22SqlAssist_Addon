using System;
using System.Globalization;
using System.Linq;
using SqlAssist.Core.Clipboard;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class ClipboardSqlBuilderTests
    {
        private const ClipboardColumnType Txt = ClipboardColumnType.Text;
        private const ClipboardColumnType Int = ClipboardColumnType.Int;

        private static ClipboardSqlRequest Request(string text, ClipboardSqlKind kind, bool header = true, params ClipboardColumnType[] types)
        {
            var table = DelimitedTextParser.Parse(text);
            Assert.True(table.IsValid, string.Join("; ", table.Errors));
            return new ClipboardSqlRequest
            {
                Rows = table.Rows,
                FirstRowIsHeader = header,
                ColumnTypes = types.Length > 0 ? types : Enumerable.Repeat(Txt, table.ColumnCount).ToArray(),
                Kind = kind,
                Newline = "\n",
            };
        }

        private static ClipboardSqlResult Build(string text, ClipboardSqlKind kind, bool header = true, params ClipboardColumnType[] types) =>
            ClipboardSqlBuilder.Build(Request(text, kind, header, types));

        // ---- IN (...) ----

        [Fact]
        public void In_KeepsLeadingZerosByDefault()
        {
            var result = Build("Code\n00123\n00456\n", ClipboardSqlKind.In);
            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Equal("IN (N'00123', N'00456')\n", result.Script);
        }

        [Fact]
        public void In_UsesTheChosenColumnAndItsType()
        {
            var request = Request("Id\tCode\n1\tA\n2\tB\n", ClipboardSqlKind.In, true, Int, Txt);
            request.InColumn = 0;
            Assert.Equal("IN (1, 2)\n", ClipboardSqlBuilder.Build(request).Script);
            request.InColumn = 1;
            Assert.Equal("IN (N'A', N'B')\n", ClipboardSqlBuilder.Build(request).Script);
        }

        [Fact]
        public void In_VarcharColumnsGetPlainLiterals()
        {
            var result = Build("Code\nA1\nB'2\n", ClipboardSqlKind.In, true, ClipboardColumnType.Varchar);
            Assert.Equal("IN ('A1', 'B''2')\n", result.Script);
        }

        [Fact]
        public void In_DistinctDropsRepeatsAndKeepsFirstOrder()
        {
            string text = "c\nb\na\nb\nc\n";
            var request = Request(text, ClipboardSqlKind.In, header: false);
            request.Distinct = false;
            Assert.Equal("IN (N'c', N'b', N'a', N'b', N'c')\n", ClipboardSqlBuilder.Build(request).Script);
            request.Distinct = true;
            Assert.Equal("IN (N'c', N'b', N'a')\n", ClipboardSqlBuilder.Build(request).Script);
        }

        [Fact]
        public void In_SkipsNullsAndSaysSo()
        {
            var result = Build("v\na\n\nNULL\nb\n", ClipboardSqlKind.In);
            Assert.Equal("IN (N'a', N'b')\n", result.Script);
            Assert.Contains(result.Warnings, w => w.Contains("NULL") && w.Contains("2"));
        }

        [Fact]
        public void In_WithNothingToListIsAnError()
        {
            var result = Build("v\n\nNULL\n", ClipboardSqlKind.In);
            Assert.False(result.IsValid);
            Assert.Null(result.Script);
        }

        [Fact]
        public void In_WrapsLongListsTenValuesPerLine()
        {
            string text = string.Join("\n", Enumerable.Range(1, 11)) + "\n";
            var result = Build(text, ClipboardSqlKind.In, header: false, Int);
            Assert.Equal("IN (\n    1, 2, 3, 4, 5, 6, 7, 8, 9, 10,\n    11\n)\n", result.Script);
        }

        [Fact]
        public void In_WarnsWhenTheListIsHuge()
        {
            string text = string.Join("\n", Enumerable.Range(1, ClipboardSqlBuilder.LargeInListWarning + 1)) + "\n";
            var result = Build(text, ClipboardSqlKind.In, header: false, Int);
            Assert.True(result.IsValid);
            Assert.Contains(result.Warnings, w => w.Contains("#Temp"));
        }

        [Fact]
        public void In_ColumnOutsideTheTableIsAnError()
        {
            var request = Request("a\tb\n1\t2\n", ClipboardSqlKind.In);
            request.InColumn = 2;
            var result = ClipboardSqlBuilder.Build(request);
            Assert.False(result.IsValid);
            Assert.Null(result.Script);
        }

        // ---- VALUES ----

        [Fact]
        public void Values_WritesOneTupleRowPerLine()
        {
            var result = Build("Name\tQty\nit's\t5\nสวัสดี\t7\n", ClipboardSqlKind.Values, true, Txt, Int);
            Assert.Equal("VALUES\n(N'it''s', 5),\n(N'สวัสดี', 7)\n", result.Script);
        }

        [Fact]
        public void Values_SplitsAfterAThousandRowsLikeInsert()
        {
            string text = string.Join("\n", Enumerable.Range(1, 1001)) + "\n";
            var result = Build(text, ClipboardSqlKind.Values, header: false, Int);
            Assert.True(result.IsValid);
            Assert.Equal(2, result.Script.Split(new[] { "VALUES" }, StringSplitOptions.None).Length - 1);
            Assert.Contains("(1000)\n\n", result.Script);
            Assert.Contains(result.Warnings, w => w.Contains("1,000"));
        }

        // ---- #Temp ----

        [Fact]
        public void TempTable_CreatesTheTableAndInsertsTheRows()
        {
            var result = Build("Code\tQty\n00123\t5\n00456\t\n", ClipboardSqlKind.TempTable, true, Txt, Int);
            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Equal(
                "CREATE TABLE #Temp (\n" +
                "    Code nvarchar(5),\n" +
                "    Qty int)\n\n" +
                "INSERT INTO #Temp (Code, Qty) VALUES\n" +
                "(N'00123', 5),\n" +
                "(N'00456', NULL)\n" +
                "GO\n", result.Script);
        }

        [Fact]
        public void TempTable_WithoutAHeaderNamesColumnsColumn1Column2()
        {
            var result = Build("a\t1\n", ClipboardSqlKind.TempTable, false, Txt, Int);
            Assert.Contains("    Column1 nvarchar(1),\n    Column2 int)", result.Script);
            Assert.Contains("INSERT INTO #Temp (Column1, Column2) VALUES\n(N'a', 1)\nGO\n", result.Script);
        }

        [Fact]
        public void TempTable_QuotesAndDeduplicatesColumnNames()
        {
            var result = Build("Order Id\tid\tID\t\ta]b\tSelect\n1\t2\t3\t4\t5\t6\n", ClipboardSqlKind.TempTable, true,
                Int, Int, Int, Int, Int, Int);
            Assert.Contains("[Order Id] int", result.Script);
            Assert.Contains("    id int,\n    ID_2 int,\n    Column4 int,\n    [a]]b] int,\n    [Select] int)", result.Script);
        }

        [Fact]
        public void TempTable_CustomNameIsValidated()
        {
            var request = Request("a\n1\n", ClipboardSqlKind.TempTable);
            request.TempTableName = "##Global1";
            Assert.StartsWith("CREATE TABLE ##Global1", ClipboardSqlBuilder.Build(request).Script);

            foreach (string bad in new[] { "Temp", "dbo.#Temp", "#my table", "#", "", null, "#a;DROP TABLE x" })
            {
                request.TempTableName = bad;
                var result = ClipboardSqlBuilder.Build(request);
                Assert.False(result.IsValid);
                Assert.Null(result.Script);
            }
        }

        [Fact]
        public void TempTable_SizesTextColumnsFromTheData()
        {
            var result = Build("a\tb\tc\nxxxxx\tx\t\n\tyy\t\n", ClipboardSqlKind.TempTable, true, Txt, ClipboardColumnType.Varchar, Txt);
            Assert.Contains("    a nvarchar(5),\n    b varchar(2),\n    c nvarchar(1))", result.Script);

            string longText = new string('x', 4001);
            result = Build("a\n" + longText + "\n", ClipboardSqlKind.TempTable);
            Assert.Contains("a nvarchar(max)", result.Script);
            result = Build("a\n" + new string('x', 4000) + "\n", ClipboardSqlKind.TempTable);
            Assert.Contains("a nvarchar(4000)", result.Script);
            result = Build("a\n" + new string('x', 8001) + "\n", ClipboardSqlKind.TempTable, true, ClipboardColumnType.Varchar);
            Assert.Contains("a varchar(max)", result.Script);
        }

        [Fact]
        public void TempTable_SizesDecimalColumnsFromTheData()
        {
            var result = Build("n\n12.50\n3.1\n100\n", ClipboardSqlKind.TempTable, true, ClipboardColumnType.Decimal);
            Assert.Contains("n decimal(5,2)", result.Script);
            Assert.Contains("(12.50),\n(3.1),\n(100)", result.Script);
            result = Build("n\n0.5\n", ClipboardSqlKind.TempTable, true, ClipboardColumnType.Decimal);
            Assert.Contains("n decimal(1,1)", result.Script);
            result = Build("n\n0\n", ClipboardSqlKind.TempTable, true, ClipboardColumnType.Decimal);
            Assert.Contains("n decimal(1,0)", result.Script);
        }

        [Fact]
        public void TempTable_FixedTypesKeepTheirNames()
        {
            var types = new[]
            {
                ClipboardColumnType.BigInt, ClipboardColumnType.Float, ClipboardColumnType.Bit, ClipboardColumnType.Date,
                ClipboardColumnType.DateTime, ClipboardColumnType.DateTime2, ClipboardColumnType.UniqueIdentifier,
            };
            var result = Build("a\tb\tc\td\te\tf\tg\n1\t1.5\t1\t2024-01-31\t2024-01-31 13:05:09\t2024-01-31 13:05:09\t0f8fad5b-d9cb-469f-a165-70867728950e\n",
                ClipboardSqlKind.TempTable, true, types);
            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Contains("a bigint,\n    b float,\n    c bit,\n    d date,\n    e datetime,\n    f datetime2,\n    g uniqueidentifier)", result.Script);
            Assert.Contains("(1, 1.5, 1, '2024-01-31', '2024-01-31T13:05:09.000', '2024-01-31 13:05:09.0000000', '0F8FAD5B-D9CB-469F-A165-70867728950E')", result.Script);
        }

        [Fact]
        public void TempTable_HeaderOnlyHasNothingToInsert()
        {
            var result = Build("a\tb\n", ClipboardSqlKind.TempTable);
            Assert.False(result.IsValid);
            Assert.Null(result.Script);
        }

        // ---- NULL rules ----

        [Fact]
        public void Nulls_TheThreeKindsOfNothingAreSeparateRules()
        {
            // empty cell, the text NULL, and an ordinary value
            var request = Request("a\tb\tc\n\tNULL\tx\n", ClipboardSqlKind.Values, true);

            Assert.Equal("VALUES\n(NULL, NULL, N'x')\n", ClipboardSqlBuilder.Build(request).Script);

            request.EmptyCell = EmptyCellRule.EmptyString;
            Assert.Equal("VALUES\n(N'', NULL, N'x')\n", ClipboardSqlBuilder.Build(request).Script);

            request.NullText = NullTextRule.Text;
            Assert.Equal("VALUES\n(N'', N'NULL', N'x')\n", ClipboardSqlBuilder.Build(request).Script);

            request.EmptyCell = EmptyCellRule.Null;
            Assert.Equal("VALUES\n(NULL, N'NULL', N'x')\n", ClipboardSqlBuilder.Build(request).Script);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("Null")]
        [InlineData(" NULL")]
        [InlineData("NULL ")]
        [InlineData("NULLS")]
        public void Nulls_OnlyTheExactWordNULLCountsSoNamesLikeNullSurvive(string cell)
        {
            var result = Build("a\n" + cell + "\n", ClipboardSqlKind.Values);
            Assert.Equal("VALUES\n(N'" + cell + "')\n", result.Script);
        }

        [Fact]
        public void Nulls_AnEmptyCellIsNotAWhitespaceCell()
        {
            Assert.Equal("VALUES\n(N' ')\n", Build("a\n \n", ClipboardSqlKind.Values).Script);
        }

        [Fact]
        public void Nulls_EmptyStringIsRefusedForTypesThatCannotHoldIt()
        {
            var request = Request("n\tm\n\tx\n", ClipboardSqlKind.Values, true, Int, Txt);
            request.EmptyCell = EmptyCellRule.EmptyString;
            var result = ClipboardSqlBuilder.Build(request);
            Assert.False(result.IsValid);
            Assert.Null(result.Script);
            Assert.Contains("แถวที่ 2", result.Errors[0]);
            Assert.Contains("คอลัมน์ 1", result.Errors[0]);
        }

        [Fact]
        public void Nulls_NullTextAsTextIsRefusedForTypesThatCannotHoldIt()
        {
            var request = Request("n\nNULL\n", ClipboardSqlKind.Values, true, Int);
            request.NullText = NullTextRule.Text;
            Assert.False(ClipboardSqlBuilder.Build(request).IsValid);
        }

        [Fact]
        public void Nulls_NonTextTypesAcceptNullAsSqlNull()
        {
            var result = Build("n\td\n\tNULL\n", ClipboardSqlKind.Values, true, Int, ClipboardColumnType.Date);
            Assert.Equal("VALUES\n(NULL, NULL)\n", result.Script);
        }

        // ---- type checking ----

        [Fact]
        public void BadValuesNameTheRowAndColumnAndStopTheScript()
        {
            var result = Build("Id\tQty\n1\t5\n2\tabc\n", ClipboardSqlKind.Values, true, Int, Int);
            Assert.False(result.IsValid);
            Assert.Null(result.Script);
            Assert.Single(result.Errors);
            Assert.Contains("แถวที่ 3", result.Errors[0]);
            Assert.Contains("คอลัมน์ 2", result.Errors[0]);
            Assert.Contains("Qty", result.Errors[0]);
            Assert.Contains("abc", result.Errors[0]);
        }

        [Fact]
        public void RowNumbersCountTheHeaderRowOnlyWhenThereIsOne()
        {
            Assert.Contains("แถวที่ 2", Build("1\nabc\n", ClipboardSqlKind.Values, false, Int).Errors[0]);
            Assert.Contains("แถวที่ 3", Build("h\n1\nabc\n", ClipboardSqlKind.Values, true, Int).Errors[0]);
        }

        [Fact]
        public void EveryBadCellIsReportedUpToACap()
        {
            string text = "n\n" + string.Concat(Enumerable.Repeat("x\n", 60));
            var result = Build(text, ClipboardSqlKind.Values, true, Int);
            Assert.False(result.IsValid);
            Assert.True(result.Errors.Count <= ClipboardSqlBuilder.MaxReportedProblems + 1);
            Assert.Contains("60", result.Errors.Last());
        }

        [Fact]
        public void ColumnsWithoutATypeAreAnError()
        {
            var request = Request("a\tb\n1\t2\n", ClipboardSqlKind.Values);
            request.ColumnTypes = new[] { Txt };
            Assert.False(ClipboardSqlBuilder.Build(request).IsValid);
        }

        [Fact]
        public void VarcharWithNonAsciiTextWarnsBecauseOfTheCodePage()
        {
            var result = Build("a\nสวัสดี\n", ClipboardSqlKind.Values, true, ClipboardColumnType.Varchar);
            Assert.True(result.IsValid);
            Assert.Equal("VALUES\n('สวัสดี')\n", result.Script);
            Assert.Contains(result.Warnings, w => w.Contains("varchar") && w.Contains("nvarchar"));
        }

        [Fact]
        public void DateWarningsAreKept()
        {
            var request = Request("d\n31/01/2567\n", ClipboardSqlKind.Values, true, ClipboardColumnType.Date);
            request.DateFormat = DateInputFormat.DayMonthYear;
            var result = ClipboardSqlBuilder.Build(request);
            Assert.True(result.IsValid);
            Assert.Contains(result.Warnings, w => w.Contains("พ.ศ.") && w.Contains("แถวที่ 2"));
        }

        [Theory]
        [InlineData("th-TH")]
        [InlineData("de-DE")]
        [InlineData("ar-SA")]
        public void TheScriptDoesNotDependOnTheMachinesCulture(string culture)
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                var request = Request("n\td\tt\n1234.5\t01/02/2024\t01/02/2024 13:05:09\n", ClipboardSqlKind.Values, true,
                    ClipboardColumnType.Decimal, ClipboardColumnType.Date, ClipboardColumnType.DateTime);
                request.DateFormat = DateInputFormat.DayMonthYear;
                Assert.Equal("VALUES\n(1234.5, '2024-02-01', '2024-02-01T13:05:09.000')\n", ClipboardSqlBuilder.Build(request).Script);
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        // ---- preview ----

        [Fact]
        public void PreviewRendersOnlyTheFirstRowsButStillChecksEverything()
        {
            string text = "n\n1\n2\n3\n4\nbad\n";
            var request = Request(text, ClipboardSqlKind.Values, true, Int);
            request.PreviewRows = 2;
            var result = ClipboardSqlBuilder.Build(request);
            Assert.False(result.IsValid); // row 6 is wrong, though the preview would not show it

            request = Request("n\n1\n2\n3\n4\n", ClipboardSqlKind.Values, true, Int);
            request.PreviewRows = 2;
            result = ClipboardSqlBuilder.Build(request);
            Assert.True(result.IsValid);
            Assert.True(result.IsTruncated);
            Assert.Equal("VALUES\n(1),\n(2)\n", result.Script);
            Assert.Equal(4, result.RowCount);
        }

        [Fact]
        public void PreviewOfAnInListShowsTheFirstValues()
        {
            var request = Request("n\n1\n2\n3\n", ClipboardSqlKind.In, true, Int);
            request.PreviewRows = 2;
            var result = ClipboardSqlBuilder.Build(request);
            Assert.True(result.IsTruncated);
            Assert.Equal("IN (1, 2)\n", result.Script);
        }

        [Fact]
        public void RowCountIsDataRowsOnly()
        {
            Assert.Equal(2, Build("h\n1\n2\n", ClipboardSqlKind.Values, true, Int).RowCount);
            Assert.Equal(3, Build("h\n1\n2\n", ClipboardSqlKind.Values, false, Txt).RowCount);
        }

        [Fact]
        public void HeaderNamesForTheDialog()
        {
            var rows = DelimitedTextParser.Parse("Code\t\tQty\n1\t2\t3\n").Rows;
            Assert.Equal(new[] { "Code", "Column2", "Qty" }, ClipboardSqlBuilder.HeaderNames(rows, true, 3));
            Assert.Equal(new[] { "Column1", "Column2", "Column3" }, ClipboardSqlBuilder.HeaderNames(rows, false, 3));
        }
    }
}
