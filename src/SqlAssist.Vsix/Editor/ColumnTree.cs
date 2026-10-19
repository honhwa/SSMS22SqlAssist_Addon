using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text.Adornments;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;

namespace SqlAssist.Editor
{
    /// <summary>A completion item that is a node of the table tree ("SELECT |" with no FROM): a table, or one of its columns.</summary>
    internal sealed class TreeNode
    {
        public TreeNode(DbObject table, DbColumn column = null) { Table = table; Column = column; }
        public DbObject Table { get; }
        /// <summary>Null for the table node itself.</summary>
        public DbColumn Column { get; }
        public bool IsTable => Column == null;
        public string Key => Column == null ? TableKey(Table) : TableKey(Table) + "|" + Column.Name;
        public static string TableKey(DbObject t) => t.ToString();
    }

    /// <summary>Per completion session: which tables are expanded, what is ticked (in tick order) and the column items
    /// made for expanded tables. Lives in the session's property bag, shared by the item manager (which lists children
    /// under expanded tables), the presenter (expander/checkbox clicks), the command filter (←/→, Shift+Space) and the
    /// commit manager (which turns the ticks into a query).</summary>
    internal sealed class TreeState
    {
        private readonly Dictionary<string, List<CompletionItem>> _children = new Dictionary<string, List<CompletionItem>>();
        private readonly List<string> _checkedOrder = new List<string>();
        private readonly Dictionary<string, TreeNode> _checked = new Dictionary<string, TreeNode>();

        public SchemaModel Schema { get; set; } = SchemaModel.Empty;
        public IReadOnlyCollection<string> TakenAliases { get; set; } = new string[0];
        public HashSet<string> Expanded { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The item to select after the list is rebuilt by an expand/collapse.</summary>
        public CompletionItem SelectAfterUpdate { get; set; }

        public static TreeState For(IAsyncCompletionSession session) =>
            session.Properties.GetOrCreateSingletonProperty(() => new TreeState());

        public static TreeNode NodeOf(CompletionItem item) =>
            item != null && item.Properties.TryGetProperty(typeof(TreeNode), out TreeNode node) ? node : null;

        public bool HasChecks => _checkedOrder.Count > 0;
        public bool IsChecked(TreeNode node) => _checked.ContainsKey(node.Key);

        public void ToggleCheck(TreeNode node)
        {
            if (_checked.Remove(node.Key)) _checkedOrder.Remove(node.Key);
            else { _checked[node.Key] = node; _checkedOrder.Add(node.Key); }
        }

        public void ToggleExpanded(TreeNode table)
        {
            if (!Expanded.Remove(table.Key)) Expanded.Add(table.Key);
        }

        /// <summary>The table item a column item sits under.</summary>
        public CompletionItem ParentItem(TreeNode column) =>
            _parents.TryGetValue(TreeNode.TableKey(column.Table), out var item) ? item : null;

        /// <summary>Re-runs the item manager (to show/hide columns, or redraw ticks) and selects <paramref name="select"/> after.</summary>
        public static void Refresh(IAsyncCompletionSession session, ITextView view, CompletionItem select)
        {
            For(session).SelectAfterUpdate = select;
            var caret = view.Caret.Position.BufferPosition;
            session.OpenOrUpdate(new CompletionTrigger(CompletionTriggerReason.FilterChange, caret.Snapshot), caret, CancellationToken.None);
        }

        private readonly Dictionary<string, CompletionItem> _parents = new Dictionary<string, CompletionItem>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The column items shown under an expanded table, made once per table and session.</summary>
        public List<CompletionItem> ChildrenOf(CompletionItem tableItem, TreeNode table)
        {
            string key = TreeNode.TableKey(table.Table);
            _parents[key] = tableItem;
            if (_children.TryGetValue(key, out var list)) return list;
            list = new List<CompletionItem>();
            foreach (var c in table.Table.Columns)
            {
                var item = new CompletionItem(c.Name, tableItem.Source, Icons.Column, ImmutableArray<CompletionFilter>.Empty,
                    (c.TypeName ?? "").ToUpperInvariant(), c.Name, tableItem.SortText + "|" + c.Name, c.Name, ImmutableArray<ImageElement>.Empty);
                item.Properties.AddProperty(typeof(TreeNode), new TreeNode(table.Table, c));
                item.Properties.AddProperty(ItemProps.Description, ColumnInfo.Describe(Schema, table.Table, c));
                list.Add(item);
            }
            return _children[key] = list;
        }

        /// <summary>What a commit inserts: the ticked tables/columns (tables in tick order), or else the committed node.</summary>
        public IReadOnlyList<PickedTable> Picks(TreeNode committed)
        {
            var nodes = HasChecks ? _checkedOrder.Select(k => _checked[k]).ToList() : new List<TreeNode> { committed };
            var picks = new List<PickedTable>();
            foreach (var group in nodes.GroupBy(n => TreeNode.TableKey(n.Table)))
            {
                var table = group.First().Table;
                // A ticked table means all of its columns, whatever else of it is ticked.
                picks.Add(group.Any(n => n.IsTable)
                    ? new PickedTable(table)
                    : new PickedTable(table, table.Columns.Where(c => group.Any(n => n.Column == c)).Select(c => c.Name)));
            }
            return picks;
        }
    }
}
