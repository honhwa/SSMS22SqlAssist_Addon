using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows.Threading;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlAssist.Connection;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Sql;
using SqlAssist.Schema;

namespace SqlAssist.Editor
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [Name("SqlAssist star tip")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class StarTipProvider : IWpfTextViewCreationListener
    {
        [Import] internal IToolTipService ToolTips { get; set; }
        [Import] internal IAsyncCompletionBroker Broker { get; set; }

        public void TextViewCreated(IWpfTextView textView) => new StarTip(textView, ToolTips, Broker);
    }

    /// <summary>Shows "Press Tab to expand N columns" under a "*" that Tab would expand — and only then, so the hint
    /// never promises an expansion that Tab will not do. Re-checked a moment after the caret settles.</summary>
    internal sealed class StarTip
    {
        private readonly IWpfTextView _view;
        private readonly IToolTipService _toolTips;
        private readonly IAsyncCompletionBroker _broker;
        private readonly DispatcherTimer _timer;
        private IToolTipPresenter _presenter;
        private int _shownAt = -1;
        private int _waitingForSchemaAt = -1;

        public StarTip(IWpfTextView view, IToolTipService toolTips, IAsyncCompletionBroker broker)
        {
            _view = view;
            _toolTips = toolTips;
            _broker = broker;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (s, e) => { _timer.Stop(); Evaluate(); };

            _view.Caret.PositionChanged += OnChanged;
            _view.TextBuffer.PostChanged += OnChanged;
            _view.LostAggregateFocus += OnLostFocus;
            _view.Closed += OnClosed;
        }

        private void OnChanged(object sender, EventArgs e)
        {
            _timer.Stop();
            _timer.Start();
        }

        private void OnLostFocus(object sender, EventArgs e) => Hide();

        private void OnClosed(object sender, EventArgs e)
        {
            _timer.Stop();
            Hide();
            _view.Caret.PositionChanged -= OnChanged;
            _view.TextBuffer.PostChanged -= OnChanged;
            _view.LostAggregateFocus -= OnLostFocus;
            _view.Closed -= OnClosed;
        }

        private void Evaluate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (_view.IsClosed || !_view.HasAggregateFocus || _broker.IsCompletionActive(_view) || !_view.Selection.IsEmpty)
                {
                    Hide();
                    return;
                }

                var caret = _view.Caret.Position.BufferPosition;
                var window = TextWindow.Around(caret.Snapshot, caret.Position);
                var star = SqlContextAnalyzer.FindStar(window.Text, window.Caret);
                var target = star == null ? null : ActiveConnection.TryGet();
                if (target == null)
                {
                    Hide();
                    return;
                }

                var schema = SchemaCache.Instance.TryGetLoaded(target);
                if (schema == null)
                {
                    Hide();
                    WaitForSchemaThenRetry(target, caret.Position);
                    return;
                }

                int count = CountColumns(star, schema);
                if (count == 0)
                {
                    Hide();
                    return;
                }

                int start = window.Offset + star.ReplaceStart;
                if (_presenter != null && _shownAt == start) return; // already showing for this star
                Show(new SnapshotSpan(caret.Snapshot, start, star.ReplaceEnd - star.ReplaceStart), count);
                _shownAt = start;
            }
            catch (Exception ex)
            {
                Log.Error("Star tip", ex);
                Hide();
            }
        }

        // First use of a database: the schema is still loading. Check again once it arrives, if the caret stayed put.
        private void WaitForSchemaThenRetry(ConnectionTarget target, int position)
        {
            if (_waitingForSchemaAt == position) return;
            _waitingForSchemaAt = position;
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await SchemaCache.Instance.GetAsync(target, TimeSpan.FromSeconds(15), System.Threading.CancellationToken.None);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _waitingForSchemaAt = -1;
                if (!_view.IsClosed && _view.Caret.Position.BufferPosition.Position == position) Evaluate();
            }).FileAndForget("SqlAssist/StarTipWaitForSchema");
        }

        private static int CountColumns(StarContext star, SchemaModel schema)
        {
            // Mirrors StarExpander: Tab only expands when every table resolves.
            var tables = star.Tables.Select(schema.Find).ToList();
            return tables.Any(t => t == null) ? 0 : tables.Sum(t => t.Columns.Count);
        }

        private void Show(SnapshotSpan span, int count)
        {
            Hide();
            _presenter = _toolTips.CreatePresenter(_view, new ToolTipParameters(trackMouse: false, ignoreBufferChange: false));
            _presenter.Dismissed += (s, e) => { if (ReferenceEquals(s, _presenter)) { _presenter = null; _shownAt = -1; } };
            var text = new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, "Press "),
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, "Tab", ClassifiedTextRunStyle.Bold),
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, $" to expand {count} column{(count == 1 ? "" : "s")}"));
            _presenter.StartOrUpdate(span.Snapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeInclusive), new object[] { text });
        }

        private void Hide()
        {
            var presenter = _presenter;
            _presenter = null;
            _shownAt = -1;
            presenter?.Dismiss();
        }
    }
}
