using System;
using System.Linq;
using SqlAssist.Core.Snippets;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class SnippetManagerTests
    {
        [Fact]
        public void ToXml_RoundTripsThroughParse()
        {
            var original = new[]
            {
                new Snippet("ssf", "SELECT * FROM $end$", "SELECT * FROM"),
                new Snippet("bt", "BEGIN TRAN;\n\n    $CARET$\n\nROLLBACK TRAN;", null),
                new Snippet("x1", "SELECT '<a & b>' AS \"q\" ", "special <chars>"),
            };
            var parsed = SnippetStore.Parse(SnippetStore.ToXml(original)).All.ToList();

            Assert.Equal(original.Select(s => (s.Key, s.Body, s.Description ?? "")),
                         parsed.Select(s => (s.Key, s.Body, s.Description ?? "")));
        }

        [Fact]
        public void DefaultSnippetsRoundTrip()
        {
            var defaults = SnippetStore.Parse(SnippetStore.DefaultXml).All.ToList();
            var again = SnippetStore.Parse(SnippetStore.ToXml(defaults)).All.ToList();
            Assert.Equal(defaults.Select(s => (s.Key, s.Body)), again.Select(s => (s.Key, s.Body)));
        }

        [Fact]
        public void Render_ReplacesLiteralsAndFindsCaretMarker()
        {
            var s = new Snippet("hdr", "-- $USER$ $date$ $DB$@$SERVER$\nSELECT $CARET$", null);
            var values = SnippetLiterals.Current("srv1", "db1", new DateTime(2026, 10, 17, 9, 5, 7), "kriang");
            var (text, caret) = SnippetStore.Render(s, "", "\n", values);
            Assert.Equal("-- kriang 2026-10-17 db1@srv1\nSELECT ", text);
            Assert.Equal(text.Length, caret);
        }

        [Fact]
        public void Render_UnknownLiteralStays() =>
            Assert.Equal("$FOO$ x", SnippetStore.Render(new Snippet("k", "$FOO$ x", null), "", "\n",
                SnippetLiterals.Current("s", "d", DateTime.Now, "u")).text);

        [Fact]
        public void Render_EndMarkerStillWorks()
        {
            var (text, caret) = SnippetStore.Render(new Snippet("ssf", "SELECT * FROM $end$ WHERE", null), "", "\n");
            Assert.Equal("SELECT * FROM  WHERE", text);
            Assert.Equal(14, caret);
        }

        [Fact]
        public void Validate_FindsProblems()
        {
            var errors = SnippetStore.Validate(new[]
            {
                new Snippet("ok", "SELECT 1", null),
                new Snippet("", "x", null),
                new Snippet("two words", "x", null),
                new Snippet("OK", "y", null),
                new Snippet("empty", "  ", null),
            });
            Assert.Equal(4, errors.Count);
            Assert.Contains(errors, e => e.Contains("two words"));
            Assert.Contains(errors, e => e.Contains("ซ้ำ"));
            Assert.Contains(errors, e => e.Contains("empty"));
        }

        [Fact]
        public void Validate_AcceptsDefaults() =>
            Assert.Empty(SnippetStore.Validate(SnippetStore.Parse(SnippetStore.DefaultXml).All));
    }
}
