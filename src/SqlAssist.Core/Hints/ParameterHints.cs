using System;
using System.Collections.Generic;
using System.Linq;
using SqlAssist.Core.Schema;

namespace SqlAssist.Core.Hints
{
    /// <summary>A piece of the signature line; the active parameter is the one to draw in bold.</summary>
    public readonly struct HintRun
    {
        public HintRun(string text, bool isActive = false) { Text = text; IsActive = isActive; }
        public string Text { get; }
        public bool IsActive { get; }
    }

    /// <summary>What a parameter hint shows, as plain data for the editor to draw.</summary>
    public sealed class ParameterHint
    {
        public ParameterHint(FunctionSignature signature, int activeParameter, IReadOnlyList<HintRun> line, string parameterNote, string summary)
        {
            Signature = signature; ActiveParameter = activeParameter; Line = line; ParameterNote = parameterNote; Summary = summary;
        }

        public FunctionSignature Signature { get; }
        /// <summary>Index into the signature's parameters; -1 when the argument is past the end of a fixed list.</summary>
        public int ActiveParameter { get; }
        /// <summary>"SUBSTRING(expression, start, length)", in pieces.</summary>
        public IReadOnlyList<HintRun> Line { get; }
        /// <summary>The active parameter's own description, if it has one.</summary>
        public string ParameterNote { get; }
        /// <summary>Description of the function: a built-in's one sentence, a user-defined function's MS_Description and return type.</summary>
        public string Summary { get; }

        public string PlainLine => string.Concat(Line.Select(r => r.Text));
    }

    public static class ParameterHints
    {
        // What stands before a name when it is declared, created or used as a table or procedure — not called as a function.
        private static readonly HashSet<string> NotACall = new HashSet<string>
        {
            "INTO", "TABLE", "UPDATE", "EXEC", "EXECUTE", "PROC", "PROCEDURE", "FUNCTION", "VIEW", "TRIGGER",
        };
        // A built-in function's name in these places is a table name (FROM Year (NOLOCK)).
        private static readonly HashSet<string> TableLeaders = new HashSet<string> { "FROM", "JOIN", "APPLY" };

        /// <summary>Whether <see cref="Resolve"/> needs the schema for this call: a schema-qualified name is a user-defined function.</summary>
        public static bool NeedsSchema(CallSite site) =>
            site != null && site.NameParts.Count == 2 && !(site.Leader != null && NotACall.Contains(site.Leader));

        /// <summary>The syntax of the function being called, or null when there is nothing certain to show: the name is not a function
        /// we know, it has special syntax, it is a table / procedure name, or it is user-defined and the schema is not loaded.
        /// Only the name decides — arguments are never used to pick an overload.</summary>
        public static FunctionSignature Resolve(CallSite site, SchemaModel schema)
        {
            if (site == null) return null;
            if (site.Leader != null && NotACall.Contains(site.Leader)) return null;
            if (site.NameParts.Count == 1)
            {
                if (site.Leader != null && TableLeaders.Contains(site.Leader)) return null;
                return BuiltInSignatures.Find(site.Name);   // null for an unknown name and for special syntax
            }
            if (site.NameParts.Count != 2 || schema == null) return null;   // three-part names point at another database: not resolved

            var routine = schema.FindRoutine(site.Schema, site.Name);
            if (routine == null || routine.Kind == RoutineKind.Procedure) return null;   // EXEC parameter hints are not part of this feature
            var parameters = routine.Parameters
                .Select(p => new SignatureParameter(p.Name, p.IsReadOnly ? p.TypeName + " READONLY" : p.TypeName))
                .ToArray();
            string returns = routine.Kind == RoutineKind.TableFunction ? "Returns TABLE"
                : string.IsNullOrEmpty(routine.ReturnType) ? null : "Returns " + routine.ReturnType;
            string summary = string.IsNullOrWhiteSpace(routine.Description) ? returns
                : returns == null ? routine.Description.Trim() : routine.Description.Trim() + "\n" + returns;
            return new FunctionSignature(routine.Schema + "." + routine.Name, parameters, summary, routine.ReturnType);
        }

        /// <summary>The hint for <paramref name="signature"/> with the parameter at <paramref name="argumentIndex"/> marked.</summary>
        public static ParameterHint Build(FunctionSignature signature, int argumentIndex)
        {
            int active = signature.ParameterAt(argumentIndex);
            var runs = new List<HintRun> { new HintRun(signature.DisplayName + "(") };
            var ps = signature.Parameters;
            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                string open = p.IsOptional ? (i == 0 ? "[" : " [, ") : (i == 0 ? "" : ", ");
                if (open.Length > 0) runs.Add(new HintRun(open));
                runs.Add(new HintRun(p.Label, i == active));
                if (p.IsOptional) runs.Add(new HintRun("]" + (p.IsVariadic && !p.Name.StartsWith("...", StringComparison.Ordinal) ? "..." : "")));
            }
            runs.Add(new HintRun(")"));
            string note = active >= 0 ? ps[active].Description : null;
            return new ParameterHint(signature, active, runs, note, signature.Summary);
        }
    }
}
