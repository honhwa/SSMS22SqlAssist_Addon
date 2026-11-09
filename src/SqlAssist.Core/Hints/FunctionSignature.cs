using System.Collections.Generic;

namespace SqlAssist.Core.Hints
{
    /// <summary>One argument of a function, as it is written in the function's syntax.</summary>
    public sealed class SignatureParameter
    {
        public SignatureParameter(string name, string typeName = null, bool isOptional = false, bool isVariadic = false, string description = null)
        {
            Name = name; TypeName = typeName; IsOptional = isOptional; IsVariadic = isVariadic; Description = description;
        }

        /// <summary>"start", "@amount" (a user-defined function), "...n".</summary>
        public string Name { get; }
        /// <summary>The declared type ("money") when the metadata has one; null for a built-in function, whose syntax names no type.</summary>
        public string TypeName { get; }
        /// <summary>May be left out (the syntax shows it in square brackets).</summary>
        public bool IsOptional { get; }
        /// <summary>May be repeated: every argument from this one on belongs to it. Only ever the last parameter.</summary>
        public bool IsVariadic { get; }
        public string Description { get; }

        /// <summary>The name with its type, the way a user-defined function's parameter is declared: "@amount money".</summary>
        public string Label => string.IsNullOrEmpty(TypeName) ? Name : Name + " " + TypeName;
    }

    /// <summary>The calling syntax of one function: what the parameter hint draws.</summary>
    public sealed class FunctionSignature
    {
        public FunctionSignature(string displayName, IReadOnlyList<SignatureParameter> parameters, string summary = null,
            string returnType = null, bool isBuiltIn = false)
        {
            DisplayName = displayName; Parameters = parameters; Summary = summary; ReturnType = returnType; IsBuiltIn = isBuiltIn;
        }

        /// <summary>"SUBSTRING", or "dbo.fnTax" for a user-defined function.</summary>
        public string DisplayName { get; }
        public IReadOnlyList<SignatureParameter> Parameters { get; }
        /// <summary>One short sentence; a user-defined function's MS_Description.</summary>
        public string Summary { get; }
        /// <summary>A scalar function's return type; "TABLE" for a table-valued function; null for a built-in function.</summary>
        public string ReturnType { get; }
        public bool IsBuiltIn { get; }

        /// <summary>Which parameter the argument at <paramref name="argumentIndex"/> (0 = first) is: a variadic last parameter takes
        /// every argument from its own position on, and an argument past the end of a fixed list has none (-1), so the hint
        /// shows the syntax without pointing at anything instead of pointing at the wrong parameter.</summary>
        public int ParameterAt(int argumentIndex)
        {
            if (argumentIndex < 0 || Parameters.Count == 0) return -1;
            if (argumentIndex < Parameters.Count) return argumentIndex;
            return Parameters[Parameters.Count - 1].IsVariadic ? Parameters.Count - 1 : -1;
        }
    }
}
