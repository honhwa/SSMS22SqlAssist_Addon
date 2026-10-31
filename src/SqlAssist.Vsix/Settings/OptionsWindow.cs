using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using SqlAssist.Core.Formatting;
using SqlAssist.Core.Risk;
using SqlAssist.Core.Settings;
using SqlAssist.Risk;

namespace SqlAssist.Settings
{
    /// <summary>Tools › SqlAssist › Options…: how Format SQL lays text out (with a sample that changes as the choices do) and which
    /// risks Check SQL Risks looks for. A plain WPF Window (see <see cref="DialogHost"/>), like the Snippets Manager.</summary>
    internal sealed class OptionsWindow : Window
    {
        private const string Sample =
            "select a.id,a.name,count(*) as cnt from dbo.Orders a inner join dbo.Customers c on c.id=a.cid and c.active=1 " +
            "where a.status in (1,2,3) and (a.x is null or a.y>5) -- active only\n" +
            "group by a.id,a.name order by a.id desc";

        private readonly SqlAssistSettings _settings;
        private readonly ComboBox _keywordCase = new ComboBox { Width = 260, Height = 26 };
        private readonly ComboBox _indent = new ComboBox { Width = 80, Height = 26 };
        private readonly RadioButton _leading = new RadioButton { Content = "行首（  , b）", GroupName = "comma", Margin = new Thickness(0, 0, 18, 0) };
        private readonly RadioButton _trailing = new RadioButton { Content = "行尾（a,  ）", GroupName = "comma" };
        private readonly TextBox _preview = new TextBox
        {
            IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
        };
        private readonly CheckBox _update = new CheckBox { Content = "没有 WHERE 的 UPDATE", Margin = new Thickness(0, 0, 0, 6) };
        private readonly CheckBox _delete = new CheckBox { Content = "没有 WHERE 的 DELETE", Margin = new Thickness(0, 0, 0, 6) };
        private readonly CheckBox _truncate = new CheckBox { Content = "TRUNCATE TABLE", Margin = new Thickness(0, 0, 0, 6) };
        private readonly CheckBox _drop = new CheckBox { Content = "DROP TABLE", Margin = new Thickness(0, 0, 0, 6) };
        private readonly CheckBox _skipTemp = new CheckBox { Content = "跳过临时表 (#...) 和表变量 (@...)", Margin = new Thickness(0, 6, 0, 0) };
        private readonly CheckBox _highlightInsert = new CheckBox { Content = "高亮 INSERT … VALUES 中位置对应的列和值", FontWeight = FontWeights.SemiBold };
        private readonly CheckBox _warn = new CheckBox { Content = "执行前提示（实验性）", FontWeight = FontWeights.SemiBold };
        private readonly TextBlock _guardStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(18, 6, 0, 0) };
        private bool _loading = true;

        public OptionsWindow()
        {
            _settings = SettingsProvider.Load();
            Title = "SqlAssist：选项";
            Width = 700;
            Height = 640;
            MinWidth = 560;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            _keywordCase.Items.Add("全部大写 (SELECT)");
            _keywordCase.Items.Add("全部小写 (select)");
            _keywordCase.Items.Add("保持原样（不修改）");
            for (int size = FormatOptions.MinIndentSize; size <= FormatOptions.MaxIndentSize; size++) _indent.Items.Add(size);

            _keywordCase.SelectedIndex = (int)_settings.Format.KeywordCase;
            _indent.SelectedItem = _settings.Format.IndentSize;
            _leading.IsChecked = _settings.Format.CommaStyle == CommaStyle.Leading;
            _trailing.IsChecked = _settings.Format.CommaStyle == CommaStyle.Trailing;
            _update.IsChecked = _settings.Risk.UpdateWithoutWhere;
            _delete.IsChecked = _settings.Risk.DeleteWithoutWhere;
            _truncate.IsChecked = _settings.Risk.TruncateTable;
            _drop.IsChecked = _settings.Risk.DropTable;
            _skipTemp.IsChecked = _settings.Risk.SkipTempTables;
            _warn.IsChecked = _settings.Risk.WarnBeforeExecute;
            _highlightInsert.IsChecked = _settings.Editor.HighlightInsertColumns;
            _guardStatus.Text = "当前状态：" + ExecuteGuard.Status;

            // Building the layout needs no UI-thread check: it only creates WPF controls, which is also what lets this window be
            // opened outside SSMS to look at it. Everything that touches SSMS (Save) checks for itself.
#pragma warning disable VSTHRD010
            Content = BuildLayout();
#pragma warning restore VSTHRD010
            _keywordCase.SelectionChanged += (s, e) => Changed();
            _indent.SelectionChanged += (s, e) => Changed();
            _leading.Click += (s, e) => Changed();
            _trailing.Click += (s, e) => Changed();
            _loading = false;
            Changed();
        }

