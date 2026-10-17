using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SqlAssist.Core.Snippets
{
    public sealed class SnippetLiteral
    {
        public SnippetLiteral(string name, string description) { Name = name; Description = description; }
        /// <summary>DATE, USER …; written in a snippet as $DATE$ (case does not matter).</summary>
        public string Name { get; }
        public string Marker => "$" + Name + "$";
        public string Description { get; }
    }

    /// <summary>The $NAME$ placeholders a snippet may contain. CARET is where the caret goes; the others are
    /// replaced with values when the snippet expands.</summary>
    public static class SnippetLiterals
    {
        public const string Caret = "CARET";
        /// <summary>Matches the caret marker: $CARET$ or the original $end$.</summary>
        public const string CaretPattern = @"\$(caret|end)\$";

        public static readonly IReadOnlyList<SnippetLiteral> All = new[]
        {
            new SnippetLiteral(Caret, "ตำแหน่งเคอร์เซอร์หลังขยาย snippet"),
            new SnippetLiteral("DATE", "วันที่ปัจจุบัน (yyyy-MM-dd)"),
            new SnippetLiteral("TIME", "เวลาปัจจุบัน (HH:mm:ss)"),
            new SnippetLiteral("USER", "ชื่อผู้ใช้ Windows"),
            new SnippetLiteral("DB", "database ของหน้า query"),
            new SnippetLiteral("SERVER", "server ของหน้า query"),
        };

        /// <summary>Values for the literals at this moment. DB and SERVER come from the caller (the active connection).</summary>
        public static Dictionary<string, string> Current(string server, string database, DateTime now, string user) =>
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DATE"] = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["TIME"] = now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                ["USER"] = user ?? "",
                ["DB"] = database ?? "",
                ["SERVER"] = server ?? "",
            };

        /// <summary>Replaces $NAME$ for every name in <paramref name="values"/>; unknown names and the caret marker stay.</summary>
        public static string Replace(string text, IReadOnlyDictionary<string, string> values)
        {
            if (values == null || values.Count == 0) return text;
            return Regex.Replace(text, @"\$([A-Za-z]+)\$", m =>
                values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        }
    }
}
