using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlAssist.Core.Ai;

namespace SqlAssist.Ai
{
    /// <summary>What Explain SQL shows: the explanation, then the findings. Clicking a finding (or Enter) moves the caret to its line in
    /// the query window it came from; each row follows its line as the text is edited after the request went out. One window at a time,
    /// like Check SQL Risks; a new request replaces (and cancels) the one shown.</summary>
    internal sealed class ExplainWindow : Window
    {
        public const string Disclaimer = "ข้อสังเกตมาจากการอ่านข้อความ SQL เท่านั้น ไม่ได้ดู execution plan หรือข้อมูลจริง ให้ตรวจสอบก่อนนำไปใช้";

        private sealed class Row
        {
            public string Severity { get; set; }
            public string Position { get; set; }
            public string Title { get; set; }
            public string Detail { get; set; }
            public ITrackingSpan Where { get; set; }
        }

        private static ExplainWindow _current;

        private readonly TextBox _summary = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, MaxHeight = 220, Margin = new Thickness(0, 0, 0, 8),
        };
        private readonly TextBlock _status = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        private readonly TextBlock _sent = new TextBlock { Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        private readonly ListView _list = new ListView();
        private readonly Button _cancel = new Button { Content = "ยกเลิก", Width = 90, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        private readonly Button _showSent = new Button { Content = "ดูสิ่งที่ส่ง", Width = 100, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        private IWpfTextView _view;
        private ExplainRequest _request;
        private string _endpoint;
        private CancellationTokenSource _cts;

        public ExplainWindow()
        {
            Title = "SqlAssist: Explain SQL (AI)";
            Width = 900;
            Height = 560;
            MinWidth = 560;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            // Only creates WPF controls (which also lets the window be rendered outside SSMS); handlers that touch SSMS check for themselves.
#pragma warning disable VSTHRD010
            Content = BuildLayout();
#pragma warning restore VSTHRD010
            Closed += (s, e) =>
            {
                if (_current == this) _current = null;
                CancelPending();
            };
        }

        /// <summary>Sends the request and shows the answer when it comes, in the window already open if there is one.</summary>
        public static void Start(IWpfTextView view, SnapshotSpan scope, bool isSelection, ExplainRequest request, string endpoint, string apiKey)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_current == null)
            {
                // Remembered only once it is on screen, so a failed Show does not leave an invisible window behind.
                var created = new ExplainWindow();
                DialogHost.SetOwner(created);
                created.Show();
                _current = created;
            }
            var window = _current;
            window.CancelPending();
            var cts = new CancellationTokenSource();
            window._cts = cts;
            window.ShowPending(view, request, endpoint, isSelection);
            window.Activate();

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                ExplainResult result;
                try
                {
                    var (status, body) = await AiClient.SendAsync(endpoint, apiKey, request.Body, cts.Token).ConfigureAwait(false);
                    result = ExplainResponse.Parse(status, body, request.SentLineCount);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error("Explain SQL", ex);
                    result = new ExplainResult { Problem = "Explain SQL ไม่สำเร็จ: " + ex.GetType().Name };
                }
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                try
                {
                    if (cts.IsCancellationRequested || window._cts != cts) return;   // replaced by a newer request, or closed
                    window.Fill(scope, result);
                }
                catch (Exception ex)
                {
                    Log.Error("Explain SQL: show result", ex);
                }
            }).FileAndForget("SqlAssist/ExplainSql");
        }

        private UIElement BuildLayout()
        {
            var root = new DockPanel { Margin = new Thickness(12) };

            var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 10, 0, 0) };
            var close = new Button { Content = "ปิด", Width = 90, Height = 28, IsCancel = true };
            close.Click += (s, e) => Close();
            DockPanel.SetDock(close, Dock.Right);
            buttons.Children.Add(close);
            _cancel.Click += (s, e) =>
            {
                CancelPending();
                _status.Text = "ยกเลิกแล้ว";
                _cancel.Visibility = Visibility.Collapsed;
            };
            DockPanel.SetDock(_cancel, Dock.Right);
            buttons.Children.Add(_cancel);
            _showSent.Click += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try { if (_request != null) AiConsentWindow.ShowSent(_request, _endpoint); }
                catch (Exception ex) { Log.Error("Explain SQL: show what was sent", ex); }
            };
            DockPanel.SetDock(_showSent, Dock.Right);
            buttons.Children.Add(_showSent);
            DockPanel.SetDock(_sent, Dock.Left);
            buttons.Children.Add(_sent);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var footer = new TextBlock { Text = Disclaimer, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            DockPanel.SetDock(_status, Dock.Top);
            root.Children.Add(_status);
            DockPanel.SetDock(_summary, Dock.Top);
            root.Children.Add(_summary);

            var grid = new GridView();
            grid.Columns.Add(new GridViewColumn { Header = "ระดับ", Width = 80, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Severity)) });
            grid.Columns.Add(new GridViewColumn { Header = "บรรทัด", Width = 80, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Position)) });
            grid.Columns.Add(new GridViewColumn { Header = "หัวข้อ", Width = 240, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Title)) });
            grid.Columns.Add(new GridViewColumn { Header = "รายละเอียด", Width = 440, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Detail)) });
            _list.View = grid;
            _list.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (ItemsControl.ContainerFromElement(_list, e.OriginalSource as DependencyObject) is ListViewItem item) Jump(item.Content as Row);
            };
            _list.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) Jump(_list.SelectedItem as Row);
            };
            root.Children.Add(_list);
            return root;
        }

        /// <summary>Cancel() runs HttpClient's registered callbacks inline and can throw; never from a WPF handler into SSMS.</summary>
        private void CancelPending()
        {
            try { _cts?.Cancel(); }
            catch (Exception ex) { Log.Error("Explain SQL: cancel", ex); }
        }

        private void ShowPending(IWpfTextView view, ExplainRequest request, string endpoint, bool isSelection)
        {
            _view = view;
            _request = request;
            _endpoint = endpoint;
            _status.Text = "กำลังรอคำตอบจากบริการ AI… (" + (isSelection ? "ส่วนที่เลือก" : "ทั้งเอกสาร") + ")";
            _summary.Text = "";
            _summary.Visibility = Visibility.Collapsed;
            _list.ItemsSource = null;
            _cancel.Visibility = Visibility.Visible;
            _sent.Text = request.TableCount == 0
                ? "ไม่มี schema ประกอบ (ยังไม่ได้โหลด หรือไม่พบตารางที่ SQL อ้างถึง)"
                : "ส่ง schema ของ " + request.TableCount + " ตาราง" + (request.SchemaTruncated ? " (ตัดที่ " + SchemaDigest.DefaultMaxTables + " ตาราง)" : "");
        }

        /// <summary>Public for rendering outside SSMS; <paramref name="scope"/> is the text that was sent, in the snapshot it was sent from.</summary>
        public void Fill(SnapshotSpan scope, ExplainResult result)
        {
            _cancel.Visibility = Visibility.Collapsed;
            if (result.Problem != null)
            {
                _status.Text = result.Problem;
                _summary.Visibility = Visibility.Collapsed;
                _list.ItemsSource = null;
                return;
            }
            _summary.Text = result.Summary;
            _summary.Visibility = Visibility.Visible;

            var snapshot = scope.Snapshot;
            int firstLine = snapshot.GetLineNumberFromPosition(scope.Start.Position);
            var rows = new List<Row>();
            foreach (var f in result.Findings)
            {
                ITrackingSpan where = null;
                int bufferLine = firstLine + f.Line - 1;
                if (f.Line > 0 && bufferLine < snapshot.LineCount)
                    where = snapshot.CreateTrackingSpan(snapshot.GetLineFromLineNumber(bufferLine).Extent, SpanTrackingMode.EdgeExclusive);
                rows.Add(new Row
                {
                    Severity = f.Severity == FindingSeverity.Warning ? "⚠ ควรแก้" : "ข้อสังเกต",
                    Position = where == null ? "—" : (bufferLine + 1).ToString(),
                    Title = f.Title,
                    Detail = f.Detail,
                    Where = where,
                });
            }
            _list.ItemsSource = rows;
            _status.Text = rows.Count == 0 ? "ไม่พบข้อสังเกต" : "ข้อสังเกต " + rows.Count + " รายการ — คลิกแถวเพื่อไปที่บรรทัดนั้น";
        }

        private void Jump(Row row)
        {
            if (row?.Where == null) return;
            try
            {
                if (_view == null || _view.IsClosed)
                {
                    MessageBox.Show(this, "หน้า query ที่ส่งไปถูกปิดไปแล้ว", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var span = row.Where.GetSpan(_view.TextSnapshot);
                _view.Selection.Clear();
                _view.Caret.MoveTo(span.Start);
                _view.ViewScroller.EnsureSpanVisible(span, EnsureSpanVisibleOptions.AlwaysCenter);
                FocusEditor(_view);
            }
            catch (Exception ex)
            {
                Log.Error("Explain SQL: jump", ex);
            }
        }

        private static void FocusEditor(IWpfTextView view)
        {
            if (PresentationSource.FromVisual(view.VisualElement) is HwndSource source)
            {
                IntPtr root = GetAncestor(source.Handle, GaRoot);
                if (root != IntPtr.Zero) SetForegroundWindow(root);
            }
            view.VisualElement.Focus();
            Keyboard.Focus(view.VisualElement);
        }

        private const uint GaRoot = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);
    }
}
