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
                // The saved set is what the user chose, so defaults they removed must not come back.
                new XAttribute(DefaultsAttribute, DefaultsRevision),
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

        /// <summary>Bump when adding a default snippet, and list it in <see cref="DefaultsAddedIn"/>.</summary>
        public const int DefaultsRevision = 1;
        private const string DefaultsAttribute = "defaults";

        // Defaults added after snippets.xml files already existed, with the revision that added them.
        // Files without a "defaults" attribute are revision 0.
        private static readonly Dictionary<string, int> DefaultsAddedIn =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["case"] = 1 };

        /// <summary>Adds default snippets introduced since the user's file was written (unless the user already
        /// has that key), then stamps the file with the current revision so removed defaults stay removed.
        /// Returns null when the file is already up to date.</summary>
        public static string UpgradeDefaults(string xml)
        {
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var root = doc.Root;
            if (root == null) return null;
            int.TryParse((string)root.Attribute(DefaultsAttribute), out int fileRevision);
            if (fileRevision >= DefaultsRevision) return null;

            var existing = new HashSet<string>(
                root.Elements("snippet").Select(e => ((string)e.Attribute("key") ?? "").Trim()),
                StringComparer.OrdinalIgnoreCase);
            var defaults = XDocument.Parse(DefaultXml, LoadOptions.PreserveWhitespace).Root.Elements("snippet");
            foreach (var e in defaults)
            {
                string key = (string)e.Attribute("key");
                if (DefaultsAddedIn.TryGetValue(key, out int addedIn) && addedIn > fileRevision && existing.Add(key))
                    root.Add("  ", new XElement(e), "\n");
            }
            root.SetAttributeValue(DefaultsAttribute, DefaultsRevision);

            using (var writer = new Utf8StringWriter())
            {
                doc.Save(writer, SaveOptions.DisableFormatting);
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
                if (key.Length == 0) { errors.Add("有代码片段尚未填写快捷键"); continue; }
                if (!key.All(c => char.IsLetterOrDigit(c) || c == '_'))
                    errors.Add($"Shortcut \"{key}\" 只能包含字母、数字和 _（不能有空格）");
                if (!seen.Add(key)) errors.Add($"Shortcut \"{key}\" 重复");
                if (string.IsNullOrWhiteSpace(s.Body)) errors.Add($"Snippet \"{key}\" 还没有内容 (Text)");
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
  SqlAssist 代码片段：输入快捷键后按 Tab。
  $CARET$ 表示展开后光标的位置。另可用：$DATE$ $TIME$ $USER$ $DB$ $SERVER$。
  多行代码片段可直接分行书写；公共缩进会被去掉。
  保存后下一次按 Tab 即生效，无需重启。
-->
<snippets defaults=""1"">
  <snippet key=""ssf"" description=""查询全部列"">SELECT * FROM $CARET$</snippet>
  <snippet key=""st100"" description=""查询前 100 行"">SELECT TOP 100 * FROM $CARET$</snippet>
  <snippet key=""scf"" description=""统计行数"">SELECT COUNT(*) FROM $CARET$</snippet>
  <snippet key=""sel"" description=""SELECT 查询"">SELECT $CARET$
FROM </snippet>
  <snippet key=""ii"" description=""插入数据"">INSERT INTO $CARET$ ()
VALUES ()</snippet>
  <snippet key=""upd"" description=""更新数据"">UPDATE $CARET$
SET
WHERE </snippet>
  <snippet key=""del"" description=""删除数据"">DELETE FROM $CARET$
WHERE </snippet>
  <snippet key=""ij"" description=""内连接"">INNER JOIN $CARET$</snippet>
  <snippet key=""lj"" description=""左连接"">LEFT JOIN $CARET$</snippet>
  <snippet key=""wh"" description=""WHERE 条件"">WHERE $CARET$</snippet>
  <snippet key=""ob"" description=""排序"">ORDER BY $CARET$</snippet>
  <snippet key=""gb"" description=""分组"">GROUP BY $CARET$</snippet>
  <snippet key=""bt"" description=""事务（可回滚）"">
    BEGIN TRAN;

    $CARET$

    ROLLBACK TRAN;
    -- COMMIT TRAN;
  </snippet>
  <snippet key=""cte"" description=""公用表表达式 (CTE)"">
    WITH cte AS (
        $CARET$
    )
    SELECT * FROM cte;
  </snippet>
  <snippet key=""ifex"" description=""存在则执行"">
    IF EXISTS (SELECT 1 FROM $CARET$)
    BEGIN

    END
  </snippet>
  <snippet key=""case"" description=""条件分支 (CASE)"">
    CASE
        WHEN $CARET$ THEN
        ELSE
    END
  </snippet>
</snippets>
";
    }
}
