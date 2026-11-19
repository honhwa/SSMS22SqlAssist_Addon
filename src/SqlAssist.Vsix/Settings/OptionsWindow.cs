using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using SqlAssist.Core.Formatting;
using SqlAssist.Core.Results;
using SqlAssist.Core.Risk;
using SqlAssist.Core.Settings;
using SqlAssist.Core.Sql;
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
        private readonly ComboBox _keywordCase = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly ComboBox _indent = new ComboBox { Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly RadioButton _leading = new RadioButton { Content = "นำหน้าบรรทัด (, a)", GroupName = "comma", Margin = new Thickness(0, 0, 0, 8) };
        private readonly RadioButton _trailing = new RadioButton { Content = "ท้ายบรรทัด (a,)", GroupName = "comma" };
        private readonly RichTextBox _lineNumbers = new RichTextBox
        {
            IsReadOnly = true, IsHitTestVisible = false, Focusable = false, Width = 40,
            FontFamily = new FontFamily("Consolas"), FontSize = 13, BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 12, 8, 12), Opacity = 0.55,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        private readonly RichTextBox _preview = new RichTextBox
        {
            Name = "SqlFormatPreview", IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 13,
            BorderThickness = new Thickness(0), Padding = new Thickness(8, 12, 12, 12),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        // The preview is a fixed sample, so only its known SQL words need highlighting.
        private static readonly HashSet<string> PreviewKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "SELECT", "COUNT", "AS", "FROM", "INNER", "JOIN", "ON", "AND", "WHERE", "IN", "IS", "NULL", "OR", "GROUP", "BY", "ORDER", "DESC" };
        private readonly CheckBox _update = new CheckBox { Content = "UPDATE ที่ไม่มี WHERE", Margin = new Thickness(0, 0, 0, 6) };
        private readonly CheckBox _delete = new CheckBox { Content = "DELETE ที่ไม่มี WHERE", Margin = new Thickness(0, 0, 0, 6) };
        private readonly CheckBox _truncate = new CheckBox { Content = "TRUNCATE TABLE", Margin = new Thickness(0, 0, 0, 6) };
        private readonly CheckBox _drop = new CheckBox { Content = "DROP TABLE", Margin = new Thickness(0, 0, 0, 6) };
        private readonly CheckBox _skipTemp = new CheckBox { Content = "ข้ามตาราง temp (#...) และ table variable (@...)", Margin = new Thickness(0, 6, 0, 0) };
        private readonly CheckBox _highlightInsert = new CheckBox { Content = "ไฮไลต์คอลัมน์และค่าที่ตรงกันใน INSERT … VALUES / INSERT … SELECT", FontWeight = FontWeights.SemiBold };
        private readonly CheckBox _parameterHints = new CheckBox { Content = "แสดง Function Parameter Hints (รูปแบบการเรียกฟังก์ชันขณะพิมพ์)", FontWeight = FontWeights.SemiBold };
        private readonly CheckBox _learnFromUsage = new CheckBox { Content = "เรียงรายการแนะนำตามที่ใช้บ่อย", FontWeight = FontWeights.SemiBold };
        private readonly RadioButton _identityKeep = new RadioButton { Content = "เก็บค่าเดิมไว้ — ครอบด้วย SET IDENTITY_INSERT ... ON / OFF", GroupName = "identity", Margin = new Thickness(0, 0, 0, 6) };
        private readonly RadioButton _identitySkip = new RadioButton { Content = "ตัดคอลัมน์ IDENTITY ออก — ให้ตารางปลายทางออกเลขใหม่เอง", GroupName = "identity" };
        private readonly CheckBox _warn =new CheckBox { Content = "เตือนก่อนกด Execute (ทดลอง)", FontWeight = FontWeights.SemiBold };
        private readonly TextBlock _guardStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(18, 6, 0, 0) };
        private bool _loading = true;

        public OptionsWindow()
        {
            _settings = SettingsProvider.Load();
            Title = "SqlAssist: Options";
            Width = 1040;
            Height = 700;
            MinWidth = 560;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            SettingsAppearance.Apply(this);

            _keywordCase.Items.Add("ตัวพิมพ์ใหญ่ทั้งหมด (SELECT)");
            _keywordCase.Items.Add("ตัวพิมพ์เล็กทั้งหมด (select)");
            _keywordCase.Items.Add("ตามที่พิมพ์ไว้ (ไม่เปลี่ยน)");
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
            _parameterHints.IsChecked = _settings.Editor.ShowParameterHints;
            _learnFromUsage.IsChecked = _settings.Editor.LearnFromUsage;
            _identityKeep.IsChecked = _settings.Results.InsertIdentity == InsertIdentityMode.KeepValues;
            _identitySkip.IsChecked = _settings.Results.InsertIdentity == InsertIdentityMode.Skip;
            _guardStatus.Text = "สถานะตอนนี้: " + ExecuteGuard.Status;

            // Building the layout needs no UI-thread check: it only creates WPF controls, which is also what lets this window be
            // opened outside SSMS to look at it. Everything that touches SSMS (Save) checks for itself.
#pragma warning disable VSTHRD010
            Content = BuildLayout();
            SettingsAppearance.NormalizeDescriptions(this);
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
            var root = new DockPanel { Margin = new Thickness(20, 12, 20, 16) };

            var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            footer.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 10) });
            var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };
            // Opens the log in the default editor for .log files, not inside SSMS: this window is modal, so a document opened in
            // SSMS would land behind it.
            var openLog = new Button
            {
                Content = "เปิดไฟล์ Log", Height = 36, Padding = new Thickness(14, 0, 14, 0), ToolTip = Log.FilePath,
            };
            openLog.Click += (s, e) => OpenLog();
            DockPanel.SetDock(openLog, Dock.Left);
            buttons.Children.Add(openLog);
            var cancel = new Button { Content = "ยกเลิก", Width = 100, Height = 36, IsCancel = true };
            cancel.Click += (s, e) => DialogResult = false;
            DockPanel.SetDock(cancel, Dock.Right);
            buttons.Children.Add(cancel);
            var save = new Button { Content = "บันทึก", Width = 100, Height = 36, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            save.SetResourceReference(StyleProperty, "SettingsPrimaryButton");
            save.Click += (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); Save(); };
            DockPanel.SetDock(save, Dock.Right);
            buttons.Children.Add(save);

            var location = new TextBlock
            {
                Text = "เก็บไว้ที่ " + SettingsProvider.FilePath, Foreground = Brushes.Gray, FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = SettingsProvider.FilePath,
            };
            footer.Children.Add(location);
            footer.Children.Add(buttons);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = "จัดรูปแบบ SQL", Content = OptionsPage(FormatTab()) });
            tabs.Items.Add(new TabItem { Header = "ตรวจจุดเสี่ยง", Content = OptionsPage(RiskTab()) });
            tabs.Items.Add(new TabItem { Header = "Editor", Content = OptionsPage(EditorTab()) });
            tabs.Items.Add(new TabItem { Header = "Results", Content = OptionsPage(ResultsTab()) });
            var assistantPanel = new StackPanel { Margin = new Thickness(12) };
            assistantPanel.Children.Add(SqlAssist.Assistant.AssistantUi.Note("จัดการ OpenRouter และ Ollama พร้อมโมเดลและ API Key ที่เข้ารหัสใน Windows Credential Manager"));
            var openAssistantSettings = SqlAssist.Assistant.AssistantUi.Button("เปิด AI API & Keys…", () =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try
                {
                    DialogHost.ShowDialog(new SqlAssist.Assistant.AssistantSettingsWindow());
                    _settings.Assistant.ReadXml(SettingsProvider.Load().Assistant.ToXml());
                }
                catch (Exception) { SqlAssist.Results.ScriptDelivery.Show("เปิดหน้าจัดการ API/Key ไม่สำเร็จ", "SqlAssist: AI"); }
            });
            openAssistantSettings.HorizontalAlignment = HorizontalAlignment.Left;
            assistantPanel.Children.Add(openAssistantSettings);
            tabs.Items.Add(new TabItem { Header = "AI Assistant", Content = OptionsPage(new ScrollViewer { Content = assistantPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }) });
            root.Children.Add(tabs);
            return root;
        }

        private UIElement FormatTab()
        {
            var layout = new DockPanel();
            var usage = new WrapPanel { Margin = new Thickness(14, 10, 14, 10) };
            usage.Children.Add(new TextBlock { Text = "เรียกใช้: Tools › SqlAssist › Format SQL", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) });
            usage.Children.Add(new Border
            {
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4),
                Child = new TextBlock { Text = "Ctrl+K, Ctrl+Shift+F" },
            });
            ((Border)usage.Children[1]).SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            var help = new Border { CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 14, 0, 0), Child = usage };
            help.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.CommandBarMenuBackgroundGradientBrushKey);
            help.ToolTip = "หรือคลิกขวาใน query editor › Format SQL (SqlAssist)";
            DockPanel.SetDock(help, Dock.Bottom);
            layout.Children.Add(help);

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            var form = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
            form.Children.Add(Row("ตัวพิมพ์ของ keyword", _keywordCase));
            form.Children.Add(Row("ย่อหน้า (ช่องว่างต่อระดับ)", _indent));
            form.Children.Add(new Separator { Margin = new Thickness(0, 2, 0, 14) });
            var comma = new StackPanel();
            comma.Children.Add(_leading);
            comma.Children.Add(_trailing);
            form.Children.Add(Row("ตำแหน่งเครื่องหมายจุลภาค", comma));
            form.Children.Add(new Separator { Margin = new Thickness(0, 2, 0, 14) });
            form.Children.Add(new TextBlock
            {
                Text = "เปลี่ยนเฉพาะ keyword ชื่อฟังก์ชันและชนิดข้อมูล\nชื่อตาราง คอลัมน์ alias และข้อความยังคงเดิม",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 6, 0, 10),
            });
            columns.Children.Add(Section("รูปแบบและการเยื้อง", new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
            var editor = new Grid();
            editor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            editor.ColumnDefinitions.Add(new ColumnDefinition());
            _lineNumbers.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            _lineNumbers.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            editor.Children.Add(_lineNumbers);
            Grid.SetColumn(_preview, 1);
            _preview.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            _preview.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            _preview.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) =>
            {
                if (e.OriginalSource is ScrollViewer scroll) _lineNumbers.ScrollToVerticalOffset(scroll.VerticalOffset);
            }));
            editor.Children.Add(_preview);
            var previewSection = Section("ตัวอย่าง SQL", editor);
            Grid.SetColumn(previewSection, 2);
            columns.Children.Add(previewSection);
            layout.Children.Add(columns);
            return layout;
        }

        /// <summary>Options already identifies the current page in its tabs; avoid repeating a breadcrumb.</summary>
        private static UIElement OptionsPage(UIElement content) => new Border { Margin = new Thickness(0, 18, 0, 0), Child = content };

        /// <summary>Theme-aware section with a fixed heading and independently scrolling content.</summary>
        private static Border Section(string title, UIElement content)
        {
            var panel = new DockPanel();
            var heading = new Border { Padding = new Thickness(18, 12, 18, 12), BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap } };
            heading.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            DockPanel.SetDock(heading, Dock.Top);
            panel.Children.Add(heading);
            panel.Children.Add(content);
            var section = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = panel, ClipToBounds = true };
            section.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            return section;
        }

        private UIElement RiskTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock { Text = "Tools › SqlAssist › Check SQL Risks ตรวจเรื่องเหล่านี้ในส่วนที่เลือก (หรือทั้งหน้า query)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
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
                Text = "ถ้าเปิด เมื่อกด Execute แล้ว script ที่จะรัน (ส่วนที่เลือก หรือทั้งหน้า query) มีรายการเสี่ยงตามกฎด้านบน จะขึ้นหน้าต่างบอก Server, Database และคำสั่งที่เสี่ยง " +
                       "ปุ่มเริ่มต้นคือ \"ยกเลิก\" (กด Enter หรือ Esc ก็ยกเลิก) ต้องกด \"รันต่อ\" เองถึงจะรัน Cancel Query ไม่ถูกแตะต้อง\n\n" +
                       "ยังเป็นของทดลอง: ดักที่ระดับคำสั่งของ SSMS (F5, Ctrl+E, ปุ่ม Execute, เมนู Query) แต่ยังไม่ได้ยืนยันกับทุกช่องทางที่รัน query ใน SSMS จริง จึงไม่ใช่การรับประกันว่าจะไม่มีคำสั่งเสี่ยงหลุดไปรัน " +
                       "ผลของตัวเลือกนี้เริ่มหลังกดบันทึก",
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
                Text = "เมื่อ caret อยู่ที่ค่าใน VALUES (…) หรือที่รายการใน SELECT ของ INSERT … SELECT จะแรเงาชื่อคอลัมน์ในรายการ INSERT (…) ที่อยู่ตำแหน่งเดียวกัน พร้อมกรอบสีฟ้ารอบค่านั้น " +
                       "และเมื่อ caret อยู่ที่ชื่อคอลัมน์ จะแรเงาค่าตำแหน่งเดียวกัน (ทุกแถวของ VALUES หรือรายการเดียวของ SELECT)\n\n" +
                       "จับคู่ตามลำดับตำแหน่งจากข้อความ SQL (ไม่ต่อฐานข้อมูล) ใช้กับ INSERT ที่ระบุรายชื่อคอลัมน์ และใช้ VALUES หรือ SELECT ธรรมดา " +
                       "ไม่รวม INSERT … EXEC, UNION / EXCEPT / INTERSECT และ SELECT * (ไม่ขยาย * จึงจับคู่เฉพาะรายการก่อน *) " +
                       "ถ้าจำนวนคอลัมน์กับค่าไม่เท่ากัน หรือ SQL ยังพิมพ์ไม่ครบ จะแสดงเฉพาะคู่ที่ชัดเจน\n\n" +
                       "ผลของตัวเลือกนี้เริ่มหลังกดบันทึกและขยับ caret",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(18, 6, 0, 0),
            });
            panel.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 14) });
            panel.Children.Add(_parameterHints);
            panel.Children.Add(new TextBlock
            {
                Text = "เมื่อพิมพ์ ( หลังชื่อฟังก์ชัน หรือคลิก/เลื่อน caret เข้าไปในวงเล็บของฟังก์ชัน จะแสดงรูปแบบการเรียก เช่น SUBSTRING(expression, start, length) โดยทำตัวหนาที่ parameter ตรงกับตำแหน่ง caret " +
                       "และเปลี่ยนตามเมื่อพิมพ์ comma หรือเลื่อน caret กด Esc เพื่อปิด\n\n" +
                       "รองรับฟังก์ชัน built-in ที่ใช้บ่อย (SUBSTRING, REPLACE, CHARINDEX, LEFT, RIGHT, ISNULL, COALESCE, DATEADD, DATEDIFF, ROUND, CONCAT ฯลฯ) " +
                       "และ scalar / table-valued function ของผู้ใช้ที่ระบุ schema (เช่น dbo.fnTax(...)) จาก schema ที่โหลดไว้แล้ว ไม่ query ฐานข้อมูลเพิ่มทุกครั้งที่พิมพ์ " +
                       "ฟังก์ชันที่ยังไม่รู้จักหรือมี syntax พิเศษ (เช่น CAST ... AS ...) จะไม่แสดง hint และไม่รวม EXEC stored procedure\n\n" +
                       "ผลของตัวเลือกนี้เริ่มหลังกดบันทึกและขยับ caret",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(18, 6, 0, 0),
            });
            panel.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 14) });
            panel.Children.Add(_learnFromUsage);
            panel.Children.Add(new TextBlock
            {
                Text = "นับว่าเลือกรายการไหนจากรายการแนะนำบ่อยแค่ไหนและล่าสุดเมื่อไร (keyword, snippet, ตาราง, คอลัมน์, function) แล้วดันรายการที่ใช้บ่อยขึ้นมาเล็กน้อย " +
                       "เฉพาะเมื่อพิมพ์แล้วอย่างน้อย 1 ตัวอักษร และเฉพาะในกลุ่มที่ตรงกับคำที่พิมพ์พอ ๆ กัน: คำที่พิมพ์ครบยังขึ้นก่อนเสมอ ต้องเลือกอย่างน้อย 3 ครั้งถึงเริ่มมีผล และที่เลิกใช้จะค่อย ๆ ลดลง\n\n" +
                       "keyword และ snippet นับรวมทุกฐานข้อมูล ส่วนตาราง คอลัมน์ และ function นับแยกตาม server + database " +
                       "เก็บในเครื่องนี้เท่านั้นที่ " + UsageFilePath + " (มีชื่อตารางและคอลัมน์ ไม่ส่งไปที่ไหนและไม่เขียนลง log)",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(18, 6, 0, 0),
            });
            var clear = new Button { Content = "ล้างประวัติการใช้งาน", Height = 26, Padding = new Thickness(12, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(18, 8, 0, 0) };
            clear.Click += (s, e) => ClearUsage();
            panel.Children.Add(clear);
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private UIElement ResultsTab()
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = "Generate Script As › INSERT เมื่อคอลัมน์ที่เลือกมีคอลัมน์ IDENTITY", FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(_identityKeep);
            panel.Children.Add(_identitySkip);
            panel.Children.Add(new TextBlock
            {
                Text = "SQL Server ไม่รับค่าที่ระบุเองในคอลัมน์ IDENTITY เว้นแต่เปิด IDENTITY_INSERT ของตารางนั้นไว้\n\n" +
                       "• เก็บค่าเดิม: เหมาะกับการย้ายข้อมูลไปอีกฐานข้อมูลโดยให้เลข ID ตรงกัน script จะมี SET IDENTITY_INSERT ตาราง ON ก่อน INSERT และ OFF ตอนท้าย " +
                       "(ต้องมีสิทธิ์ ALTER บนตาราง และเปิดได้ทีละตารางต่อ session) ชื่อตารางใช้ชื่อเดียวกับใน INSERT ถ้าเป็น [Table] ให้แก้ทั้งสามที่\n" +
                       "• ตัดออก: เหมาะกับการ copy แถวเป็นข้อมูลใหม่ script จะมี comment บอกว่าตัดคอลัมน์ไหนออก\n\n" +
                       "รู้ว่าคอลัมน์ไหนเป็น IDENTITY จากข้อมูลของผลลัพธ์ที่ SSMS เก็บไว้ (ไม่ query ฐานข้อมูลเพิ่ม) คอลัมน์ที่เป็นผลจากการคำนวณ เช่น ID + 0 จะไม่ถูกมองเป็น IDENTITY " +
                       "INSERT #tmpTable ไม่ได้รับผลจากตัวเลือกนี้ เพราะตาราง temp ที่สร้างไม่มีคอลัมน์ IDENTITY\n\n" +
                       "ผลของตัวเลือกนี้เริ่มหลังกดบันทึก",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(18, 8, 0, 0),
            });
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private static UIElement Row(string label, UIElement control)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 20), HorizontalAlignment = HorizontalAlignment.Stretch };
            var text = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(text);
            row.Children.Add(control);
            return row;
        }

        private static string UsageFilePath => Editor.UsageStore.FilePath;

        private void ClearUsage()
        {
            if (MessageBox.Show(this, "ล้างประวัติว่าเลือกรายการแนะนำไหนบ่อย? ลำดับจะกลับไปเป็นแบบเดิมจนกว่าจะใช้งานใหม่ (ย้อนกลับไม่ได้)",
                    Title, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            try
            {
                Editor.UsageStore.Clear();
                MessageBox.Show(this, "ล้างประวัติแล้ว", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error("Clear usage history", ex);
                MessageBox.Show(this, "ล้างประวัติไม่สำเร็จ:\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
                MessageBox.Show(this, "เปิดไฟล์ log ไม่สำเร็จ:\n" + ex.Message + "\n\nไฟล์อยู่ที่ " + Log.FilePath, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
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
            var text = result.IsSuccess ? result.Text : result.Error;
            var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 21, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
            int position = 0;
            foreach (var token in SqlTokenizer.Tokenize(text))
            {
                if (token.Start > position) paragraph.Inlines.Add(new Run(text.Substring(position, token.Start - position)));
                var run = new Run(token.Text);
                if (token.Kind == TokenKind.Word && PreviewKeywords.Contains(token.Text))
                {
                    run.SetResourceReference(TextElement.ForegroundProperty, SystemColors.HighlightBrushKey);
                    run.FontWeight = FontWeights.SemiBold;
                }
                else if (token.Kind == TokenKind.Comment) run.SetResourceReference(TextElement.ForegroundProperty, SystemColors.GrayTextBrushKey);
                paragraph.Inlines.Add(run);
                position = token.End;
            }
            if (position < text.Length) paragraph.Inlines.Add(new Run(text.Substring(position)));
            // Fit the actual longest sample line; reserve horizontal scrolling for smaller windows.
            var measure = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(_preview.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), _preview.FontSize,
                Brushes.Black, VisualTreeHelper.GetDpi(_preview).PixelsPerDip);
            _preview.Document = new FlowDocument(paragraph)
            {
                PagePadding = new Thickness(0), ColumnWidth = double.PositiveInfinity,
                PageWidth = Math.Max(600, Math.Ceiling(measure.WidthIncludingTrailingWhitespace) + 60),
            };
            int count = text.Split('\n').Length;
            var numbers = new System.Text.StringBuilder();
            for (int line = 1; line <= count; line++) { if (line > 1) numbers.Append('\n'); numbers.Append(line); }
            _lineNumbers.Document = new FlowDocument(new Paragraph(new Run(numbers.ToString()))
            {
                Margin = new Thickness(0), LineHeight = 21, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextAlignment = TextAlignment.Right,
            }) { PagePadding = new Thickness(0) };
            _preview.ScrollToHome();
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
                _settings.Editor.ShowParameterHints = _parameterHints.IsChecked == true;
                _settings.Editor.LearnFromUsage = _learnFromUsage.IsChecked == true;
                _settings.Results.InsertIdentity = _identitySkip.IsChecked == true ? InsertIdentityMode.Skip : InsertIdentityMode.KeepValues;
                SettingsProvider.Save(_settings);

                ExecuteGuard.Apply(_settings.Risk);
                if (_settings.Risk.WarnBeforeExecute && !ExecuteGuard.IsHooked)
                    MessageBox.Show(this, "บันทึกแล้ว แต่ตัวเตือนก่อน Execute ยังไม่ทำงาน:\n\n" + ExecuteGuard.Status, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                Log.Error("Saving settings", ex);
                MessageBox.Show(this, "บันทึกไม่สำเร็จ:\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
