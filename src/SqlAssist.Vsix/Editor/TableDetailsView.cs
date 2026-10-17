using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using SqlAssist.Core.Schema;

namespace SqlAssist.Editor
{
    /// <summary>The table panel shared by the completion list (beside a selected table) and the hover tooltip:
    /// "dbo.Work_InitialApproval (Table)", one row per column — key icon, name, type, IDENTITY / NOT NULL — and the
    /// estimated row count. Types and flags use the editor's keyword colour, as in the code.</summary>
    internal static class TableDetailsView
    {
        private const double RowHeight = 20;

        /// <param name="textKey">Theme resource key for ordinary text (differs between a tool window and a tooltip).</param>
        /// <param name="borderKey">Theme resource key for the separator above the row count.</param>
        public static FrameworkElement Create(TableDetails d, Brush keyword, object textKey, object borderKey)
        {
            var panel = new StackPanel { Margin = new Thickness(6, 4, 8, 4) };
            TextElement.SetFontFamily(panel, new FontFamily("Segoe UI"));
            TextElement.SetFontSize(panel, 13);

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            header.Children.Add(Image(d.Kind == "View" ? KnownMonikers.View : KnownMonikers.Table, new Thickness(0, 0, 6, 0)));
            var title = Text(textKey, null, 14);
            title.Inlines.Add(new Run(d.Schema + "."));
            title.Inlines.Add(new Bold(new Run(d.Name)));
            title.Inlines.Add(new Run(" (" + d.Kind + ")"));
            header.Children.Add(title);
            panel.Children.Add(header);

            var grid = new Grid();
            for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < d.Columns.Count; i++)
            {
                var c = d.Columns[i];
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });

                var name = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
                var moniker = c.Key == ColumnKeyKind.PrimaryKey ? KnownMonikers.KeyColumn
                    : c.Key == ColumnKeyKind.ForeignKey ? KnownMonikers.ForeignKey
                    : KnownMonikers.Column;
                name.Children.Add(Image(moniker, new Thickness(0, 0, 6, 0)));
                name.Children.Add(Text(textKey, c.Name));
                Place(grid, name, i, 0);

                var type = Text(null, c.Type);
                type.Foreground = keyword;
                type.Margin = new Thickness(0, 0, 16, 0);
                Place(grid, type, i, 1);

                var flags = Text(null, c.Flags);
                flags.Foreground = keyword;
                Place(grid, flags, i, 2);
            }
            panel.Children.Add(grid);

            if (d.HiddenColumns > 0)
            {
                var more = Text(textKey, $"… and {d.HiddenColumns} more columns");
                more.Opacity = 0.7;
                more.Margin = new Thickness(22, 2, 0, 0);
                panel.Children.Add(more);
            }

            if (d.RowCount != null)
            {
                var count = Text(textKey, null);
                count.Margin = new Thickness(0, 4, 0, 0);
                count.Inlines.Add(new Run("Est. row count: "));
                count.Inlines.Add(new Bold(new Run(d.RowCount)));
                var separator = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 0), Child = count };
                separator.SetResourceReference(Border.BorderBrushProperty, borderKey);
                panel.Children.Add(separator);
            }
            return panel;
        }

        private static TextBlock Text(object textKey, string text, double size = 0)
        {
            var block = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            if (text != null) block.Text = text;
            if (size > 0) block.FontSize = size;
            if (textKey != null) block.SetResourceReference(TextBlock.ForegroundProperty, textKey);
            return block;
        }

        private static CrispImage Image(ImageMoniker moniker, Thickness margin) =>
            new CrispImage { Moniker = moniker, Width = 16, Height = 16, Margin = margin, VerticalAlignment = VerticalAlignment.Center };

        private static void Place(Grid grid, UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
    }
}
