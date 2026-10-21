namespace SqlAssist.Core.Clipboard
{
    /// <summary>The SQL type a pasted column is read as. Text is the default: it keeps every character, so a code like
    /// 00123 never turns into 123.</summary>
    public enum ClipboardColumnType
    {
        /// <summary>nvarchar: written as N'...'.</summary>
        Text,
        /// <summary>varchar: written as '...'.</summary>
        Varchar,
        Int,
        BigInt,
        Decimal,
        Float,
        Bit,
        Date,
        DateTime,
        DateTime2,
        UniqueIdentifier,
    }

    /// <summary>What a completely empty cell (nothing between two tabs) means.</summary>
    public enum EmptyCellRule
    {
        /// <summary>SQL NULL.</summary>
        Null,
        /// <summary>The empty string ''. Only text columns can hold it.</summary>
        EmptyString,
    }

    /// <summary>What a cell holding exactly the four letters NULL means. Other spellings (Null, null) are always text.</summary>
    public enum NullTextRule
    {
        /// <summary>SQL NULL.</summary>
        Null,
        /// <summary>The text 'NULL'. Only text columns can hold it.</summary>
        Text,
    }

    /// <summary>How dates are written in the pasted cells. Never read from the machine's regional settings.</summary>
    public enum DateInputFormat
    {
        /// <summary>2024-01-31</summary>
        Iso,
        /// <summary>31/01/2024</summary>
        DayMonthYear,
        /// <summary>01/31/2024</summary>
        MonthDayYear,
    }

    public enum ClipboardSqlKind
    {
        /// <summary>IN (v1, v2, ...) for one column.</summary>
        In,
        /// <summary>VALUES (...), (...)</summary>
        Values,
        /// <summary>CREATE TABLE #Temp (...) and the INSERTs that fill it.</summary>
        TempTable,
    }
}
