using SqlAssist.Core.Search;

namespace SqlAssist.Search
{
    /// <summary>The Search window's type filter and match options. Kept in memory only, so every SSMS session starts
    /// from the defaults (all object types, plain case-insensitive contains). Earlier versions saved them to
    /// %APPDATA%\SqlAssist\search.json; that file is no longer read.</summary>
    internal sealed class SearchSettings
    {
        public SearchObjectType Types { get; set; } = SearchObjectType.All;
        public bool MatchCase { get; set; }
        public bool WholeWord { get; set; }
        public bool UseWildcards { get; set; }
    }
}
