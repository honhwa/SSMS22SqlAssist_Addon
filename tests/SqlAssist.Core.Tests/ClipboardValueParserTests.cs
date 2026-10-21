using System;
using System.Globalization;
using SqlAssist.Core.Clipboard;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class ClipboardValueParserTests
    {
        private static CellParse Parse(string cell, ClipboardColumnType type, DateInputFormat format = DateInputFormat.Iso) =>
            ClipboardValueParser.Parse(cell, type, format);

        [Theory]
        [InlineData("00123")]
        [InlineData("  padded  ")]
        [InlineData("N'x' -- ; DROP")]
        [InlineData("สวัสดี")]
        [InlineData("line1\nline2")]
        public void TextIsKeptExactly(string cell)
        {
            foreach (var type in new[] { ClipboardColumnType.Text, ClipboardColumnType.Varchar })
            {
                var result = Parse(cell, type);
                Assert.True(result.IsValid);
                Assert.Equal(cell, result.Value);
            }
        }

        [Theory]
        [InlineData("123", 123)]
        [InlineData(" 12 ", 12)]
        [InlineData("+5", 5)]
        [InlineData("-7", -7)]
        [InlineData("007", 7)]
        public void Int(string cell, int expected)
        {
            var result = Parse(cell, ClipboardColumnType.Int);
            Assert.True(result.IsValid, result.Error);
            Assert.Equal(expected, result.Value);
        }

        [Theory]
        [InlineData("1,234")]
        [InlineData("1.5")]
        [InlineData("12abc")]
        [InlineData("1e3")]
        [InlineData("2147483648")]
        [InlineData("๑๒๓")]
        [InlineData("--1")]
        public void IntRejects(string cell)
        {
            var result = Parse(cell, ClipboardColumnType.Int);
            Assert.False(result.IsValid);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
        }

        [Fact]
        public void BigInt()
        {
            Assert.Equal(9223372036854775807L, Parse("9223372036854775807", ClipboardColumnType.BigInt).Value);
            Assert.False(Parse("9223372036854775808", ClipboardColumnType.BigInt).IsValid);
        }

        [Theory]
        [InlineData("12.50", "12.50")]
        [InlineData(".5", "0.5")]
        [InlineData("5.", "5")]
        [InlineData("-3.14", "-3.14")]
        [InlineData(" 7 ", "7")]
        public void Decimal(string cell, string expected)
        {
            var result = Parse(cell, ClipboardColumnType.Decimal);
            Assert.True(result.IsValid, result.Error);
            Assert.Equal(expected, ((decimal)result.Value).ToString(CultureInfo.InvariantCulture));
        }

        [Theory]
        [InlineData("1,5")]
        [InlineData("1,234.50")]
        [InlineData("1e5")]
        [InlineData("abc")]
        [InlineData("1.2.3")]
        public void DecimalRejects(string cell) => Assert.False(Parse(cell, ClipboardColumnType.Decimal).IsValid);

        [Theory]
        [InlineData("1.5", 1.5)]
        [InlineData("1e5", 100000d)]
        [InlineData("-2.5E-2", -0.025)]
        public void Float(string cell, double expected) => Assert.Equal(expected, Parse(cell, ClipboardColumnType.Float).Value);

        [Theory]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("1,5")]
        [InlineData("x")]
        public void FloatRejects(string cell) => Assert.False(Parse(cell, ClipboardColumnType.Float).IsValid);

        [Theory]
        [InlineData("1", true)]
        [InlineData("0", false)]
        [InlineData("true", true)]
        [InlineData("FALSE", false)]
        [InlineData(" True ", true)]
        public void Bit(string cell, bool expected) => Assert.Equal(expected, Parse(cell, ClipboardColumnType.Bit).Value);

        [Theory]
        [InlineData("2")]
        [InlineData("yes")]
        [InlineData("")]
        public void BitRejects(string cell) => Assert.False(Parse(cell, ClipboardColumnType.Bit).IsValid);

        [Theory]
        [InlineData("2024-01-31", DateInputFormat.Iso, 2024, 1, 31)]
        [InlineData("2024-1-3", DateInputFormat.Iso, 2024, 1, 3)]
        [InlineData("31/01/2024", DateInputFormat.DayMonthYear, 2024, 1, 31)]
        [InlineData("1/2/2024", DateInputFormat.DayMonthYear, 2024, 2, 1)]
        [InlineData("1/2/2024", DateInputFormat.MonthDayYear, 2024, 1, 2)]
        [InlineData("12/31/2024", DateInputFormat.MonthDayYear, 2024, 12, 31)]
        public void Date(string cell, DateInputFormat format, int y, int m, int d)
        {
            var result = Parse(cell, ClipboardColumnType.Date, format);
            Assert.True(result.IsValid, result.Error);
            Assert.Equal(new DateTime(y, m, d), result.Value);
        }

        [Theory]
        [InlineData("31/01/2024", DateInputFormat.Iso)]
        [InlineData("2024-01-31", DateInputFormat.DayMonthYear)]
        [InlineData("13/01/2024", DateInputFormat.MonthDayYear)]
        [InlineData("2024-02-30", DateInputFormat.Iso)]
        [InlineData("2024-01-31 10:00", DateInputFormat.Iso)] // a date column must not drop the time silently
        [InlineData("31 Jan 2024", DateInputFormat.Iso)]
        [InlineData("20240131", DateInputFormat.Iso)]
        public void DateRejects(string cell, DateInputFormat format)
        {
            var result = Parse(cell, ClipboardColumnType.Date, format);
            Assert.False(result.IsValid);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
        }

        [Theory]
        [InlineData("2024-01-31 13:05:09", 13, 5, 9, 0)]
        [InlineData("2024-01-31T13:05:09", 13, 5, 9, 0)]
        [InlineData("2024-01-31 13:05", 13, 5, 0, 0)]
        [InlineData("2024-01-31", 0, 0, 0, 0)]
        [InlineData("2024-01-31 13:05:09.120", 13, 5, 9, 120)]
        [InlineData("2024-01-31 13:05:09.5", 13, 5, 9, 500)]
        public void DateTimeValues(string cell, int h, int min, int s, int ms)
        {
            foreach (var type in new[] { ClipboardColumnType.DateTime, ClipboardColumnType.DateTime2 })
            {
                var result = Parse(cell, type);
                Assert.True(result.IsValid, result.Error);
                Assert.Equal(new DateTime(2024, 1, 31, h, min, s, ms), result.Value);
            }
        }

        [Fact]
        public void DateTimeWithDayFirstFormat()
        {
            Assert.Equal(new DateTime(2024, 1, 31, 13, 5, 9), Parse("31/01/2024 13:05:09", ClipboardColumnType.DateTime, DateInputFormat.DayMonthYear).Value);
            Assert.Equal(new DateTime(2024, 1, 31), Parse("31/01/2024", ClipboardColumnType.DateTime2, DateInputFormat.DayMonthYear).Value);
        }

        [Theory]
        [InlineData("2024-01-31 25:00")]
        [InlineData("2024-01-31 13:60")]
        [InlineData("2024-01-31 1pm")]
        [InlineData("tomorrow")]
        public void DateTimeRejects(string cell) => Assert.False(Parse(cell, ClipboardColumnType.DateTime2).IsValid);

        [Fact]
        public void DateTimeHasThreeDigitsAndDateTime2Seven()
        {
            string fine = "2024-01-31 13:05:09.1234567";
            Assert.False(Parse(fine, ClipboardColumnType.DateTime).IsValid);
            Assert.Contains("datetime2", Parse(fine, ClipboardColumnType.DateTime).Error);
            Assert.True(Parse(fine, ClipboardColumnType.DateTime2).IsValid);
            Assert.False(Parse("2024-01-31 13:05:09.12345678", ClipboardColumnType.DateTime2).IsValid);
        }

        [Fact]
        public void DateTimeRangeFollowsTheSqlType()
        {
            Assert.False(Parse("1752-12-31", ClipboardColumnType.DateTime).IsValid);
            Assert.True(Parse("1753-01-01", ClipboardColumnType.DateTime).IsValid);
            Assert.True(Parse("0001-01-01", ClipboardColumnType.DateTime2).IsValid);
            Assert.True(Parse("9999-12-31", ClipboardColumnType.Date).IsValid);
        }

        [Fact]
        public void BuddhistLookingYearsAreAcceptedButWarnedAbout()
        {
            var result = Parse("31/01/2567", ClipboardColumnType.Date, DateInputFormat.DayMonthYear);
            Assert.True(result.IsValid);
            Assert.Contains("พ.ศ.", result.Warning);
            Assert.Null(Parse("2999-12-31", ClipboardColumnType.Date).Warning);
            Assert.Null(Parse("2024-01-31", ClipboardColumnType.Date).Warning);
        }

        [Theory]
        [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
        [InlineData("{0F8FAD5B-D9CB-469F-A165-70867728950E}")]
        [InlineData(" 0f8fad5bd9cb469fa16570867728950e ")]
        public void Guid(string cell)
        {
            Assert.Equal(System.Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), Parse(cell, ClipboardColumnType.UniqueIdentifier).Value);
        }

        [Theory]
        [InlineData("not-a-guid")]
        [InlineData("0f8fad5b-d9cb-469f-a165")]
        public void GuidRejects(string cell) => Assert.False(Parse(cell, ClipboardColumnType.UniqueIdentifier).IsValid);

        [Theory]
        [InlineData("th-TH")]
        [InlineData("de-DE")]
        [InlineData("ar-SA")]
        [InlineData("fa-IR")]
        public void NeverFollowsTheMachinesRegionalSettings(string culture)
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                Assert.Equal(1234.5m, Parse("1234.5", ClipboardColumnType.Decimal).Value);
                Assert.False(Parse("1234,5", ClipboardColumnType.Decimal).IsValid);
                Assert.Equal(1.5, Parse("1.5", ClipboardColumnType.Float).Value);
                Assert.Equal(new DateTime(2024, 1, 31, 13, 5, 9), Parse("2024-01-31 13:05:09", ClipboardColumnType.DateTime2).Value);
                Assert.Equal(new DateTime(2024, 2, 1), Parse("01/02/2024", ClipboardColumnType.Date, DateInputFormat.DayMonthYear).Value);
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }
    }
}
