using Microsoft.VisualStudio.Text;

namespace SqlAssist.Editor
{
    /// <summary>The part of the buffer around the caret that analysis looks at. A statement never needs more,
    /// and it keeps the per-keystroke cost flat in long scripts.</summary>
    internal readonly struct TextWindow
    {
        private const int Radius = 20000;

        private TextWindow(string text, int offset, int caret) { Text = text; Offset = offset; Caret = caret; }

        public string Text { get; }
        /// <summary>Buffer position of Text[0].</summary>
        public int Offset { get; }
        /// <summary>Caret position within Text.</summary>
        public int Caret { get; }

        public static TextWindow Around(ITextSnapshot snapshot, int position)
        {
            int start = position > Radius ? snapshot.GetLineFromPosition(position - Radius).Start.Position : 0;
            int end = snapshot.Length - position > Radius ? snapshot.GetLineFromPosition(position + Radius).End.Position : snapshot.Length;
            return new TextWindow(snapshot.GetText(start, end - start), start, position - start);
        }
    }
}