        private UIElement BuildLayout()
        {
            var root = new DockPanel { Margin = new Thickness(14) };

            var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 12, 0, 0) };
            // Opens the log in the default editor for .log files, not inside SSMS: this window is modal, so a document opened in
            // SSMS would land behind it.
            var openLog = new Button
            {
                Content = "打开日志文件", Height = 28, Padding = new Thickness(14, 0, 14, 0), ToolTip = Log.FilePath,
            };
            openLog.Click += (s, e) => OpenLog();
            DockPanel.SetDock(openLog, Dock.Left);
            buttons.Children.Add(openLog);
            var cancel = new Button { Content = "取消", Width = 90, Height = 28, IsCancel = true };
            cancel.Click += (s, e) => DialogResult = false;
            DockPanel.SetDock(cancel, Dock.Right);
            buttons.Children.Add(cancel);
            var save = new Button { Content = "保存", Width = 90, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            save.Click += (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); Save(); };
            DockPanel.SetDock(save, Dock.Right);
            buttons.Children.Add(save);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var location = new TextBlock
            {
                Text = "保存位置：" + SettingsProvider.FilePath, Foreground = Brushes.Gray, FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 8, 0, 0),
            };
            DockPanel.SetDock(location, Dock.Bottom);
            root.Children.Add(location);

            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = "格式化 SQL", Content = FormatTab() });
            tabs.Items.Add(new TabItem { Header = "风险检查", Content = RiskTab() });
            tabs.Items.Add(new TabItem { Header = "编辑器", Content = EditorTab() });
            root.Children.Add(tabs);
            return root;
        }

        private UIElement FormatTab()
        {
            var root = new DockPanel { Margin = new Thickness(12) };
            var form = new StackPanel();
            form.Children.Add(new TextBlock { Text = "工具 › SqlAssist › 格式化 SQL，或在查询编辑器中右键 › Format SQL (SqlAssist)，或按 Ctrl+K, Ctrl+Shift+F", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
            form.Children.Add(Row("关键字大小写", _keywordCase));
            form.Children.Add(Row("缩进（每级空格数）", _indent));
            var comma = new StackPanel { Orientation = Orientation.Horizontal };
            comma.Children.Add(_leading);
            comma.Children.Add(_trailing);
            form.Children.Add(Row("列表中逗号 , 的位置", comma));
            form.Children.Add(new TextBlock
            {
                Text = "可改变大小写的是 T-SQL 保留字，以及位置明确表明不是标识符的函数名 / 数据类型名 —— 表名、列名、别名、'...' 中的文本和 [...] 中的名称绝不会被修改",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 6, 0, 10),
            });
            form.Children.Add(new TextBlock { Text = "示例", Margin = new Thickness(0, 0, 0, 3) });
            DockPanel.SetDock(form, Dock.Top);
            root.Children.Add(form);
            root.Children.Add(_preview);
            return root;
        }

        private UIElement RiskTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock { Text = "工具 › SqlAssist › 检查 SQL 风险 会检查所选内容（或整个查询窗口）中的以下问题", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(_update);
            panel.Children.Add(_delete);
            panel.Children.Add(_truncate);
            panel.Children.Add(_drop);
            panel.Children.Add(_skipTemp);
            panel.Children.Add(new TextBlock
            {
                Text = RiskAnalyzer.Disclaimer, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 10, 0, 0),
            });
            panel.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 14) });
            panel.Children.Add(_warn);
            panel.Children.Add(new TextBlock
            {
                Text = "启用后，按下 Execute 时，将要运行的脚本（所选内容或整个查询窗口）若命中上述规则的风险项，会弹出窗口显示服务器、数据库和风险语句。" +
                       "默认按钮是 \"取消\"（按 Enter 或 Esc 也会取消），必须手动点 \"仍然执行\" 才会运行；取消查询不受影响。\n\n" +
                       "仍属实验性：仅在 SSMS 的命令层拦截（F5、Ctrl+E、Execute 按钮、查询菜单），尚未验证 SSMS 中所有执行查询的入口，因此不能保证没有风险语句被漏过。" +
                       "该选项在保存后生效",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(18, 4, 0, 0),
            });
            panel.Children.Add(_guardStatus);
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private UIElement EditorTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(_highlightInsert);
            panel.Children.Add(new TextBlock
            {
                Text = "当光标位于 VALUES (…) 中的某个值时，会为 INSERT (…) 列表中同位置的列名加上底纹，并在该值周围显示蓝色边框。" +
                       "当光标位于某个列名时，会为 VALUES 每一行中同位置的值加上底纹\n\n" +
                       "按 SQL 文本中的位置顺序配对（不连接数据库），仅适用于指定列名并使用 VALUES 的 INSERT，不包括 INSERT … SELECT / EXEC。" +
                       "如果列数与值数不相等，或 SQL 尚未输入完整，则只显示明确的配对\n\n" +
                       "该选项在保存并移动光标后生效",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(18, 6, 0, 0),
            });
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private static UIElement Row(string label, UIElement control)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var text = new TextBlock { Text = label, Width = 230, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(text, Dock.Left);
            row.Children.Add(text);
            var holder = new StackPanel { Orientation = Orientation.Horizontal };
            holder.Children.Add(control);
            row.Children.Add(holder);
            return row;
        }

        private void OpenLog()
        {
            try
            {
                if (!File.Exists(Log.FilePath))
                {
                    Directory.CreateDirectory(Log.Directory);
                    File.WriteAllText(Log.FilePath, "");
                }
                try
                {
                    Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
                }
                catch (Win32Exception)
                {
                    // No program is associated with .log files.
                    Process.Start("notepad.exe", "\"" + Log.FilePath + "\"");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Open log", ex);
                MessageBox.Show(this, "无法打开日志文件：\n" + ex.Message + "\n\n文件位于 " + Log.FilePath, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private FormatOptions Chosen() => new FormatOptions
        {
            KeywordCase = (KeywordCase)Math.Max(0, _keywordCase.SelectedIndex),
            IndentSize = _indent.SelectedItem is int size ? size : 4,
            CommaStyle = _trailing.IsChecked == true ? CommaStyle.Trailing : CommaStyle.Leading,
            Newline = "\r\n",
        };

        private void Changed()
        {
            if (_loading) return;
            var result = SqlFormatter.Format(Sample, Chosen());
            _preview.Text = result.IsSuccess ? result.Text : result.Error;
        }

        private void Save()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var chosen = Chosen();
                _settings.Format.KeywordCase = chosen.KeywordCase;
                _settings.Format.IndentSize = chosen.IndentSize;
                _settings.Format.CommaStyle = chosen.CommaStyle;
                _settings.Risk.UpdateWithoutWhere = _update.IsChecked == true;
                _settings.Risk.DeleteWithoutWhere = _delete.IsChecked == true;
                _settings.Risk.TruncateTable = _truncate.IsChecked == true;
                _settings.Risk.DropTable = _drop.IsChecked == true;
                _settings.Risk.SkipTempTables = _skipTemp.IsChecked == true;
                _settings.Risk.WarnBeforeExecute = _warn.IsChecked == true;
                _settings.Editor.HighlightInsertColumns = _highlightInsert.IsChecked == true;
                SettingsProvider.Save(_settings);

                ExecuteGuard.Apply(_settings.Risk);
                if (_settings.Risk.WarnBeforeExecute && !ExecuteGuard.IsHooked)
                    MessageBox.Show(this, "已保存，但执行前提示尚未生效：\n\n" + ExecuteGuard.Status, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                Log.Error("Saving settings", ex);
                MessageBox.Show(this, "保存失败：\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
