using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlAssist.Connection;
using SqlAssist.Core.Hints;
using SqlAssist.Core.Schema;
using SqlAssist.Schema;
using SqlAssist.Settings;

namespace SqlAssist.Editor
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [Name("SqlAssist parameter hints")]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class ParameterHintTipProvider : IWpfTextViewCreationListener
    {
        [Import] internal IToolTipService ToolTips { get; set; }
        [Import] internal IAsyncCompletionBroker CompletionBroker { get; set; }
        [Import] internal IAsyncQuickInfoBroker QuickInfoBroker { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            try
            {
                new ParameterHintTip(textView, ToolTips, CompletionBroker, QuickInfoBroker);
            }
            catch (Exception ex)
            {
                Log.Error("Parameter hints: creating", ex);
            }
        }
    }

    /// <summary>Function parameter hints: inside "SUBSTRING(a, |)" shows "SUBSTRING(expression, start, length)" with the parameter
    /// at the caret in bold, in the editor's own tooltip popup (no focus, nothing written, no undo item). The call and the
    /// argument come from Core's <see cref="FunctionCallFinder"/> (only the text just before the caret is read, never the document);
    /// the syntax from <see cref="ParameterHints"/> (built-in table, or the schema cache for dbo.fn(...) — nothing is queried here,
    /// and the cache loads a database's schema at most once per 10 minutes, as for completion). Re-checked a moment after the caret or the text settles, like the other editor tips.</summary>
    internal sealed class ParameterHintTip
    {
        /// <summary>Set on the view while the Tab-expansion tip is up, so the two tips never stack.</summary>
        internal const string ExpansionTipKey = "SqlAssist.ExpansionTipVisible";

        private readonly IWpfTextView _view;
        private readonly IToolTipService _toolTips;
        private readonly IAsyncCompletionBroker _completion;
        private readonly IAsyncQuickInfoBroker _quickInfo;
        private readonly DispatcherTimer _timer;
        private readonly CancellationTokenSource _closed = new CancellationTokenSource();
        private IToolTipPresenter _presenter;
        private string _shownKey;
        private ITrackingPoint _dismissedAt;   // the "(" of the call the user closed with Esc
        private ITrackingPoint _dismissedPoint;   // the "(" of the hint on screen
        private int _waitingForSchemaAt = -1;
        private int _popupRetries;   // how many times in a row a completion / quick info popup has held the hint back
        private bool _disposed;

        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan SlowPoll = TimeSpan.FromSeconds(1);
        private const int FastRetries = 5, MaxRetries = 65;   // ~1.5 s at the debounce, then once a second for about a minute

        public ParameterHintTip(IWpfTextView view, IToolTipService toolTips, IAsyncCompletionBroker completion, IAsyncQuickInfoBroker quickInfo)
        {
            _view = view;
            _toolTips = toolTips;
            _completion = completion;
            _quickInfo = quickInfo;
            // A little slower than the expansion tip's 250 ms, so when both have something to say that one is decided first.
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Debounce };
            _timer.Tick += OnTick;

            _view.Caret.PositionChanged += OnChanged;
            _view.TextBuffer.PostChanged += OnChanged;
            _view.GotAggregateFocus += OnChanged;
            _view.LostAggregateFocus += OnLostFocus;
            _view.VisualElement.PreviewKeyDown += OnPreviewKeyDown;
            _view.Closed += OnClosed;
            VSColorTheme.ThemeChanged += OnThemeChanged;
        }

        // Every handler catches for itself: an exception here would reach the WPF dispatcher, i.e. SSMS.
        private void OnChanged(object sender, EventArgs e)
        {
            try
            {
                if (_disposed) return;
                _popupRetries = 0;
                _timer.Interval = Debounce;
                _timer.Stop();
                _timer.Start();
            }
            catch (Exception ex)
            {
                Log.Error("Parameter hints: scheduling", ex);
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _timer.Stop();
                Evaluate();
            }
            catch (Exception ex)
            {
                Log.Error("Parameter hints: tick", ex);
            }
        }

        private void OnLostFocus(object sender, EventArgs e) => Hide();

        // A hint drawn with the old theme's colours is dropped and drawn again with the new ones. Hops to the UI thread first, in
        // case the theme event is raised from another one.
        private void OnThemeChanged(ThemeChangedEventArgs e)
        {
            try
            {
                if (_disposed) return;
#pragma warning disable VSSDK007
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (_disposed) return;
                    Hide();
                    OnChanged(this, EventArgs.Empty);
                }).FileAndForget("SqlAssist/ParameterHintTheme");
