using System;
using System.Collections.Generic;
using SqlAssist.Core.Results;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class ResultValueCacheTests
    {
        private readonly List<(int row, int column)> _reads = new List<(int, int)>();

        private ResultValueCache Cache(int columns = 4, int rows = 5) =>
            new ResultValueCache(columns, rows, (r, c) => { _reads.Add((r, c)); return r * 10 + c; });

        [Fact]
        public void Rows_FillsOnlyTheRequestedColumns()
        {
            var rows = Cache().Rows(new[] { 1, 3 }, 2);
            Assert.Equal(2, rows.Count);
            Assert.Equal(4, rows[1].Length);
            Assert.Equal(new object[] { null, 11, null, 13 }, rows[1]);
            Assert.Equal(new object[] { null, 1, null, 3 }, rows[0]);
        }

        [Fact]
        public void Rows_ReadsEachCellOnce()
        {
            var cache = Cache();
            cache.Rows(new[] { 0 }, 3);
            cache.Rows(new[] { 0 }, 3);
            Assert.Equal(3, _reads.Count);

            // More rows: only the new ones. Another column: only that column's rows.
            cache.Rows(new[] { 0 }, 5);
            Assert.Equal(5, _reads.Count);
            cache.Rows(new[] { 0, 2 }, 5);
            Assert.Equal(10, _reads.Count);
            Assert.Equal(5, _reads.FindAll(x => x.column == 2).Count);
        }

        [Fact]
        public void Rows_CountIsCappedAtTheRowTotal()
        {
            Assert.Equal(5, Cache().Rows(new[] { 0 }, 99).Count);
            Assert.Empty(Cache().Rows(new[] { 0 }, 0));
        }

        [Fact]
        public void Rows_KeepsValuesFromEarlierCalls()
        {
            var cache = Cache();
            cache.Rows(new[] { 0 }, 2);
            var rows = cache.Rows(new[] { 2 }, 2);
            Assert.Equal(new object[] { 0, null, 2, null }, rows[0]);
        }

        [Fact]
        public void Rows_RejectsAColumnOutsideTheResultSet()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Cache().Rows(new[] { 4 }, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Cache().Rows(new[] { -1 }, 1));
        }
    }
}
