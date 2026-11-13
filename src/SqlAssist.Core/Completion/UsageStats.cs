using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SqlAssist.Core.Completion
{
    /// <summary>How often, and how lately, the user committed each completion item, so items they use a lot rank a little higher.
    /// Each use adds 1 to a score that halves every <see cref="HalfLifeDays"/> days (frecency): what was used a lot but not for weeks
    /// fades. The bonus is small on purpose (at most <see cref="MaxBonus"/>, against <see cref="FuzzyMatcher"/> tiers 100 apart), so it
    /// only reorders items that match the typed text about equally well: a fully typed word is still first, and a prefix match is still
    /// ahead of a subsequence match.
    /// Keys: keywords and snippets are shared by every database (<see cref="GlobalKey"/>); tables, columns and routines belong to one
    /// server and database (<see cref="ScopedKey"/>). Not thread-safe: the caller locks.</summary>
    public sealed class UsageStats
    {
        public const int MaxBonus = 8;
        /// <summary>Uses before an item gets any bonus, so one stray pick does not move the list.</summary>
        public const int MinUses = 3;
        public const double HalfLifeDays = 14;
        /// <summary>Entries kept; past this the least recently used go first.</summary>
        public const int MaxEntries = 2000;
        private const string Header = "SqlAssist usage 1";

        private sealed class Entry
        {
            public int Count;
            public double Score;
            public DateTime LastUtc;
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public int Count => _entries.Count;
        /// <summary>Changed since the last <see cref="Serialize"/> or <see cref="MarkSaved"/>.</summary>
        public bool IsDirty { get; private set; }

        public static string GlobalKey(string text) => Valid(text) ? "*|" + text : null;

        /// <summary>Null (nothing recorded) when the scope or the text is missing.</summary>
        public static string ScopedKey(string server, string database, string text) =>
            Valid(text) && Valid(server) && Valid(database) ? server + "|" + database + "|" + text : null;

        // Tabs and line breaks would break the file's lines; names that contain them are simply not counted.
        private static bool Valid(string part) => !string.IsNullOrEmpty(part) && part.IndexOfAny(new[] { '\t', '\r', '\n' }) < 0;

        public void Record(string key, DateTime nowUtc)
        {
            if (key == null) return;
            if (!_entries.TryGetValue(key, out var e)) _entries[key] = e = new Entry();
            e.Score = Decayed(e, nowUtc) + 1;
            e.Count++;
            e.LastUtc = nowUtc;
            IsDirty = true;
            if (_entries.Count > MaxEntries) Trim();
        }

        /// <summary>0 for an unknown key or one used fewer than <see cref="MinUses"/> times; then 2·log2(1 + score), capped: about 4 for a
        /// score of 3, 6 for 7, 8 from 15 up.</summary>
        public int Bonus(string key, DateTime nowUtc)
        {
            if (key == null || !_entries.TryGetValue(key, out var e) || e.Count < MinUses) return 0;
            double score = Decayed(e, nowUtc);
            return Math.Max(0, Math.Min(MaxBonus, (int)Math.Floor(2 * Math.Log(1 + score, 2))));
        }

        public void Clear()
        {
            if (_entries.Count > 0) IsDirty = true;
            _entries.Clear();
        }

        public void MarkSaved() => IsDirty = false;

        private static double Decayed(Entry e, DateTime nowUtc)
        {
            double days = Math.Max(0, (nowUtc - e.LastUtc).TotalDays);
            return e.Score * Math.Pow(0.5, days / HalfLifeDays);
        }

        private void Trim()
        {
            foreach (var key in _entries.OrderBy(p => p.Value.LastUtc).Take(_entries.Count - MaxEntries).Select(p => p.Key).ToList())
                _entries.Remove(key);
        }

        /// <summary>One line per item: key, count, score, last use (UTC ticks), tab-separated.</summary>
        public string Serialize()
        {
            var sb = new StringBuilder(Header).Append('\n');
            foreach (var p in _entries.OrderByDescending(p => p.Value.LastUtc))
                sb.Append(p.Key).Append('\t')
                  .Append(p.Value.Count.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(p.Value.Score.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(p.Value.LastUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
            IsDirty = false;
            return sb.ToString();
        }

        /// <summary>As lenient as the settings file: lines that do not read are skipped, and anything else is an empty history.</summary>
        public static UsageStats Parse(string text)
        {
            var stats = new UsageStats();
            if (string.IsNullOrEmpty(text)) return stats;
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 || lines[0].Trim() != Header) return stats;
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split('\t');
                if (parts.Length < 4) continue;
                string key = string.Join("\t", parts.Take(parts.Length - 3));
                if (key.Length == 0 || key.Contains("\t")) continue;
                if (!int.TryParse(parts[parts.Length - 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count <= 0) continue;
                if (!double.TryParse(parts[parts.Length - 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double score) ||
                    double.IsNaN(score) || double.IsInfinity(score) || score < 0) continue;
                if (!long.TryParse(parts[parts.Length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) ||
                    ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) continue;
                stats._entries[key] = new Entry { Count = count, Score = score, LastUtc = new DateTime(ticks, DateTimeKind.Utc) };
            }
            if (stats._entries.Count > MaxEntries) stats.Trim();
            stats.IsDirty = false;
            return stats;
        }
    }
}
