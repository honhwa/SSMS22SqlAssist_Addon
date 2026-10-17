using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using SqlAssist.Core.Search;

namespace SqlAssist.Search
{
    /// <summary>The Search window's type filter and match options, kept in %APPDATA%\SqlAssist\search.json.
    /// The database selection is not kept: it belongs to whichever server is connected.</summary>
    [DataContract]
    internal sealed class SearchSettings
    {
        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SqlAssist", "search.json");

        [DataMember] public SearchObjectType Types { get; set; } = SearchObjectType.All;
        [DataMember] public bool MatchCase { get; set; }
        [DataMember] public bool WholeWord { get; set; }
        [DataMember] public bool UseWildcards { get; set; }

        public static SearchSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var stream = File.OpenRead(FilePath))
                        if (new DataContractJsonSerializer(typeof(SearchSettings)).ReadObject(stream) is SearchSettings s)
                        {
                            // The serializer skips initializers, so a hand-edited file without Types would search nothing.
                            if (s.Types == SearchObjectType.None) s.Types = SearchObjectType.All;
                            return s;
                        }
            }
            catch (Exception ex)
            {
                Log.Error("Reading " + FilePath, ex);
            }
            return new SearchSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using (var stream = File.Create(FilePath))
                    new DataContractJsonSerializer(typeof(SearchSettings)).WriteObject(stream, this);
            }
            catch (Exception ex)
            {
                Log.Error("Writing " + FilePath, ex);
            }
        }
    }
}
