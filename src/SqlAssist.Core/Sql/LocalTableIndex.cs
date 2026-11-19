using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Schema;

namespace SqlAssist.Core.Sql
{
    /// <summary>Tracks editor declarations independently of the connected database.</summary>
    public sealed class LocalTableIndex
    {
        private readonly List<(int End, string Name, DbObject Table)> _changes = new List<(int, string, DbObject)>();

        public static LocalTableIndex Parse(string text)
        {
            var index = new LocalTableIndex();
            var scan = SqlStatementScanner.Scan(text);
            foreach (var batch in scan.Batches)
            {
                foreach (var statement in batch.Statements)
                {
                    if (statement.FirstSignificant < 0) continue;
                    var tokens = scan.Tokens.Skip(statement.FirstSignificant)
                        .Take((statement.Kind == StatementKind.Drop ? batch.LastToken : statement.LastToken) - statement.FirstSignificant + 1)
                        .Where(t => t.Kind != TokenKind.Comment).ToArray();
                    if (tokens.Length < 3) continue;
                    if (tokens[0].IsWord("SELECT") || statement.Kind == StatementKind.Select)
                    {
                        for (int i = 1; i + 1 < tokens.Length; i++)
                            if (tokens[i].IsWord("INTO") && IsTemp(tokens[i + 1]))
                                index._changes.Add((tokens[i + 1].End, tokens[i + 1].Value, null));
                        continue;
                    }

                    if (tokens[0].IsWord("DECLARE") && tokens[1].Kind == TokenKind.Variable)
                    {
                        int type = tokens[2].IsWord("AS") ? 3 : 2;
                        if (type + 1 < tokens.Length && tokens[type].IsWord("TABLE"))
                            index.AddTable(text, tokens, 1, type + 1, "Table variable");
                        continue;
                    }

                    if (!tokens[1].IsWord("TABLE")) continue;
                    if (tokens[0].IsWord("CREATE") && IsTemp(tokens[2]))
                        index.AddTable(text, tokens, 2, 3, "Temporary table");
                    else if (tokens[0].IsWord("ALTER") && IsTemp(tokens[2]))
                        index._changes.Add((tokens[2].End, tokens[2].Value, null));
                    else if (tokens[0].IsWord("DROP"))
                    {
                        int name = tokens.Length > 4 && tokens[2].IsWord("IF") && tokens[3].IsWord("EXISTS") ? 4 : 2;
                        for (; name < tokens.Length; name += 2)
                        {
                            if (!tokens[name].IsName) break;
                            if (IsTemp(tokens[name])) index._changes.Add((tokens[name].End, tokens[name].Value, null));
                            if (name + 1 >= tokens.Length || !tokens[name + 1].IsSymbol(',')) break;
                        }
                    }
                }
                if (batch.HasGo)
                    index._changes.Add((scan.Tokens[batch.GoLastToken].End, null, null));
            }
            return index;
        }

        public IReadOnlyList<DbObject> At(int position)
        {
            var tables = new Dictionary<string, DbObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in _changes)
            {
                if (change.End > position) break;
                if (change.Name == null)
                {
                    foreach (var variable in tables.Keys.Where(n => n.StartsWith("@", StringComparison.Ordinal)).ToArray())
                        tables.Remove(variable);
                }
                else if (change.Table == null) tables.Remove(change.Name);
                else tables[change.Name] = change.Table;
            }
            return tables.Values.ToArray();
        }

        private static bool IsTemp(SqlToken token) => token.IsName && token.Terminated &&
            token.Value.StartsWith("#", StringComparison.Ordinal) && !token.Value.StartsWith("##", StringComparison.Ordinal);

        private void AddTable(string text, SqlToken[] tokens, int name, int open, string kind)
        {
            var tableName = tokens[name].Value;
            _changes.Add((tokens[name].End, tableName, null));
            if (open >= tokens.Length || !tokens[open].IsSymbol('(')) return;
            int depth = 1, start = open + 1;
            var columns = new List<DbColumn>();
            for (int i = start; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (token.IsSymbol('(')) depth++;
                else if (token.IsSymbol(')')) depth--;
                if (depth == 1 && token.IsSymbol(',') || depth == 0)
                {
                    if (!ReadColumn(text, tokens, start, i, columns)) return;
                    start = i + 1;
                }
                if (depth != 0) continue;
                if (columns.Count != 0 && columns.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == columns.Count)
                    _changes.Add((token.End, tableName, new DbObject(null, tableName, false, columns, kind: kind)));
                return;
            }
        }

        private static bool ReadColumn(string text, SqlToken[] tokens, int start, int end, List<DbColumn> columns)
        {
            if (start >= end || !tokens[start].IsName || !tokens[start].Terminated) return false;
            var name = tokens[start];
            if (name.IsWord("CONSTRAINT") || name.IsWord("PRIMARY") || name.IsWord("UNIQUE") ||
                name.IsWord("FOREIGN") || name.IsWord("CHECK") || name.IsWord("INDEX")) return true;
            int type = start + 1;
            if (type >= end || !tokens[type].IsName || !tokens[type].Terminated) return false;
            if (tokens[type].IsWord("AS"))
            {
                columns.Add(new DbColumn(name.Value, null, isComputed: true));
                return type + 1 < end;
            }
            int stop = type + 1;
            while (stop + 1 < end && tokens[stop].IsSymbol('.') && tokens[stop + 1].IsName) stop += 2;
            if (stop < end && tokens[stop].IsSymbol('('))
            {
                while (stop < end && !tokens[stop].IsSymbol(')')) stop++;
                if (stop == end) return false;
                stop++;
            }
            columns.Add(new DbColumn(name.Value, text.Substring(tokens[type].Start, tokens[stop - 1].End - tokens[type].Start)));
            return true;
        }
    }
}
