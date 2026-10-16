using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlAssist.Core.Completion;

namespace SqlAssist.Editor
{
    [Export(typeof(IAsyncCompletionItemManagerProvider))]
    [Name("SqlAssist fuzzy item manager")]
    [ContentType(SqlContentType.Name)]
    [Order(Before = "default")]
    internal sealed class SqlItemManagerProvider : IAsyncCompletionItemManagerProvider
    {
        private static readonly SqlItemManager Instance = new SqlItemManager();
        public IAsyncCompletionItemManager GetOrCreate(ITextView textView) => Instance;
    }

    /// <summary>Filters and ranks with <see cref="FuzzyMatcher"/>: typing "custid" finds CustomerID, "od" finds OrderDetails.</summary>
    internal sealed class SqlItemManager : IAsyncCompletionItemManager
    {
        public Task<ImmutableArray<CompletionItem>> SortCompletionListAsync(IAsyncCompletionSession session,
            AsyncCompletionSessionInitialDataSnapshot data, CancellationToken token) =>
            Task.FromResult(data.InitialItemList.OrderBy(i => i.SortText, System.StringComparer.OrdinalIgnoreCase).ToImmutableArray());

        public Task<FilteredCompletionModel> UpdateCompletionListAsync(IAsyncCompletionSession session,
            AsyncCompletionSessionDataSnapshot data, CancellationToken token)
        {
            string typed = session.ApplicableToSpan.GetText(data.Snapshot).TrimStart('[');

            if (typed.Length == 0)
            {
                // Nothing typed yet (list opened by "." or "JOIN "): show everything, but only soft-select,
                // so Enter still means a new line.
                var all = data.InitialSortedItemList.Select(i => new CompletionItemWithHighlight(i)).ToImmutableArray();
                return Task.FromResult(new FilteredCompletionModel(all, 0, data.SelectedFilters, UpdateSelectionHint.SoftSelected,
                    centerSelection: true, uniqueItem: null));
            }

            var ranked = data.InitialSortedItemList
                .Select((item, index) => (item, index, score: FuzzyMatcher.Score(typed, item.FilterText)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.index) // ties keep group order: JOIN suggestions before the plain table list
                .Select(x => new CompletionItemWithHighlight(x.item))
                .ToImmutableArray();

            return Task.FromResult(new FilteredCompletionModel(ranked, 0, data.SelectedFilters,
                ranked.IsEmpty ? UpdateSelectionHint.NoChange : UpdateSelectionHint.Selected,
                centerSelection: true, uniqueItem: null));
        }
    }
}
