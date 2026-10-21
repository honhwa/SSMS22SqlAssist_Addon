using System.Linq;
using SqlAssist.Core.Clipboard;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class DelimitedTextParserTests
    {
        private static string[][] Rows(string text) => DelimitedTextParser.Parse(text).Rows.ToArray();

        [Fact]
        public void ReadsExcelRowsAndTabs()
        {
            var table = DelimitedTextParser.Parse("Code\tQty\r\n00123\t5\r\n00456\t7\r\n");
            Assert.True(table.IsValid);
            Assert.Equal(3, table.Rows.Count);
            Assert.Equal(2, table.ColumnCount);
            Assert.Equal(new[] { "00123", "5" }, table.Rows[1]);
            Assert.Equal(new[] { "00456", "7" }, table.Rows[2]);
        }

        [Theory]
        [InlineData("a\tb\nc\td")]
        [InlineData("a\tb\r\nc\td")]
        [InlineData("a\tb\rc\td\r")]
        [InlineData("a\tb\nc\td\n")]
        public void AcceptsAnyLineEndingWithOrWithoutTheFinalOne(string text)
        {
            Assert.Equal(new[] { new[] { "a", "b" }, new[] { "c", "d" } }, Rows(text));
        }

        [Fact]
        public void IgnoresBlankLinesAtTheEndButNotInTheMiddle()
        {
            Assert.Equal(new[] { new[] { "a" }, new[] { "b" } }, Rows("a\r\nb\r\n\r\n\r\n"));
            Assert.Equal(new[] { new[] { "a" }, new[] { "" }, new[] { "b" } }, Rows("a\r\n\r\nb\r\n"));
        }

        [Fact]
        public void KeepsEmptyCells()
        {
            Assert.Equal(new[] { new[] { "a", "", "c" }, new[] { "", "", "" } }, Rows("a\t\tc\r\n\t\t\r\n"));
            Assert.Equal(new[] { new[] { "a", "" } }, Rows("a\t\r\n"));
        }

        [Fact]
        public void QuotedCellsMayHoldTabsNewlinesAndQuotes()
        {
            var rows = Rows("\"a\tb\"\t\"line1\nline2\"\t\"say \"\"hi\"\"\"\r\nx\ty\tz\r\n");
            Assert.Equal(2, rows.Length);
            Assert.Equal(new[] { "a\tb", "line1\nline2", "say \"hi\"" }, rows[0]);
            Assert.Equal(new[] { "x", "y", "z" }, rows[1]);
        }

        [Fact]
        public void QuotedCellKeepsItsOwnWindowsNewlines()
        {
            Assert.Equal(new[] { new[] { "a\r\nb", "c" } }, Rows("\"a\r\nb\"\tc"));
        }

        [Fact]
        public void AQuoteInsideAnUnquotedCellIsLiteral()
        {
            Assert.Equal(new[] { new[] { "5\" pipe", "x" } }, Rows("5\" pipe\tx"));
        }

        [Fact]
        public void AnEmptyQuotedCellIsAnEmptyCell()
        {
            Assert.Equal(new[] { new[] { "", "x" } }, Rows("\"\"\tx"));
        }

        [Fact]
        public void EmptyInputHasNoRows()
        {
            foreach (string text in new[] { "", "\r\n", null })
            {
                var table = DelimitedTextParser.Parse(text);
                Assert.True(table.IsValid);
                Assert.Empty(table.Rows);
                Assert.Equal(0, table.ColumnCount);
            }
        }

        [Fact]
        public void RowsWithADifferentWidthAreErrorsNotPadding()
        {
            var table = DelimitedTextParser.Parse("a\tb\tc\r\n1\t2\r\n4\t5\t6\r\n\r\n7\t8\t9\t10\r\n");
            Assert.False(table.IsValid);
            Assert.Equal(3, table.Errors.Count);
            Assert.Contains("แถวที่ 2", table.Errors[0]);
            Assert.Contains("2 คอลัมน์", table.Errors[0]);
            Assert.Contains("3 คอลัมน์", table.Errors[0]);
            Assert.Contains("แถวที่ 4", table.Errors[1]);
            Assert.Contains("แถวที่ 5", table.Errors[2]);
        }

        [Fact]
        public void AnUnclosedQuoteIsAnErrorAndGivesNoRows()
        {
            var table = DelimitedTextParser.Parse("a\t\"b\r\nc\td");
            Assert.False(table.IsValid);
            Assert.Contains("quote", table.Errors[0]);
            Assert.Empty(table.Rows);
        }

        [Fact]
        public void TextAfterAClosingQuoteIsKeptAndWarnedAbout()
        {
            var table = DelimitedTextParser.Parse("\"abc\"def\tx");
            Assert.True(table.IsValid);
            Assert.Equal(new[] { "abcdef", "x" }, table.Rows[0]);
            Assert.Single(table.Warnings);
            Assert.Contains("แถวที่ 1", table.Warnings[0]);
        }

        [Fact]
        public void RefusesInputOverTheCharacterLimit()
        {
            var table = DelimitedTextParser.Parse(new string('a', DelimitedTextParser.MaxChars + 1));
            Assert.False(table.IsValid);
            Assert.Empty(table.Rows);
            Assert.Contains("ใหญ่เกิน", table.Errors[0]);
        }

        [Fact]
        public void RefusesMoreRowsThanTheLimit()
        {
            string text = string.Concat(Enumerable.Repeat("1\r\n", DelimitedTextParser.MaxRows + 1));
            var table = DelimitedTextParser.Parse(text);
            Assert.False(table.IsValid);
            Assert.Empty(table.Rows);
            Assert.Contains("แถว", table.Errors[0]);
        }

        [Fact]
        public void RefusesMoreColumnsThanTheLimit()
        {
            string text = string.Join("\t", Enumerable.Repeat("x", DelimitedTextParser.MaxColumns + 1));
            var table = DelimitedTextParser.Parse(text);
            Assert.False(table.IsValid);
            Assert.Contains("คอลัมน์", table.Errors[0]);
        }

        [Theory]
        [InlineData(',')]
        [InlineData(';')]
        [InlineData('|')]
        public void ReadsCsvStyleTextWithAnyOfTheOfferedDelimiters(char delimiter)
        {
            string d = delimiter.ToString();
            string text = "code" + d + "note" + d + "qty\r\n" +
                          "00123" + d + "\"a" + d + "b\"" + d + "5\r\n" +
                          "00456" + d + "\"say \"\"hi\"\"\nthere\"" + d + "\r\n";
            var table = DelimitedTextParser.Parse(text, delimiter);
            Assert.True(table.IsValid, string.Join(" ", table.Errors));
            Assert.Equal(3, table.Rows.Count);
            Assert.Equal(new[] { "code", "note", "qty" }, table.Rows[0]);
            Assert.Equal(new[] { "00123", "a" + d + "b", "5" }, table.Rows[1]);   // the delimiter inside quotes is data
            Assert.Equal(new[] { "00456", "say \"hi\"\nthere", "" }, table.Rows[2]);
        }

        [Fact]
        public void ADelimiterOtherThanTheChosenOneIsJustText()
        {
            // Excel data parsed while the comma option is chosen stays one cell per line: nothing is guessed.
            var table = DelimitedTextParser.Parse("a\tb\r\nc\td", ',');
            Assert.Equal(new[] { new[] { "a\tb" }, new[] { "c\td" } }, table.Rows.ToArray());
            // and a single column of names that contain commas stays a single column with the default (tab).
            Assert.Equal(new[] { new[] { "Smith, John" }, new[] { "Doe, Jane" } }, Rows("Smith, John\r\nDoe, Jane"));
        }

        [Fact]
        public void ErrorsAreCappedSoAHugeBadPasteStaysReadable()
        {
            string text = "a\tb\r\n" + string.Concat(Enumerable.Repeat("x\r\n", 500));
            var table = DelimitedTextParser.Parse(text);
            Assert.False(table.IsValid);
            Assert.True(table.Errors.Count <= DelimitedTextParser.MaxReportedProblems + 1);
            Assert.Contains("500", table.Errors.Last()); // "... and N more" names the real count
        }
    }
}
