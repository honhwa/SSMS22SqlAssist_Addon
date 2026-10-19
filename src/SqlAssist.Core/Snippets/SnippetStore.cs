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
        /// indentation of the line the shortcut was typed on, so multi-line snippets stay aligned.
        /// Literals ($date$, $user$, $db$ … see <see cref="SnippetLiterals"/>) are replaced from
        /// <paramref name="literals"/>; the caret goes to $end$ (or $CARET$), else to the end.</summary>
        public static (string text, int caretOffset) Render(Snippet snippet, string indent, string newLine,
            IReadOnlyDictionary<string, string> literals = null)
        {
            var lines = snippet.Body.Replace("\r\n", "\n").Split('\n');
            string text = SnippetLiterals.Replace(string.Join(newLine + indent, lines), literals);
            var caret = System.Text.RegularExpressions.Regex.Match(text, SnippetLiterals.CaretPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!caret.Success) return (text, text.Length);
            return (text.Remove(caret.Index, caret.Length), caret.Index);
        }

        /// <summary>Writes snippets back in the format <see cref="Parse"/> reads, keeping the help comment at the top.</summary>
        public static string ToXml(IEnumerable<Snippet> snippets)
        {
            var root = new XElement("snippets",
                snippets.Select(s => new XElement("snippet",
                    new XAttribute("key", s.Key),
                    string.IsNullOrEmpty(s.Description) ? null : new XAttribute("description", s.Description),
                    // Multi-line bodies start on their own line so the file stays readable; Parse trims it back.
                    s.Body.IndexOf('\n') >= 0 ? "\n" + s.Body.Replace("\r\n", "\n") + "\n  " : s.Body)));
            var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), new XComment(FileHelp), root);
            using (var writer = new Utf8StringWriter())
            {
                doc.Save(writer);
                return writer.ToString();
            }
        }

        /// <summary>Problems that would make a set of snippets unusable: missing or duplicate shortcuts, shortcuts
        /// that are not one word (Tab only looks at the word before the caret), empty text.</summary>
        public static IReadOnlyList<string> Validate(IEnumerable<Snippet> snippets)
        {
            var errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in snippets)
            {
                string key = s.Key ?? "";
                if (key.Length == 0) { errors.Add("มี snippet ที่ยังไม่ได้ใส่ Shortcut"); continue; }
                if (!key.All(c => char.IsLetterOrDigit(c) || c == '_'))
                    errors.Add($"Shortcut \"{key}\" ใช้ได้เฉพาะตัวอักษร ตัวเลข และ _ (ห้ามมีช่องว่าง)");
                if (!seen.Add(key)) errors.Add($"Shortcut \"{key}\" ซ้ำกัน");
                if (string.IsNullOrWhiteSpace(s.Body)) errors.Add($"Snippet \"{key}\" ยังไม่มีข้อความ (Text)");
            }
            return errors;
        }

        private sealed class Utf8StringWriter : System.IO.StringWriter
        {
            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        }

        private const string FileHelp = @"
  SqlAssist snippets: type the key, then press Tab.
  $end$ (or $CARET$) marks where the caret lands after expansion.
  Literals replaced on expansion: $DATE$ $TIME$ $USER$ $DB$ $SERVER$.
  Multi-line snippets can be written on their own lines; common indentation is removed.
  Edit with Tools > SqlAssist > Edit Snippets..., or by hand; saved changes apply on the next Tab.
";

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
  $CARET$ marks where the caret lands after expansion. Also: $DATE$ $TIME$ $USER$ $DB$ $SERVER$.
  Multi-line snippets can be written on their own lines; common indentation is removed.
  Saved changes take effect on the next Tab - no restart needed.
-->
<snippets>
  <snippet key=""ssf"" description=""SELECT * FROM"">SELECT * FROM $CARET$</snippet>
  <snippet key=""st100"" description=""SELECT TOP 100 * FROM"">SELECT TOP 100 * FROM $CARET$</snippet>
  <snippet key=""scf"" description=""SELECT COUNT(*) FROM"">SELECT COUNT(*) FROM $CARET$</snippet>
  <snippet key=""sel"" description=""SELECT ... FROM"">SELECT $CARET$
FROM </snippet>
  <snippet key=""ii"" description=""INSERT INTO"">INSERT INTO $CARET$ ()
VALUES ()</snippet>
  <snippet key=""upd"" description=""UPDATE ... SET ... WHERE"">UPDATE $CARET$
SET
WHERE </snippet>
  <snippet key=""del"" description=""DELETE FROM ... WHERE"">DELETE FROM $CARET$
WHERE </snippet>
  <snippet key=""ij"" description=""INNER JOIN"">INNER JOIN $CARET$</snippet>
  <snippet key=""lj"" description=""LEFT JOIN"">LEFT JOIN $CARET$</snippet>
  <snippet key=""wh"" description=""WHERE"">WHERE $CARET$</snippet>
  <snippet key=""ob"" description=""ORDER BY"">ORDER BY $CARET$</snippet>
  <snippet key=""gb"" description=""GROUP BY"">GROUP BY $CARET$</snippet>
  <snippet key=""bt"" description=""BEGIN TRAN / ROLLBACK"">
    BEGIN TRAN;

    $CARET$

    ROLLBACK TRAN;
    -- COMMIT TRAN;
  </snippet>
  <snippet key=""cte"" description=""WITH cte AS (...)"">
    WITH cte AS (
        $CARET$
    )
    SELECT * FROM cte;
  </snippet>
  <snippet key=""ifex"" description=""IF EXISTS (SELECT 1 ...)"">
    IF EXISTS (SELECT 1 FROM $CARET$)
    BEGIN

    END
  </snippet>
  <snippet key=""cw"" description=""CASE WHEN ... THEN ... ELSE ... END"">
    CASE
        WHEN $CARET$ THEN
        ELSE
    END
  </snippet>
</snippets>
";
    }
}
