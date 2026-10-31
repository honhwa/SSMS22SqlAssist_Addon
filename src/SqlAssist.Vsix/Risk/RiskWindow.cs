using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlAssist.Core.Risk;

namespace SqlAssist.Risk
{
    /// <summary>The list Check SQL Risks shows: what was found, where, and why. Clicking a row (or Enter) selects that statement in the
    /// query window it came from; the window stays open beside the editor. Each row follows its statement as the text is edited.</summary>
    internal sealed class RiskWindow : Window
    {
        private sealed class Row
        {
            public string Rule { get; set; }
            public string Position { get; set; }
            public string Statement { get; set; }
            public string Message { get; set; }
            public ITrackingSpan Where { get; set; }
        }

        private static RiskWindow _current;

        private readonly ListView _list = new ListView();
        private readonly TextBlock _summary = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _notes = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange, Margin = new Thickness(0, 6, 0, 0) };
        private IWpfTextView _view;

        private RiskWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Title = "SqlAssist：检查 SQL 风险";
            Width = 900;
            Height = 420;
            MinWidth = 560;
            MinHeight = 260;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            Content = BuildLayout();
            Closed += (s, e) => { if (_current == this) _current = null; };
        }

        /// <summary>Shows the findings, in the window already open if there is one.</summary>
        public static void ShowReport(IWpfTextView view, SnapshotSpan scope, bool isSelection, RiskReport report)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_current == null)
            {
                _current = new RiskWindow();
                DialogHost.SetOwner(_current);
                _current.Show();
            }
            _current.Fill(view, scope, isSelection, report);
            _current.Activate();
        }

        private UIElement BuildLayout()
        {
            var root = new DockPanel { Margin = new Thickness(12) };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var again = new Button { Content = "重新检查", Width = 110, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            again.Click += (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); RiskCheckCommand.Run(); };
            var close = new Button { Content = "关闭", Width = 90, Height = 28, IsCancel = true };
            close.Click += (s, e) => Close();
            buttons.Children.Add(again);
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var footer = new StackPanel();
            footer.Children.Add(_notes);
            footer.Children.Add(new TextBlock { Text = RiskAnalyzer.Disclaimer, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            DockPanel.SetDock(_summary, Dock.Top);
            root.Children.Add(_summary);

            var grid = new GridView();
            grid.Columns.Add(new GridViewColumn { Header = "规则", Width = 150, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Rule)) });
            grid.Columns.Add(new GridViewColumn { Header = "位置", Width = 130, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Position)) });
            grid.Columns.Add(new GridViewColumn { Header = "语句", Width = 280, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Statement)) });
            grid.Columns.Add(new GridViewColumn { Header = "原因", Width = 300, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Message)) });
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

        private void Fill(IWpfTextView view, SnapshotSpan scope, bool isSelection, RiskReport report)
        {
            _view = view;
            var snapshot = scope.Snapshot;
            var rows = new List<Row>();
            foreach (var f in report.Findings)
            {
                int position = scope.Start.Position + f.Offset;
                var line = snapshot.GetLineFromPosition(position);
                rows.Add(new Row
                {
                    Rule = RuleName(f.Rule),
                    Position = "第 " + (line.LineNumber + 1) + " 行，第 " + (position - line.Start.Position + 1),
                    Statement = f.Statement,
                    Message = f.Message,
                    Where = snapshot.CreateTrackingSpan(new Span(position, f.Length), SpanTrackingMode.EdgeExclusive),
                });
            }
            _list.ItemsSource = rows;
            string what = isSelection ? "所选内容" : "整个文档";
            _summary.Text = rows.Count == 0
                ? "未发现问题（" + what + "）—— 并不代表安全，请见下方说明"
                : "在" + what + "中发现 " + rows.Count + " 项 —— 点击行可跳转到该语句";
            _notes.Text = string.Join("\r\n", report.Notes.Select(n => "⚠ " + n));
            _notes.Visibility = report.Notes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Jump(Row row)
        {
            if (row == null) return;
            try
            {
                if (_view == null || _view.IsClosed)
                {
                    MessageBox.Show(this, "被检查的查询窗口已关闭", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var span = row.Where.GetSpan(_view.TextSnapshot);
                _view.Selection.Select(span, false);
                _view.Caret.MoveTo(span.Start);
                _view.ViewScroller.EnsureSpanVisible(span, EnsureSpanVisibleOptions.AlwaysCenter);
                FocusEditor(_view);
            }
            catch (Exception ex)
            {
                Log.Error("Risk check: jump", ex);
            }
        }

        /// <summary>Brings the editor's window forward and gives it the keyboard, so the selection shows and typing goes there.</summary>
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

        internal static string RuleName(RiskRule rule)
        {
            switch (rule)
            {
                case RiskRule.UpdateWithoutWhere: return "UPDATE 无 WHERE";
                case RiskRule.DeleteWithoutWhere: return "DELETE 无 WHERE";
                case RiskRule.TruncateTable: return "TRUNCATE TABLE";
                default: return "DROP TABLE";
            }
        }
    }
}
