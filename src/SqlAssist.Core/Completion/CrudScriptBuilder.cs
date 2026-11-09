using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Search;

namespace SqlAssist.Core.Completion
{
    [Flags]
    public enum CrudActions { None = 0, Select = 1, Insert = 2, Update = 4, Delete = 8, All = Select | Insert | Update | Delete }

    public sealed class CrudOptions
    {
        public const string DefaultNamePattern = "usp_{Table}_{Action}";

        public CrudActions Actions { get; set; } = CrudActions.All;
        /// <summary>{Schema}, {Table} and {Action} (Select, Insert, Update, Delete), case-insensitive.</summary>
        public string NamePattern { get; set; } = DefaultNamePattern;
        /// <summary>CREATE OR ALTER (SQL Server 2016 SP1+) instead of CREATE.</summary>
        public bool CreateOrAlter { get; set; } = true;
        public string NewLine { get; set; } = "\r\n";
    }

    /// <summary>The generated procedures. <see cref="Text"/> is ready to paste: the procedures separated by GO, led by a
    /// comment line for each action that was left out.</summary>
    public sealed class CrudScript
    {
        public CrudScript(string text, IReadOnlyList<string> skipped) { Text = text; Skipped = skipped; }
        public string Text { get; }
        /// <summary>One entry per requested action that could not be generated, e.g. "Update: no primary key".</summary>
        public IReadOnlyList<string> Skipped { get; }
    }

    /// <summary>Select / Insert / Update / Delete stored procedures for a table, keyed on its primary key. Never guesses a
    /// key: a table without one gets no Update or Delete.</summary>
    public static class CrudScriptBuilder
    {
        private const string Indent = "    ";

        public static CrudScript Build(DbObject table, CrudOptions options)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (table.IsView) throw new ArgumentException("CRUD procedures are generated for tables, not views.", nameof(table));
            options = options ?? new CrudOptions();
            string nl = options.NewLine ?? "\r\n";

            // One name per column, shared by every procedure: a duplicate after cleaning gets a number.
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parameters = new Dictionary<DbColumn, string>();
            foreach (var c in table.Columns) parameters[c] = StatementExpander.ParameterName(c.Name, used);

            var keys = table.Columns.Where(c => c.IsPrimaryKey).ToList();
            var skipped = new List<string>();
            var procedures = new List<string>();
            foreach (var action in new[] { CrudActions.Select, CrudActions.Insert, CrudActions.Update, CrudActions.Delete })
            {
                if ((options.Actions & action) == 0) continue;
                string body = null, why = null;
                switch (action)
                {
                    case CrudActions.Select: body = Select(table, keys, parameters, nl); break;
                    case CrudActions.Insert: body = Insert(table, parameters, nl); break;
                    case CrudActions.Update:
                        if (keys.Count == 0) why = "no primary key";
                        else if (!table.Columns.Any(c => StatementExpander.Writable(c) && !c.IsPrimaryKey)) why = "no column to set besides the key";
                        else body = Update(table, keys, parameters, nl);
                        break;
                    case CrudActions.Delete:
                        if (keys.Count == 0) why = "no primary key";
                        else body = Delete(table, keys, parameters, nl);
                        break;
                }
                if (body == null) { skipped.Add(action + ": " + why); continue; }

                string name = ProcedureName(options.NamePattern, table.Schema, table.Name, action);
                var sb = new StringBuilder();
                sb.Append(options.CreateOrAlter ? "CREATE OR ALTER PROCEDURE " : "CREATE PROCEDURE ")
                  .Append(TableScriptBuilder.Bracket(table.Schema)).Append('.').Append(TableScriptBuilder.Bracket(name)).Append(nl);
                sb.Append(body).Append("GO").Append(nl);
                procedures.Add(sb.ToString());
            }

            var text = new StringBuilder();
            foreach (var s in skipped) text.Append("-- Skipped ").Append(s).Append(nl);
            if (skipped.Count > 0 && procedures.Count > 0) text.Append(nl);
            text.Append(string.Join(nl, procedures));
            return new CrudScript(text.ToString(), skipped);
        }

        /// <summary>The pattern with {Schema}, {Table} and {Action} filled in; a blank pattern means the default one.
        /// The name is always bracketed in the script, so it needs no cleaning.</summary>
        public static string ProcedureName(string pattern, string schema, string table, CrudActions action)
        {
            if (string.IsNullOrWhiteSpace(pattern)) pattern = CrudOptions.DefaultNamePattern;
            return Replace(Replace(Replace(pattern.Trim(), "{Schema}", schema), "{Table}", table), "{Action}", action.ToString());
        }

