using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using SqlAssist.Core.Assistant;

namespace SqlAssist.Assistant
{
    internal static class AssistantMarkdownView
    {
        public static StackPanel Create(string text, Action<string> status)
        {
            var panel = new StackPanel();
            foreach (var block in AssistantMarkdown.Parse(text))
            {
                if (block.IsCode)
                {
                    var buttons = new WrapPanel();
                    buttons.Children.Add(AssistantUi.Button("Copy", () => Copy(block.Text, status)));
                    if (block.IsSql) buttons.Children.Add(AssistantUi.Button("Insert into Editor", () =>
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        try { AssistantEditorBridge.Insert(block.Text); status("แทรก SQL แล้ว ใช้ Ctrl+Z เพื่อย้อนกลับ"); }
                        catch (Exception ex) { status(AssistantUi.Error(ex)); }
                    }));
                    panel.Children.Add(buttons);
                    panel.Children.Add(new TextBox
                    {
                        Text = block.Text, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12,
                        TextWrapping = TextWrapping.NoWrap, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 420, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8)
                    });
                }
                else
                {
                    foreach (string line in block.Text.Split('\n'))
                    {
                        var paragraph = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 3) };
                        string content = line;
                        var heading = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
                        if (heading.Success) { paragraph.FontSize = 19 - heading.Groups[1].Length; paragraph.FontWeight = FontWeights.SemiBold; content = heading.Groups[2].Value; }
                        else if (Regex.IsMatch(line, @"^\s*[-*+]\s+")) content = "• " + Regex.Replace(line, @"^\s*[-*+]\s+", "");
                        foreach (string part in Regex.Split(content, @"(\*\*[^*]+\*\*|`[^`]+`|\*[^*]+\*)"))
                        {
                            if (part.StartsWith("**", StringComparison.Ordinal) && part.EndsWith("**", StringComparison.Ordinal) && part.Length >= 4) paragraph.Inlines.Add(new Bold(new Run(part.Substring(2, part.Length - 4))));
                            else if (part.StartsWith("`", StringComparison.Ordinal) && part.EndsWith("`", StringComparison.Ordinal) && part.Length >= 2) paragraph.Inlines.Add(new Run(part.Substring(1, part.Length - 2)) { FontFamily = new FontFamily("Consolas") });
                            else if (part.StartsWith("*", StringComparison.Ordinal) && part.EndsWith("*", StringComparison.Ordinal) && part.Length >= 2) paragraph.Inlines.Add(new Italic(new Run(part.Substring(1, part.Length - 2))));
                            else paragraph.Inlines.Add(new Run(part));
                        }
                        panel.Children.Add(paragraph);
                    }
                }
            }
            return panel;
        }
        public static void Copy(string text, Action<string> status)
        {
            try { Clipboard.SetText(text); status("คัดลอกแล้ว"); }
            catch (Exception) { status("คัดลอกไม่ได้ คลิปบอร์ดอาจกำลังถูกใช้งาน กรุณาลองใหม่"); }
        }
    }
}
