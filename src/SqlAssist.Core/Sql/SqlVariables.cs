using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SqlAssist.Core.Sql
{
    public enum SqlVariableKind { Variable, Parameter, TableVariable }

    /// <summary>A @variable or @parameter declared in the batch above the caret.</summary>
    public sealed class SqlVariable
    {
        public SqlVariable(string name, string typeName, SqlVariableKind kind, string owner = null, string direction = null)
        {
            Name = name; TypeName = typeName; Kind = kind; Owner = owner; Direction = direction;
        }

        /// <summary>With the leading '@'.</summary>
        public string Name { get; }
        /// <summary>"INT", "VARCHAR(50)", "TABLE", "dbo.MyType"; null when it could not be read.</summary>
        public string TypeName { get; }
        public SqlVariableKind Kind { get; }
        /// <summary>For a parameter: the procedure or function it belongs to.</summary>
        public string Owner { get; }
        /// <summary>For a parameter: "INPUT", "OUTPUT" or "READONLY".</summary>
        public string Direction { get; }

        public override string ToString() => Name + " " + TypeName;
    }

    /// <summary>Reads DECLARE statements and procedure/function parameter lists, so "@" can offer what the
    /// script has declared.</summary>
    public static class SqlVariables
    {
        // Words that start a new statement: a DECLARE's "= expression" (which needs no ';') ends at one of these.
        private static readonly HashSet<string> StatementStarts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "SET", "DECLARE", "IF", "ELSE", "WHILE", "BEGIN", "RETURN",
            "PRINT", "EXEC", "EXECUTE", "WITH", "CREATE", "ALTER", "DROP", "TRUNCATE", "RAISERROR", "THROW", "GOTO",
            "FETCH", "OPEN", "CLOSE", "DEALLOCATE", "USE", "GO",
        };

        /// <summary>Variables and parameters declared before <paramref name="caret"/> in its batch (since the last GO).
        /// A name declared twice keeps its first declaration.</summary>
        public static IReadOnlyList<SqlVariable> DeclaredBefore(string text, int caret)
        {
            var tokens = SqlTokenizer.Tokenize(text)
                // The @name the caret is typing ("DECLARE @Ma|") is not a declaration yet.
                .Where(t => t.Kind != TokenKind.Comment && t.Kind != TokenKind.String && t.Start < caret &&
                            !(t.Kind == TokenKind.Variable && t.End >= caret))
                .ToList();
            int from = 0;
            for (int i = tokens.Count - 1; i >= 0; i--)
                if (tokens[i].IsWord("GO") && IsAloneOnLine(text, tokens[i])) { from = i + 1; break; }

            var result = new List<SqlVariable>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(SqlVariable v)
            {
                if (v.Name.Length > 1 && seen.Add(v.Name)) result.Add(v);
            }

            for (int i = from; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.IsWord("DECLARE")) i = ReadDeclare(tokens, i + 1, Add) - 1;
                else if ((t.IsWord("PROCEDURE") || t.IsWord("PROC") || t.IsWord("FUNCTION")) && i > 0 && IsCreateOrAlter(tokens, i - 1))
                    i = ReadParameters(tokens, i, Add) - 1;
            }
            return result;
        }

        // "CREATE PROC", "ALTER PROC", "CREATE OR ALTER PROC".
        private static bool IsCreateOrAlter(List<SqlToken> tokens, int i) => tokens[i].IsWord("CREATE") || tokens[i].IsWord("ALTER");

        // DECLARE @a INT = 1, @b AS VARCHAR(10), @t TABLE (...). Returns the index after the statement.
        private static int ReadDeclare(List<SqlToken> tokens, int i, Action<SqlVariable> add)
        {
            while (i < tokens.Count && tokens[i].Kind == TokenKind.Variable)
            {
                string name = tokens[i].Text;
                i++;
                if (i < tokens.Count && tokens[i].IsWord("AS")) i++;
                if (i < tokens.Count && tokens[i].IsWord("CURSOR")) return i; // DECLARE @c CURSOR FOR ...: skip
                bool table = i < tokens.Count && tokens[i].IsWord("TABLE");
                string type = ReadType(tokens, ref i);
                add(new SqlVariable(name, type, table ? SqlVariableKind.TableVariable : SqlVariableKind.Variable));

                // The rest of this item: "= expression", up to a comma that starts the next @name.
                int depth = 0;
                while (i < tokens.Count)
                {
                    var t = tokens[i];
                    if (t.IsSymbol('(') || t.IsWord("CASE")) depth++;
                    else if ((t.IsSymbol(')') || t.IsWord("END")) && depth > 0) depth--;
                    else if (depth == 0)
                    {
                        if (t.IsSymbol(';')) return i + 1;
                        if (t.IsSymbol(',') && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Variable) { i++; break; }
                        if (t.Kind == TokenKind.Word && StatementStarts.Contains(t.Text)) return i;
                    }
                    i++;
                }
            }
            return i;
        }

        // PROCEDURE name [(] @p type [= default] [OUT|OUTPUT|READONLY], ... [)] AS / RETURNS ...
        private static int ReadParameters(List<SqlToken> tokens, int i, Action<SqlVariable> add)
        {
            i++;
            var owner = new StringBuilder();
            while (i < tokens.Count && (tokens[i].IsName && tokens[i].Kind != TokenKind.Variable || tokens[i].IsSymbol('.')))
                owner.Append(tokens[i++].Value);
            string ownerName = owner.Length > 0 ? owner.ToString() : null;

            int depth = 0;
            while (i < tokens.Count)
            {
                var t = tokens[i];
                if (t.IsSymbol('(')) { depth++; i++; continue; }
                if (t.IsSymbol(')'))
                {
                    if (--depth <= 0) { i++; break; } // a function's parameter list is closed
                    i++; continue;
                }
                if (depth == 0 && (t.IsWord("AS") || t.IsWord("WITH") || t.IsWord("RETURNS") || t.IsWord("FOR"))) break;
                if (t.Kind != TokenKind.Variable) { i++; continue; }

                string name = t.Text;
                i++;
                if (i < tokens.Count && tokens[i].IsWord("AS")) i++;
                string type = ReadType(tokens, ref i);
                string direction = "INPUT";
                int itemDepth = depth;
                while (i < tokens.Count)
                {
                    var u = tokens[i];
                    if (u.IsSymbol('(')) depth++;
                    else if (u.IsSymbol(')')) { if (depth == itemDepth) break; depth--; }
                    else if (depth == itemDepth && (u.IsSymbol(',') || u.Kind == TokenKind.Variable)) break;
                    else if (depth == itemDepth && (u.IsWord("AS") || u.IsWord("WITH") || u.IsWord("RETURNS") || u.IsWord("FOR"))) break;
                    else if (u.IsWord("OUT") || u.IsWord("OUTPUT")) direction = "OUTPUT";
                    else if (u.IsWord("READONLY")) direction = "READONLY";
                    i++;
                }
                add(new SqlVariable(name, type, SqlVariableKind.Parameter, ownerName, direction));
                if (i < tokens.Count && tokens[i].IsSymbol(',')) i++;
            }

            // RETURNS @t TABLE (...): the function's result table.
            if (i < tokens.Count && tokens[i].IsWord("RETURNS") && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Variable)
            {
                string name = tokens[i + 1].Text;
                i += 2;
                add(new SqlVariable(name, ReadType(tokens, ref i), SqlVariableKind.TableVariable, ownerName));
            }
            return i;
        }

        // INT, VARCHAR(50), DECIMAL(18, 2), NVARCHAR(MAX), dbo.MyTableType, TABLE (...) -> "TABLE".
        private static string ReadType(List<SqlToken> tokens, ref int i)
        {
            if (i >= tokens.Count || !tokens[i].IsName || tokens[i].Kind == TokenKind.Variable) return null;
            if (tokens[i].IsWord("TABLE"))
            {
                i++;
                SkipParens(tokens, ref i);
                return "TABLE";
            }

            var parts = new List<string> { tokens[i++].Value };
            while (i + 1 < tokens.Count && tokens[i].IsSymbol('.') && tokens[i + 1].IsName)
            {
                parts.Add(tokens[i + 1].Value);
                i += 2;
            }
            // System types read best upper-case, the way SSMS shows them; user types keep their spelling.
            string type = parts.Count == 1 ? parts[0].ToUpperInvariant() : string.Join(".", parts);

            if (i < tokens.Count && tokens[i].IsSymbol('('))
            {
                var args = new List<string>();
                i++;
                while (i < tokens.Count && !tokens[i].IsSymbol(')'))
                {
                    if (!tokens[i].IsSymbol(',')) args.Add(tokens[i].Kind == TokenKind.Word ? tokens[i].Text.ToUpperInvariant() : tokens[i].Text);
                    i++;
                }
                if (i < tokens.Count) i++;
                type += "(" + string.Join(", ", args) + ")";
            }
            if (i < tokens.Count && tokens[i].IsWord("VARYING")) i++;
            return type;
        }

        private static void SkipParens(List<SqlToken> tokens, ref int i)
        {
            if (i >= tokens.Count || !tokens[i].IsSymbol('(')) return;
            int depth = 0;
            for (; i < tokens.Count; i++)
            {
                if (tokens[i].IsSymbol('(')) depth++;
                else if (tokens[i].IsSymbol(')') && --depth == 0) { i++; return; }
            }
        }

        private static bool IsAloneOnLine(string text, SqlToken t)
        {
            for (int j = t.Start - 1; j >= 0 && text[j] != '\n'; j--)
                if (!char.IsWhiteSpace(text[j])) return false;
            for (int j = t.End; j < text.Length && text[j] != '\n' && text[j] != '\r'; j++)
                if (!char.IsWhiteSpace(text[j])) return false;
            return true;
        }
    }
}
