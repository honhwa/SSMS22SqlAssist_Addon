using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;

namespace SqlAssist.Settings
{
    /// <summary>Shared native settings appearance; host resources stay dynamic when SSMS changes theme.</summary>
    internal static class SettingsAppearance
    {
        public static void Apply(Window window)
        {
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/SqlAssist;component/Settings/SettingsTheme.xaml", UriKind.Relative) });
            window.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            window.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        }

        public static TextBlock Heading(string page)
        {
            var text = new TextBlock { FontSize = 22, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 20) };
            text.Inlines.Add(new Run("การตั้งค่า › SqlAssist › ") { Foreground = Brushes.Gray });
            text.Inlines.Add(new Run(page) { FontWeight = FontWeights.SemiBold });
            return text;
        }

        public static UIElement Page(string title, UIElement content)
        {
            var page = new DockPanel { Margin = new Thickness(4, 18, 4, 0) };
            var heading = Heading(title);
            DockPanel.SetDock(heading, Dock.Top); page.Children.Add(heading);
            page.Children.Add(content);
            return page;
        }

        public static void NormalizeDescriptions(DependencyObject root)
        {
            if (root is TextBlock text && text.Foreground == Brushes.Gray)
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                text.Opacity = 0.68;
            }
            foreach (object child in LogicalTreeHelper.GetChildren(root))
                if (child is DependencyObject element) NormalizeDescriptions(element);
        }
    }
}
