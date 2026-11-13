using System;
using System.Linq;
using SqlAssist.Core.Completion;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class UsageStatsTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

        private static UsageStats Used(string key, int times, DateTime? at = null)
        {
            var stats = new UsageStats();
            for (int i = 0; i < times; i++) stats.Record(key, at ?? Now);
            return stats;
        }

        [Fact]
        public void NoBonusBeforeMinUses()
        {
            Assert.Equal(0, Used("*|WHERE", UsageStats.MinUses - 1).Bonus("*|WHERE", Now));
            Assert.True(Used("*|WHERE", UsageStats.MinUses).Bonus("*|WHERE", Now) > 0);
            Assert.Equal(0, new UsageStats().Bonus("*|WHERE", Now));
            Assert.Equal(0, new UsageStats().Bonus(null, Now));
        }

        [Fact]
        public void BonusGrowsWithUseAndIsCapped()
        {
            int few = Used("k", 3).Bonus("k", Now);
            int more = Used("k", 7).Bonus("k", Now);
            int many = Used("k", 500).Bonus("k", Now);
            Assert.True(few < more);
            Assert.Equal(UsageStats.MaxBonus, many);
        }

        [Fact]
        public void OldUseFades()
        {
            var stats = Used("k", 20, Now.AddDays(-UsageStats.HalfLifeDays * 6));
            Assert.Equal(0, stats.Bonus("k", Now));
            Assert.Equal(UsageStats.MaxBonus, stats.Bonus("k", Now.AddDays(-UsageStats.HalfLifeDays * 6)));
        }

        [Fact]
        public void KeysAreCaseInsensitive()
        {
            Assert.True(Used("*|where", 5).Bonus("*|WHERE", Now) > 0);
        }

        [Fact]
        public void ScopedKeysKeepDatabasesApart()
        {
            string a = UsageStats.ScopedKey("srv", "DbA", "JOB_General");
            string b = UsageStats.ScopedKey("srv", "DbB", "JOB_General");
            Assert.NotEqual(a, b);
            Assert.Equal(0, Used(a, 5).Bonus(b, Now));
            Assert.Null(UsageStats.ScopedKey("srv", null, "x"));
            Assert.Null(UsageStats.GlobalKey(""));
            Assert.Null(UsageStats.GlobalKey("a\tb"));
        }

        [Fact]
        public void RoundTrip()
        {
            var stats = Used("*|WHERE", 4);
            stats.Record(UsageStats.ScopedKey("srv", "db", "Order Line"), Now);
            Assert.True(stats.IsDirty);
            string text = stats.Serialize();
            Assert.False(stats.IsDirty);

            var read = UsageStats.Parse(text);
            Assert.Equal(2, read.Count);
            Assert.Equal(stats.Bonus("*|WHERE", Now), read.Bonus("*|WHERE", Now));
            Assert.False(read.IsDirty);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a usage file")]
        [InlineData("SqlAssist usage 1\nbroken line\nk\tx\t1\t0\nk\t1\tNaN\t0\nk\t1\t1\t-5")]
        public void BadInputIsAnEmptyHistory(string text) => Assert.Equal(0, UsageStats.Parse(text).Count);

        [Fact]
        public void KeepsOnlyTheMostRecentEntries()
        {
            var stats = new UsageStats();
            for (int i = 0; i < UsageStats.MaxEntries + 10; i++) stats.Record("k" + i, Now.AddMinutes(i));
            Assert.Equal(UsageStats.MaxEntries, stats.Count);
            var read = UsageStats.Parse(stats.Serialize());
            Assert.Equal(UsageStats.MaxEntries, read.Count);
        }

        [Fact]
        public void ClearEmptiesAndNeedsSaving()
        {
            var stats = Used("k", 5);
            stats.MarkSaved();
            stats.Clear();
            Assert.Equal(0, stats.Count);
            Assert.True(stats.IsDirty);
        }

        // The bonus must stay inside FuzzyMatcher's tiers: it reorders equally good matches, never a worse match over a better one.
        [Fact]
        public void BonusNeverLiftsAWorseMatchAboveABetterOne()
        {
            var stats = Used("*|WITH", 500);
            int bonus = stats.Bonus("*|WITH", Now);
            Assert.True(FuzzyMatcher.Score("where", "WITH") == 0 || FuzzyMatcher.Score("where", "WITH") + bonus < FuzzyMatcher.Score("where", "WHERE"));
            // Exact beats any prefix match plus the largest bonus.
            Assert.True(FuzzyMatcher.Score("wh", "WHERE") + UsageStats.MaxBonus + KeywordPreference.AfterTableSourceBonus < FuzzyMatcher.Score("wh", "wh"));
            // A prefix match beats a subsequence or substring match plus the largest bonus.
            Assert.True(FuzzyMatcher.Score("ord", "OrderDetailsWithAVeryLongNameIndeed") > FuzzyMatcher.Score("ord", "TheOrders") + UsageStats.MaxBonus);
        }

        [Fact]
        public void FrequentlyUsedKeywordMovesUpAmongEqualMatches()
        {
            var stats = Used("*|WHILE", 10);
            var ranked = new[] { "WHEN", "WITH", "WHERE", "WHILE" }
                .Select((k, i) => (k, i, score: FuzzyMatcher.Score("w", k) + stats.Bonus(UsageStats.GlobalKey(k), Now)))
                .OrderByDescending(x => x.score).ThenBy(x => x.i).Select(x => x.k).ToArray();
            Assert.Equal("WHILE", ranked[0]);
        }
    }
}
