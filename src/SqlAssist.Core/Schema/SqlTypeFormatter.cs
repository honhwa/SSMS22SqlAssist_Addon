namespace SqlAssist.Core.Schema
{
    public static class SqlTypeFormatter
    {
        /// <summary>Renders a sys.columns type the way it is declared: nvarchar(50), varchar(max), decimal(18,2).
        /// <paramref name="maxLength"/> is sys.columns.max_length (bytes, -1 for max).</summary>
        public static string Format(string type, short maxLength, byte precision, byte scale)
        {
            switch (type)
            {
                case "varchar": case "char": case "varbinary": case "binary":
                    return type + "(" + (maxLength == -1 ? "max" : maxLength.ToString()) + ")";
                case "nvarchar": case "nchar":
                    return type + "(" + (maxLength == -1 ? "max" : (maxLength / 2).ToString()) + ")";
                case "decimal": case "numeric":
                    return type + "(" + precision + "," + scale + ")";
                case "datetime2": case "time": case "datetimeoffset":
                    // 7 is the default fractional-second precision, which declarations usually leave out.
                    return scale == 7 ? type : type + "(" + scale + ")";
                default:
                    return type;
            }
        }
    }
}
