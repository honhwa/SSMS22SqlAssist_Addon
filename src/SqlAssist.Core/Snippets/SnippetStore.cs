using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace SqlAssist.Core.Snippets
{
    public sealed class Snippet
    {
        public Snippet(string key, string body, string description)
        {
            Key = key; Body = body; Description = description;
        }
        public string Key { get; }
        /// <summary>SQL to insert. "$end$" marks where the caret goes; without it the caret goes to the end.</summary>
        public string Body { get; }
        public string Description { get; }
    }

    public sealed class SnippetStore
    {
        public const string EndMarker = "$end$";

        private readonly Dictionary<string, Snippet> _byKey;

        private SnippetStore(IEnumerable<Snippet> snippets)
        {
            _byKey = new Dictionary<string, Snippet>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in snippets) _byKey[s.Key] = s; // a later duplicate wins
        }

        public static SnippetStore Empty { get; } = new SnippetStore(new Snippet[0]);

        public IReadOnlyCollection<Snippet> All => _byKey.Values;

        public bool TryGet(string key, out Snippet snippet) => _byKey.TryGetValue(key ?? "", out snippet);

        /// <summary>Reads &lt;snippets&gt;&lt;snippet key="ssf" description="..."&gt;SELECT * FROM $end$&lt;/snippet&gt;&lt;/snippets&gt;.</summary>
        public static SnippetStore Parse(string xml)
        {
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var snippets = doc.Root?.Elements("snippet")
                .Select(e => new Snippet(
                    ((string)e.Attribute("key") ?? "").Trim(),
                    TrimBlankEdgeLines(e.Value),
                    (string)e.Attribute("description")))
                .Where(s => s.Key.Length > 0 && s.Body.Length > 0)
                ?? Enumerable.Empty<Snippet>();
            return new SnippetStore(snippets);
        }

        /// <summary>The text to insert and the caret offset within it. Lines after the first get the
        /// indentation of the line the shortcut was typed on, so multi-line snippets stay aligned.</summary>
        public static (string text, int caretOffset) Render(Snippet snippet, string indent, string newLine)
        {
            var lines = snippet.Body.Replace("\r\n", "\n").Split('\n');
            string text = string.Join(newLine + indent, lines);
            int caret = text.IndexOf(EndMarker, StringComparison.Ordinal);
            if (caret < 0) return (text, text.Length);
            return (text.Remove(caret, EndMarker.Length), caret);
        }

        // Lets a snippet be written on its own lines inside the element without gaining blank lines.
        private static string TrimBlankEdgeLines(string body)
        {
            var lines = body.Replace("\r\n", "\n").Split('\n').ToList();
            while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            if (lines.Count <= 1) return lines.Count == 0 ? "" : lines[0];
            int common = lines.Where(l => l.Trim().Length > 0).Min(l => l.Length - l.TrimStart().Length);
            return string.Join("\n", lines.Select(l => l.Length >= common ? l.Substring(common) : l.TrimStart()));
        }

        public const string DefaultXml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<!--
  SqlAssist snippets: type the key, then press Tab.
  $end$ marks where the caret lands after expansion.
  Multi-line snippets can be written on their own lines; common indentation is removed.
  Saved changes take effect on the next Tab - no restart needed.
-->
<snippets>
  <snippet key=""ssf"" description=""SELECT * FROM"">SELECT * FROM $end$</snippet>
  <snippet key=""st100"" description=""SELECT TOP 100 * FROM"">SELECT TOP 100 * FROM $end$</snippet>
  <snippet key=""scf"" description=""SELECT COUNT(*) FROM"">SELECT COUNT(*) FROM $end$</snippet>
  <snippet key=""sel"" description=""SELECT ... FROM"">SELECT $end$
FROM </snippet>
  <snippet key=""ii"" description=""INSERT INTO"">INSERT INTO $end$ ()
VALUES ()</snippet>
  <snippet key=""upd"" description=""UPDATE ... SET ... WHERE"">UPDATE $end$
SET
WHERE </snippet>
  <snippet key=""del"" description=""DELETE FROM ... WHERE"">DELETE FROM $end$
WHERE </snippet>
  <snippet key=""ij"" description=""INNER JOIN"">INNER JOIN $end$</snippet>
  <snippet key=""lj"" description=""LEFT JOIN"">LEFT JOIN $end$</snippet>
  <snippet key=""wh"" description=""WHERE"">WHERE $end$</snippet>
  <snippet key=""ob"" description=""ORDER BY"">ORDER BY $end$</snippet>
  <snippet key=""gb"" description=""GROUP BY"">GROUP BY $end$</snippet>
  <snippet key=""bt"" description=""BEGIN TRAN / ROLLBACK"">
    BEGIN TRAN;

    $end$

    ROLLBACK TRAN;
    -- COMMIT TRAN;
  </snippet>
  <snippet key=""cte"" description=""WITH cte AS (...)"">
    WITH cte AS (
        $end$
    )
    SELECT * FROM cte;
  </snippet>
  <snippet key=""ifex"" description=""IF EXISTS (SELECT 1 ...)"">
    IF EXISTS (SELECT 1 FROM $end$)
    BEGIN

    END
  </snippet>
</snippets>
";
    }
}
