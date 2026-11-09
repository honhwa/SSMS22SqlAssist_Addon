using SqlAssist.Core.Completion;

namespace SqlAssist.Core.Settings
{
    /// <summary>What the Search window's Generate CRUD procedures dialog remembers between sessions.</summary>
    public sealed class CrudSettings
    {
        /// <summary>{Schema}, {Table}, {Action}; a blank value in settings.xml means the default.</summary>
        public string NamePattern { get; set; } = CrudOptions.DefaultNamePattern;

        public bool CreateOrAlter { get; set; } = true;
    }
}
