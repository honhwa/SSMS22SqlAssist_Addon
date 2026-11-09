using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlAssist.Core.Hints
{
    /// <summary>The calling syntax of the built-in functions the parameter hint knows. Names and order are the ones in Microsoft's
    /// T-SQL reference; a function that is not here gets no hint (nothing is guessed), and so does every function whose syntax
    /// is not a plain comma-separated list (<see cref="IsSpecialSyntax"/>).</summary>
    public static class BuiltInSignatures
    {
        // "?name" = optional, "*name" = optional and repeatable (the last parameter only).
        private static readonly Dictionary<string, FunctionSignature> Catalog = Build(new[]
        {
            F("SUBSTRING", "Returns part of a character, binary, text or image expression.", "expression", "start", "length"),
            F("REPLACE", "Replaces every occurrence of a string value with another string value.", "string_expression", "string_pattern", "string_replacement"),
            F("CHARINDEX", "Returns the starting position of one expression found within another expression.", "expressionToFind", "expressionToSearch", "?start_location"),
            F("PATINDEX", "Returns the starting position of the first occurrence of a pattern in an expression.", "'%pattern%'", "expression"),
            F("LEFT", "Returns the left part of a character string with the specified number of characters.", "character_expression", "integer_expression"),
            F("RIGHT", "Returns the right part of a character string with the specified number of characters.", "character_expression", "integer_expression"),
            F("LEN", "Returns the number of characters of a string expression, excluding trailing spaces.", "string_expression"),
            F("UPPER", "Returns a character expression with lowercase data converted to uppercase.", "character_expression"),
            F("LOWER", "Returns a character expression with uppercase data converted to lowercase.", "character_expression"),
            F("LTRIM", "Removes leading spaces from a string.", "character_expression"),
            F("RTRIM", "Removes trailing spaces from a string.", "character_expression"),
            F("REVERSE", "Returns a string value in reverse order.", "string_expression"),
            F("REPLICATE", "Repeats a string value a specified number of times.", "string_expression", "integer_expression"),
            F("STUFF", "Inserts a string into another string, deleting a specified number of characters first.", "character_expression", "start", "length", "replaceWith_expression"),
            F("CONCAT", "Joins two or more string values into one string.", "argument1", "argument2", "*argumentN"),
            F("CONCAT_WS", "Joins two or more string values with a separator between them.", "separator", "argument1", "argument2", "*argumentN"),
            F("FORMAT", "Returns a value formatted with the specified format and optional culture.", "value", "format", "?culture"),
            F("ISNULL", "Replaces NULL with the specified replacement value.", "check_expression", "replacement_value"),
            F("COALESCE", "Returns the first expression that does not evaluate to NULL.", "expression", "*...n"),
            F("NULLIF", "Returns NULL if the two expressions are equal, otherwise the first expression.", "expression", "expression"),
            F("IIF", "Returns one of two values depending on whether a Boolean expression is true or false.", "boolean_expression", "true_value", "false_value"),
            F("CHOOSE", "Returns the item at the specified index from a list of values.", "index", "val_1", "val_2", "*val_n"),
            F("DATEADD", "Adds a number (a signed integer) to a datepart of an input date and returns the modified value.", "datepart", "number", "date"),
            F("DATEDIFF", "Returns the number of datepart boundaries crossed between two dates (an int).", "datepart", "startdate", "enddate"),
            F("DATEDIFF_BIG", "Returns the number of datepart boundaries crossed between two dates (a bigint).", "datepart", "startdate", "enddate"),
            F("DATEPART", "Returns an integer that represents the specified datepart of a date.", "datepart", "date"),
            F("DATENAME", "Returns a string that represents the specified datepart of a date.", "datepart", "date"),
            F("YEAR", "Returns an integer that represents the year of a date.", "date"),
            F("MONTH", "Returns an integer that represents the month of a date.", "date"),
            F("DAY", "Returns an integer that represents the day of a date.", "date"),
            F("EOMONTH", "Returns the last day of the month containing a date, with an optional offset.", "start_date", "?month_to_add"),
            F("DATEFROMPARTS", "Returns a date value for the specified year, month and day.", "year", "month", "day"),
            F("ROUND", "Returns a numeric value, rounded to the specified length or precision.", "numeric_expression", "length", "?function"),
            F("ABS", "Returns the absolute (positive) value of a numeric expression.", "numeric_expression"),
            F("CEILING", "Returns the smallest integer greater than or equal to a numeric expression.", "numeric_expression"),
            F("FLOOR", "Returns the largest integer less than or equal to a numeric expression.", "numeric_expression"),
            F("POWER", "Returns the value of an expression raised to the specified power.", "float_expression", "y"),
            F("ISNUMERIC", "Determines whether an expression is a valid numeric type.", "expression"),
            F("ISDATE", "Determines whether an expression is a valid date, time or datetime value.", "expression"),
            F("CONVERT", "Converts an expression of one data type to another.", "data_type [(length)]", "expression", "?style"),
            F("TRY_CONVERT", "Converts an expression to the specified data type, or returns NULL when the conversion fails.", "data_type [(length)]", "expression", "?style"),
        });

        // Functions whose arguments are not a comma-separated list (CAST(x AS type), TRIM(chars FROM s), ...). The comma rule would
        // count and highlight them wrongly, so they are skipped on purpose until each gets handling of its own.
        private static readonly HashSet<string> SpecialSyntax = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CAST", "TRY_CAST", "PARSE", "TRY_PARSE", "TRIM", "EXTRACT", "STRING_AGG", "JSON_OBJECT", "JSON_ARRAY",
        };

        /// <summary>The signature of a built-in function by name (no schema), or null when it is unknown or has special syntax.</summary>
        public static FunctionSignature Find(string name) =>
            name != null && Catalog.TryGetValue(name, out var signature) ? signature : null;

        /// <summary>A function whose syntax is not a plain argument list: no hint is shown for it, on purpose.</summary>
        public static bool IsSpecialSyntax(string name) => name != null && SpecialSyntax.Contains(name);

        public static IEnumerable<string> Names => Catalog.Keys;

        private static Entry F(string name, string summary, params string[] parameters) => new Entry(name, summary, parameters);

        private sealed class Entry
        {
            public Entry(string name, string summary, string[] parameters) { Name = name; Summary = summary; Parameters = parameters; }
            public string Name { get; }
            public string Summary { get; }
            public string[] Parameters { get; }
        }

        private static Dictionary<string, FunctionSignature> Build(IEnumerable<Entry> entries)
        {
            var result = new Dictionary<string, FunctionSignature>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                var parameters = e.Parameters.Select(p =>
                {
                    if (p.StartsWith("*", StringComparison.Ordinal)) return new SignatureParameter(p.Substring(1), isOptional: true, isVariadic: true);
                    if (p.StartsWith("?", StringComparison.Ordinal)) return new SignatureParameter(p.Substring(1), isOptional: true);
                    return new SignatureParameter(p);
                }).ToArray();
                result[e.Name] = new FunctionSignature(e.Name, parameters, e.Summary, isBuiltIn: true);
            }
            return result;
        }
    }
}
