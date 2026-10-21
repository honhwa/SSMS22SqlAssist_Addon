using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SqlAssist.Core.Risk;

namespace SqlAssist.Risk
{
    /// <summary>What the Execute guard shows when the script about to run has findings: where it will run, what is risky, and the choice.
    /// Cancel is the default (Enter, Esc and closing the window all cancel); running anyway takes a click on its own button.</summary>
    internal sealed class ExecuteWarningWindow : Window
    {
        /// <summary>One row of the list; <see cref="Line"/> counts from the top of the query window, not of the selection.</summary>
        public sealed class Item
        {
            public string Rule { get; set; }
            public int Line { get; set; }
            public string Statement { get; set; }
            public string Message { get; set; }
        }

        public ExecuteWarningWindow(string server, string database, bool isSelection, IReadOnlyList<Item> findings, IReadOnlyList<string> notes)
        {
            Title = "SqlAssist: คำสั่งเสี่ยงก่อน Execute";
            Width = 760;
            Height = 460;
            MinWidth = 520;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            var root = new DockPanel { Margin = new Thickness(14) };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var run = new Button { Content = "รันต่อ", Width = 100, Height = 30, Margin = new Thickness(0, 0, 10, 0) };
            run.Click += (s, e) => DialogResult = true;
            var cancel = new Button { Content = "ยกเลิก", Width = 100, Height = 30, IsDefault = true, IsCancel = true };
            cancel.Click += (s, e) => DialogResult = false;
            buttons.Children.Add(run);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var head = new StackPanel();
            head.Children.Add(new TextBlock { Text = "กำลังจะรัน " + (isSelection ? "ส่วนที่เลือก" : "ทั้งหน้า query") + " ซึ่งมีคำสั่งเสี่ยง " + findings.Count + " รายการ", FontWeight = FontWeights.SemiBold, FontSize = 15 });
            var where = new TextBlock { Margin = new Thickness(0, 6, 0, 8) };
            where.Inlines.Add(new System.Windows.Documents.Run("Server: ") { Foreground = Brushes.Gray });
            where.Inlines.Add(new System.Windows.Documents.Run(server) { FontWeight = FontWeights.SemiBold });
            where.Inlines.Add(new System.Windows.Documents.Run("    Database: ") { Foreground = Brushes.Gray });
            where.Inlines.Add(new System.Windows.Documents.Run(database) { FontWeight = FontWeights.SemiBold });
            head.Children.Add(where);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var foot = new StackPanel();
            if (notes.Count > 0)
                foot.Children.Add(new TextBlock { Text = string.Join("\r\n", notes.Select(n => "⚠ " + n)), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange, Margin = new Thickness(0, 6, 0, 0) });
            foot.Children.Add(new TextBlock { Text = RiskAnalyzer.Disclaimer, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
            DockPanel.SetDock(foot, Dock.Bottom);
            root.Children.Add(foot);

            var list = new ListView();
            var grid = new GridView();
            grid.Columns.Add(new GridViewColumn { Header = "กฎ", Width = 140, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Item.Rule)) });
            grid.Columns.Add(new GridViewColumn { Header = "บรรทัด", Width = 60, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Item.Line)) });
            grid.Columns.Add(new GridViewColumn { Header = "คำสั่ง", Width = 250, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Item.Statement)) });
            grid.Columns.Add(new GridViewColumn { Header = "เหตุผล", Width = 260, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Item.Message)) });
            list.View = grid;
            list.ItemsSource = findings;
            root.Children.Add(list);

            Content = root;
            Loaded += (s, e) => cancel.Focus();
        }
    }
}