        private static string Replace(string text, string token, string value)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (true)
            {
                int at = text.IndexOf(token, i, StringComparison.OrdinalIgnoreCase);
                if (at < 0) { sb.Append(text, i, text.Length - i); return sb.ToString(); }
                sb.Append(text, i, at - i).Append(value);
                i = at + token.Length;
            }
        }

        private static string Select(DbObject table, List<DbColumn> keys, Dictionary<DbColumn, string> parameters, string nl)
        {
            var sb = new StringBuilder();
            AppendParameters(sb, keys.Select(k => Declare(k, parameters, " = NULL")).ToList(), nl);
            sb.Append("AS").Append(nl).Append("BEGIN").Append(nl).Append(Indent).Append("SET NOCOUNT ON;").Append(nl).Append(nl);
            sb.Append(Indent).Append("SELECT ").Append(string.Join(", ", table.Columns.Select(c => TableScriptBuilder.Bracket(c.Name)))).Append(nl);
            sb.Append(Indent).Append("FROM ").Append(Table(table));
            if (keys.Count > 0)
            {
                sb.Append(nl).Append(Indent).Append("WHERE ");
                sb.Append(string.Join(nl + Indent + "  AND ", keys.Select(k =>
                    "(" + parameters[k] + " IS NULL OR " + TableScriptBuilder.Bracket(k.Name) + " = " + parameters[k] + ")")));
            }
            sb.Append(';').Append(nl).Append("END").Append(nl);
            return sb.ToString();
        }

        private static string Insert(DbObject table, Dictionary<DbColumn, string> parameters, string nl)
        {
            var writable = table.Columns.Where(StatementExpander.Writable).ToList();
            var identity = table.Columns.FirstOrDefault(c => c.IsIdentity);
            var declarations = writable.Select(c => Declare(c, parameters, c.IsNullable ? " = NULL" : "")).ToList();
            if (identity != null) declarations.Add(Declare(identity, parameters, " OUTPUT"));

            var sb = new StringBuilder();
            AppendParameters(sb, declarations, nl);
            sb.Append("AS").Append(nl).Append("BEGIN").Append(nl).Append(Indent).Append("SET NOCOUNT ON;").Append(nl).Append(nl);
            if (writable.Count == 0)
                sb.Append(Indent).Append("INSERT INTO ").Append(Table(table)).Append(" DEFAULT VALUES;");
            else
                sb.Append(Indent).Append("INSERT INTO ").Append(Table(table)).Append(" (")
                  .Append(string.Join(", ", writable.Select(c => TableScriptBuilder.Bracket(c.Name)))).Append(')').Append(nl)
                  .Append(Indent).Append("VALUES (").Append(string.Join(", ", writable.Select(c => parameters[c]))).Append(");");
            sb.Append(nl);
            if (identity != null) sb.Append(nl).Append(Indent).Append("SET ").Append(parameters[identity]).Append(" = SCOPE_IDENTITY();").Append(nl);
            sb.Append("END").Append(nl);
            return sb.ToString();
        }

        private static string Update(DbObject table, List<DbColumn> keys, Dictionary<DbColumn, string> parameters, string nl)
        {
            var set = table.Columns.Where(c => StatementExpander.Writable(c) && !c.IsPrimaryKey).ToList();
            var sb = new StringBuilder();
            // No defaults: a forgotten parameter must not silently turn into NULL in a full-row UPDATE.
            AppendParameters(sb, keys.Concat(set).Select(c => Declare(c, parameters, "")).ToList(), nl);
            sb.Append("AS").Append(nl).Append("BEGIN").Append(nl).Append(Indent).Append("SET NOCOUNT ON;").Append(nl).Append(nl);
            sb.Append(Indent).Append("UPDATE ").Append(Table(table)).Append(nl);
            sb.Append(Indent).Append("SET ").Append(string.Join("," + nl + Indent + "    ",
                set.Select(c => TableScriptBuilder.Bracket(c.Name) + " = " + parameters[c]))).Append(nl);
            AppendWhere(sb, keys, parameters, nl);
            sb.Append(nl).Append("END").Append(nl);
            return sb.ToString();
        }

        private static string Delete(DbObject table, List<DbColumn> keys, Dictionary<DbColumn, string> parameters, string nl)
        {
            var sb = new StringBuilder();
            AppendParameters(sb, keys.Select(k => Declare(k, parameters, "")).ToList(), nl);
            sb.Append("AS").Append(nl).Append("BEGIN").Append(nl).Append(Indent).Append("SET NOCOUNT ON;").Append(nl).Append(nl);
            sb.Append(Indent).Append("DELETE FROM ").Append(Table(table)).Append(nl);
            AppendWhere(sb, keys, parameters, nl);
            sb.Append(nl).Append("END").Append(nl);
            return sb.ToString();
        }

        private static void AppendWhere(StringBuilder sb, List<DbColumn> keys, Dictionary<DbColumn, string> parameters, string nl)
        {
            sb.Append(Indent).Append("WHERE ").Append(string.Join(nl + Indent + "  AND ",
                keys.Select(k => TableScriptBuilder.Bracket(k.Name) + " = " + parameters[k]))).Append(';');
        }

        /// <summary>One parameter per line; nothing at all for a procedure without parameters.</summary>
        private static void AppendParameters(StringBuilder sb, IReadOnlyList<string> declarations, string nl)
        {
            for (int i = 0; i < declarations.Count; i++)
                sb.Append(Indent).Append(declarations[i]).Append(i < declarations.Count - 1 ? "," : "").Append(nl);
        }

        private static string Declare(DbColumn column, Dictionary<DbColumn, string> parameters, string suffix) =>
            parameters[column] + " " + (string.IsNullOrWhiteSpace(column.TypeName) ? "sql_variant" : column.TypeName) + suffix;

        private static string Table(DbObject table) => TableScriptBuilder.Bracket(table.Schema) + "." + TableScriptBuilder.Bracket(table.Name);
    }
}
