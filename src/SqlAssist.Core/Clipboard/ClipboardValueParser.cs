using System;
using System.Globalization;

namespace SqlAssist.Core.Clipboard
{
    /// <summary>One cell read as a SQL type. <see cref="Value"/> is a string, int, long, decimal, double, bool, DateTime or
    /// Guid; it is only meaningful when <see cref="IsValid"/>.</summary>
    public sealed class CellParse
    {
        public CellParse(object value, string error = null, string warning = null)
        {
            Value = value; Error = error; Warning = warning;
        }

        public object Value { get; }
        /// <summary>Why the cell is not a value of the type, in Thai; null when it is.</summary>
        public string Error { get; }
        /// <summary>The cell is accepted, but looks suspicious.</summary>
        public string Warning { get; }
        public bool IsValid => Error == null;
    }

    /// <summary>Turns pasted text into typed values. Always strict and always invariant: the digits are ASCII, the decimal
    /// separator is a dot, dates match one explicit format, and nothing depends on the machine's regional settings
    /// (a Thai Windows must not read 2024 as 2567, nor a German one 1,5 as 15).</summary>
    public static class ClipboardValueParser
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly DateTime DateTimeMin = new DateTime(1753, 1, 1);
        private static readonly DateTime DateTimeMax = new DateTime(9999, 12, 31, 23, 59, 59, 997);

        public static CellParse Parse(string cell, ClipboardColumnType type, DateInputFormat dateFormat)
        {
            cell = cell ?? "";
            if (type == ClipboardColumnType.Text || type == ClipboardColumnType.Varchar) return new CellParse(cell);

            string text = cell.Trim();
            switch (type)
            {
                case ClipboardColumnType.Int:
                    return int.TryParse(text, NumberStyles.AllowLeadingSign, Invariant, out int i)
                        ? new CellParse(i) : Fail("不是 int 整数（范围 -2,147,483,648 到 2,147,483,647，不可含千分位逗号）");
                case ClipboardColumnType.BigInt:
                    return long.TryParse(text, NumberStyles.AllowLeadingSign, Invariant, out long l)
                        ? new CellParse(l) : Fail("不是 bigint 整数（不可含千分位逗号）");
                case ClipboardColumnType.Decimal:
                    return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Invariant, out decimal m)
                        ? new CellParse(m) : Fail("不是定点小数（小数用 .，不可含千分位逗号，也不可用 e）");
                case ClipboardColumnType.Float:
                    return double.TryParse(text, NumberStyles.Float, Invariant, out double d) && !double.IsNaN(d) && !double.IsInfinity(d)
                        ? new CellParse(d) : Fail("不是 float 数值（小数用 .，可用 e，例如 1e5）");
                case ClipboardColumnType.Bit:
                    if (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase)) return new CellParse(true);
                    if (text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase)) return new CellParse(false);
                    return Fail("不是 bit（请用 1、0、true 或 false）");
                case ClipboardColumnType.UniqueIdentifier:
                    return Guid.TryParse(text, out Guid g) ? new CellParse(g) : Fail("不是 GUID");
                case ClipboardColumnType.Date:
                case ClipboardColumnType.DateTime:
                case ClipboardColumnType.DateTime2:
                    return ParseDate(text, type, dateFormat);
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static CellParse ParseDate(string text, ClipboardColumnType type, DateInputFormat format)
        {
            string date = DatePattern(format);
            string[] patterns = type == ClipboardColumnType.Date ? new[] { date } : TimePatterns(date, format == DateInputFormat.Iso);
            if (!DateTime.TryParseExact(text, patterns, Invariant, DateTimeStyles.None, out DateTime value))
                return Fail(type == ClipboardColumnType.Date
                    ? "不符合日期格式 " + Describe(format) + "（不含时间）"
                    : "不符合日期时间格式 " + Describe(format) + " [HH:mm[:ss[.fffffff]]]");

            if (type == ClipboardColumnType.DateTime)
            {
                if (value.Ticks % TimeSpan.TicksPerMillisecond != 0)
                    return Fail("秒的小数位超过 datetime 可存储的 3 位 —— 请改用 datetime2");
                if (value < DateTimeMin || value > DateTimeMax)
                    return Fail("datetime 的日期范围是 1753-01-01 至 9999-12-31 23:59:59.997（其他范围请用 datetime2）");
            }
            // A year in this range is more likely a Buddhist-era year (2567 = 2024) than a real far-future date.
            string warning = value.Year >= 2400 && value.Year <= 2700
                ? "年份 " + value.Year + " 看起来是佛历年份（公历 = 佛历 - 543）—— 请确认是否有意使用该年份"
                : null;
            return new CellParse(value, null, warning);
        }

        private static string DatePattern(DateInputFormat format)
        {
            switch (format)
            {
                case DateInputFormat.DayMonthYear: return "d/M/yyyy";
                case DateInputFormat.MonthDayYear: return "M/d/yyyy";
                default: return "yyyy-M-d";
            }
        }

        private static string[] TimePatterns(string date, bool iso)
        {
            var patterns = new System.Collections.Generic.List<string> { date };
            foreach (string separator in iso ? new[] { " ", "'T'" } : new[] { " " })
            {
                patterns.Add(date + separator + "H:mm");
                patterns.Add(date + separator + "H:mm:ss");
                patterns.Add(date + separator + "H:mm:ss.FFFFFFF");
            }
            return patterns.ToArray();
        }

        private static string Describe(DateInputFormat format)
        {
            switch (format)
            {
                case DateInputFormat.DayMonthYear: return "dd/MM/yyyy";
                case DateInputFormat.MonthDayYear: return "MM/dd/yyyy";
                default: return "yyyy-MM-dd";
            }
        }

        private static CellParse Fail(string error) => new CellParse(null, error);
    }
}
