using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.Text;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;

namespace SqlAssist.Editor
{
    /// <summary>Shares analysis of an immutable snapshot between completion, Tab and its hint.</summary>
    internal static class EditorLocalTables
    {
        private static readonly ConditionalWeakTable<ITextSnapshot, Lazy<LocalTableIndex>> Indices =
            new ConditionalWeakTable<ITextSnapshot, Lazy<LocalTableIndex>>();

        public static IReadOnlyList<DbObject> At(SnapshotPoint caret)
        {
            var index = Indices.GetValue(caret.Snapshot, snapshot => new Lazy<LocalTableIndex>(() =>
            {
                string text = snapshot.GetText();
                return text.IndexOf('#') < 0 && text.IndexOf('@') < 0 ? LocalTableIndex.Parse("") : LocalTableIndex.Parse(text);
            }));
            return index.Value.At(caret.Position);
        }
    }
}
