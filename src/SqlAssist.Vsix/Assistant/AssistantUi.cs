using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.VisualStudio.PlatformUI;
using SqlAssist.Core.Assistant;

namespace SqlAssist.Assistant
{
    internal static class AssistantUi
    {
        public static void Theme(FrameworkElement element)
        {
            var templates = new ResourceDictionary { Source = new Uri("/SqlAssist;component/Assistant/AssistantTheme.xaml", UriKind.Relative) };
            element.Resources.MergedDictionaries.Add(templates);
            element.SetResourceReference(element is Control ? Control.BackgroundProperty : Panel.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            element.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(PasswordBox), typeof(ComboBox), typeof(TabControl), typeof(TabItem), typeof(CheckBox) })
            {
                var style = new Style(type);
                style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowBackgroundBrushKey)));
                style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowTextBrushKey)));
                style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowBorderBrushKey)));
                if (type == typeof(ComboBox)) style.Setters.Add(new Setter(Control.TemplateProperty, templates["AssistantComboBoxTemplate"]));
                if (type == typeof(TabItem)) style.Setters.Add(new Setter(Control.TemplateProperty, templates["AssistantTabItemTemplate"]));
                element.Resources[type] = style;
            }
        }
        public static Button Button(string text, Action action)
        {
            var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 3, 6, 3), MinHeight = 28 };
            button.Click += (s, e) => action();
            return button;
        }
        public static TextBlock Note(string text) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8) };
        public static Button IconButton(string name, string geometry, Action action)
        {
            var icon = new Path { Data = Geometry.Parse(geometry), StrokeThickness = 1.5, Width = 18, Height = 18, Stretch = Stretch.Uniform,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            icon.SetResourceReference(Shape.StrokeProperty, EnvironmentColors.ToolWindowTextBrushKey);
            var button = Button("", action);
            button.Content = icon; button.Width = 30; button.Height = 30;
            button.Padding = new Thickness(6); button.Margin = new Thickness(2, 0, 0, 0);
            button.ToolTip = name;
            AutomationProperties.SetName(button, name);
            button.SetResourceReference(FrameworkElement.StyleProperty, "AssistantIconButtonStyle");
            return button;
        }
        public static string Error(Exception error) => error is AiException || error is ArgumentException ||
            (error is InvalidOperationException && error.Message == "เข้าถึง Windows Credential Manager ไม่สำเร็จ") ? error.Message : "ทำรายการไม่สำเร็จ ตรวจการตั้งค่าและลองใหม่";
    }
}
