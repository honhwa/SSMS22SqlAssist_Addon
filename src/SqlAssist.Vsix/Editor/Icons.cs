using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Text.Adornments;

namespace SqlAssist.Editor
{
    internal static class Icons
    {
        public static readonly ImageElement Table = Make(KnownMonikers.Table, "Table");
        public static readonly ImageElement View = Make(KnownMonikers.View, "View");
        public static readonly ImageElement Column = Make(KnownMonikers.Column, "Column");
        public static readonly ImageElement Join = Make(KnownMonikers.Relationship, "Join");
        public static readonly ImageElement Schema = Make(KnownMonikers.Database, "Schema");
        public static readonly ImageElement Keyword = Make(KnownMonikers.IntellisenseKeyword, "Keyword");
        public static readonly ImageElement Snippet = Make(KnownMonikers.Snippet, "Snippet");
        public static readonly ImageElement Variable = Make(KnownMonikers.LocalVariable, "Variable");
        public static readonly ImageElement Parameter = Make(KnownMonikers.Parameter, "Parameter");

        private static ImageElement Make(ImageMoniker moniker, string name) => new ImageElement(new ImageId(moniker.Guid, moniker.Id), name);
    }
}
