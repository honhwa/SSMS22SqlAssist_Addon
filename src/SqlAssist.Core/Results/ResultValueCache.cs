using System;
using System.Collections.Generic;

namespace SqlAssist.Core.Results
{
    /// <summary>Reads result-grid cells on demand and remembers them. Reading a cell goes through SSMS by reflection,
    /// so a dialog that lets the user tick columns in and out asks for exactly the columns and rows it needs, and
    /// each cell is read once however often the choice changes.</summary>
    public sealed class ResultValueCache
    {
        private readonly int _columnCount;
        private readonly object[][] _rows;
        private readonly Func<int, int, object> _read;
        /// <summary>How many leading rows are read, per column.</summary>
        private readonly int[] _loaded;

        /// <param name="read">(row index within the selection, data column) → value; null or DBNull for NULL.</param>
        public ResultValueCache(int columnCount, int rowCount, Func<int, int, object> read)
        {
            _columnCount = columnCount;
            _rows = new object[rowCount][];
            _read = read;
            _loaded = new int[columnCount];
        }

        public int RowCount => _rows.Length;

        /// <summary>The first <paramref name="count"/> rows, each indexed like the result set's columns, with only
        /// <paramref name="columns"/> filled in (the rest stay null). The arrays are the cache's own: do not modify them.</summary>
        public IReadOnlyList<object[]> Rows(IReadOnlyList<int> columns, int count)
        {
            foreach (int column in columns)
                if (column < 0 || column >= _columnCount) throw new ArgumentOutOfRangeException(nameof(columns));
            count = Math.Max(0, Math.Min(count, _rows.Length));

            foreach (int column in columns)
                for (int row = _loaded[column]; row < count; row++)
                {
                    if (_rows[row] == null) _rows[row] = new object[_columnCount];
                    _rows[row][column] = _read(row, column);
                    _loaded[column] = row + 1;
                }
            for (int row = 0; row < count; row++)
                if (_rows[row] == null) _rows[row] = new object[_columnCount];
            return new ArraySegment<object[]>(_rows, 0, count);
        }
    }
}