#pragma warning restore VSSDK007
            }
            catch (Exception ex)
            {
                Log.Error("Parameter hints: theme", ex);
            }
        }

        // Esc closes the hint and keeps it closed while the caret stays in that call. The key is not marked handled, so whatever else
        // listens for Esc still gets it; nothing in the buffer or the selection is touched.
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key != Key.Escape || _presenter == null) return;
                _dismissedAt = _dismissedPoint;
                Hide();
                _timer.Stop();   // an evaluation already queued by an earlier caret move must not bring it back
            }
            catch (Exception ex)
            {
                Log.Error("Parameter hints: Esc", ex);
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            try
            {
                if (_disposed) return;
                _disposed = true;
                _timer.Stop();
                _timer.Tick -= OnTick;
                _closed.Cancel();
                Hide();
                _view.Caret.PositionChanged -= OnChanged;
                _view.TextBuffer.PostChanged -= OnChanged;
                _view.GotAggregateFocus -= OnChanged;
                _view.LostAggregateFocus -= OnLostFocus;
                _view.VisualElement.PreviewKeyDown -= OnPreviewKeyDown;
                _view.Closed -= OnClosed;
                VSColorTheme.ThemeChanged -= OnThemeChanged;
                _dismissedAt = null;
                _dismissedPoint = null;
                _closed.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Parameter hints: closing", ex);
            }
        }

        private void Evaluate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            try
            {
                if (_view.IsClosed || !SettingsProvider.ShowParameterHints || !_view.HasAggregateFocus || !_view.Selection.IsEmpty)
                {
                    Hide();
                    return;
                }

                // Never on top of another popup: the completion list and a hover tooltip win. Checked before any analysis, and repeated
                // (every 300 ms, then every second, for about a minute at most) so the hint comes back by itself when the popup closes —
                // Esc on the list moves nothing, so no editor event would. Any caret or text change starts the count again.
                if (_completion.IsCompletionActive(_view) || _quickInfo.IsQuickInfoActive(_view))
                {
                    Hide();
                    if (_popupRetries < MaxRetries)
                    {
                        _timer.Interval = ++_popupRetries > FastRetries ? SlowPoll : Debounce;
                        _timer.Start();
                    }
                    return;
                }

                // The text analysed and the text the hint is drawn against are the same snapshot: nothing here yields to the editor.
                var snapshot = _view.TextSnapshot;
                int caret = _view.Caret.Position.BufferPosition.TranslateTo(snapshot, PointTrackingMode.Positive).Position;
                int from = WindowStart(snapshot, caret);
                string text = snapshot.GetText(from, caret - from);
                var site = FunctionCallFinder.Find(text, text.Length);
                if (site == null)
                {
                    _dismissedAt = null;
                    Hide();
                    return;
                }
                int openParen = from + site.OpenParen;
                int nameStart = from + site.NameStart;

                if (_dismissedAt != null)
                {
                    if (_dismissedAt.GetPosition(snapshot) == openParen)
                    {
                        Hide();
                        return;
                    }
                    _dismissedAt = null;   // the caret is in another call now: hints are wanted again
                }

                SchemaModel schema = null;
                if (ParameterHints.NeedsSchema(site))
                {
                    var target = EditorConnection.Get(TextWindow.Around(snapshot, caret));
                    // Never waits. The cache starts a load only for a database it has none for (or whose copy is 10 minutes old), so this
                    // is not a query per keystroke — but the first schema-qualified name before "(" in a database does cause that one load.
                    schema = target == null ? null : SchemaCache.Instance.TryGetLoaded(target);
                    if (schema == null)
                    {
                        Hide();
                        if (target != null) WaitForSchemaThenRetry(target, caret);
                        return;
                    }
                }

                var signature = ParameterHints.Resolve(site, schema);
                if (signature == null)
                {
                    Hide();
                    return;
                }

                if (_view.Properties.ContainsProperty(ExpansionTipKey))
                {
                    Hide();
                    return;
                }

                var hint = ParameterHints.Build(signature, site.ArgumentIndex);
                var anchor = Anchor(snapshot, nameStart, openParen, caret);
                string key = openParen + "|" + signature.DisplayName + "|" + hint.ActiveParameter + "|" + anchor.Start + "|" + anchor.Length;
                if (_presenter != null && key == _shownKey) return;
                Show(snapshot, anchor, hint, key, snapshot.CreateTrackingPoint(openParen, PointTrackingMode.Positive));
            }
            catch (Exception ex)
            {
                Log.Error("Parameter hints", ex);
                Hide();
            }
        }

        /// <summary>Where analysis starts: far enough back for any call a person writes, on a line start so the first token is whole.</summary>
        private static int WindowStart(ITextSnapshot snapshot, int caret)
        {
            if (caret <= FunctionCallFinder.MaxBack) return 0;
            int from = caret - FunctionCallFinder.MaxBack;
            var line = snapshot.GetLineFromPosition(from);
            // A line that long is cut where it is, rather than copying the whole of it on every caret move.
            return from - line.Start.Position > FunctionCallFinder.MaxBack ? from : line.Start.Position;
        }

        // Under the function name while the caret is on the same line as the "(" — the popup then sits where the call started and the
        // caret's line stays readable. For a call that goes on over several lines it sits under the caret's own line instead.
        private static SnapshotSpan Anchor(ITextSnapshot snapshot, int nameStart, int openParen, int caret)
        {
            var parenLine = snapshot.GetLineFromPosition(openParen);
            var caretLine = snapshot.GetLineFromPosition(caret);
            if (parenLine.LineNumber == caretLine.LineNumber || caretLine.Length == 0)
                return new SnapshotSpan(snapshot, nameStart, openParen + 1 - nameStart);
            return caretLine.Extent;
        }

        // First use of a database: the schema is still loading. Check again once it arrives, if the caret stayed put.
        private void WaitForSchemaThenRetry(ConnectionTarget target, int position)
        {
            if (_waitingForSchemaAt == position) return;
            _waitingForSchemaAt = position;
            var token = _closed.Token;
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await SchemaCache.Instance.GetAsync(target, TimeSpan.FromSeconds(15), token);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    // Only with a schema in hand: after a failed load GetAsync returns at once, and retrying then would loop.
                    if (_disposed || token.IsCancellationRequested || _view.IsClosed) return;
                    if (_view.Caret.Position.BufferPosition.Position == position && SchemaCache.Instance.TryGetLoaded(target) != null) Evaluate();
                }
                finally
                {
                    _waitingForSchemaAt = -1;   // also when the wait throws, so a later retry at the same caret is not blocked
                }
            }).FileAndForget("SqlAssist/ParameterHintWaitForSchema");
        }

        private void Show(ITextSnapshot snapshot, SnapshotSpan anchor, ParameterHint hint, string key, ITrackingPoint paren)
        {
            if (_presenter == null)
            {
                // Does not follow the mouse, and survives typing and caret moves: this class decides when it goes, and moves it itself.
                var parameters = new ToolTipParameters(trackMouse: false, ignoreBufferChange: true, keepOpenFunc: null,
                    ignoreCaretPositionChange: true, dismissWhenOffscreen: true);   // scrolled away: gone, and back on the next caret move
                var presenter = _toolTips.CreatePresenter(_view, parameters);
                presenter.Dismissed += (s, e) =>
                {
                    if (!ReferenceEquals(s, _presenter)) return;
                    _presenter = null;
                    _shownKey = null;
                };
                _presenter = presenter;
            }
            _presenter.StartOrUpdate(snapshot.CreateTrackingSpan(anchor, SpanTrackingMode.EdgeInclusive), new object[] { Content(hint) });
            _shownKey = key;
            _dismissedPoint = paren;
        }

        private static object Content(ParameterHint hint)
        {
            var runs = new List<ClassifiedTextRun>();
            foreach (var piece in hint.Line)
                runs.Add(new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, piece.Text,
                    piece.IsActive ? ClassifiedTextRunStyle.Bold : ClassifiedTextRunStyle.Plain));
            var lines = new List<object> { new ClassifiedTextElement(runs) };
            if (!string.IsNullOrWhiteSpace(hint.ParameterNote)) lines.Add(PlainLine(hint.ParameterNote));
            if (!string.IsNullOrWhiteSpace(hint.Summary))
                foreach (string line in hint.Summary.Split('\n')) lines.Add(PlainLine(line.Trim()));
            return lines.Count == 1 ? lines[0] : new ContainerElement(ContainerElementStyle.Stacked, lines);
        }

        private static ClassifiedTextElement PlainLine(string text) =>
            new ClassifiedTextElement(new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, text));

        private void Hide()
        {
            var presenter = _presenter;
            _presenter = null;
            _shownKey = null;
            _dismissedPoint = null;
            try
            {
                presenter?.Dismiss();
            }
            catch (Exception ex)
            {
                Log.Error("Parameter hints: dismissing", ex);   // the presenter is already forgotten: nothing else to undo
            }
        }
    }
}
