using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SqlAssist.Core.Clipboard;
using SqlAssist.Results;

namespace SqlAssist.ClipboardSql
{
    /// <summary>Tools › SqlAssist › CSV to SQL…: paste cells copied from Excel (tab-separated, the default) or CSV text (comma,
    /// semicolon or pipe), pick what each column is, and get IN (...), VALUES (...) or CREATE TABLE #Temp + INSERT. The pasted text stays editable and
    /// the parsed columns and the SQL are previewed as it changes; nothing is executed, and the clipboard's content is
    /// never written to the log. A plain WPF Window (see <see cref="DialogHost"/>), like the Snippets Manager.</summary>
    internal sealed class ClipboardSqlWindow : Window
    {
        private const int PreviewRows = 50;
        private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(350);

        private sealed class TypeChoice
        {
            public TypeChoice(ClipboardColumnType type, string label) { Type = type; Label = label; }
            public ClipboardColumnType Type { get; }
            public string Label { get; }
        }

        private static readonly TypeChoice[] Types =
        {
            new TypeChoice(ClipboardColumnType.Text, "ข้อความ (nvarchar)"),
            new TypeChoice(ClipboardColumnType.Varchar, "ข้อความ (varchar)"),
            new TypeChoice(ClipboardColumnType.Int, "int"),
            new TypeChoice(ClipboardColumnType.BigInt, "bigint"),
            new TypeChoice(ClipboardColumnType.Decimal, "decimal"),
            new TypeChoice(ClipboardColumnType.Float, "float"),
            new TypeChoice(ClipboardColumnType.Bit, "bit"),
            new TypeChoice(ClipboardColumnType.Date, "date"),
            new TypeChoice(ClipboardColumnType.DateTime, "datetime"),
            new TypeChoice(ClipboardColumnType.DateTime2, "datetime2"),
            new TypeChoice(ClipboardColumnType.UniqueIdentifier, "uniqueidentifier"),
        };

        private sealed class ColumnRow : INotifyPropertyChanged
        {
            private ClipboardColumnType _type;
            private string _name;
            private string _sample;

            public ColumnRow(int index) { Index = index; }
            public int Index { get; }
            public int Number => Index + 1;
            public string Name { get => _name; set { _name = value; Raise(nameof(Name)); } }
            public string Sample { get => _sample; set { _sample = value; Raise(nameof(Sample)); } }
            public ClipboardColumnType Type { get => _type; set { if (_type == value) return; _type = value; Raise(nameof(Type)); } }
            public event PropertyChangedEventHandler PropertyChanged;
            private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        private readonly TextBox _input = new TextBox
        {
            AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Consolas"), FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
        };
        private readonly CheckBox _header = new CheckBox { Content = "แถวแรกเป็นชื่อคอลัมน์", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        private readonly RadioButton _in = new RadioButton { Content = "IN (...)", GroupName = "kind", IsChecked = true, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly RadioButton _values = new RadioButton { Content = "VALUES (...), (...)", GroupName = "kind", Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly RadioButton _temp = new RadioButton { Content = "CREATE TABLE #Temp + INSERT", GroupName = "kind", VerticalAlignment = VerticalAlignment.Center };
        private readonly ComboBox _delimiter = new ComboBox { Width = 170, Height = 24, Margin = new Thickness(6, 0, 16, 0) };
        private readonly ComboBox _inColumn = new ComboBox { MinWidth = 160, Height = 24, Margin = new Thickness(6, 0, 12, 0) };
        private readonly CheckBox _distinct = new CheckBox { Content = "ตัดค่าซ้ำ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        private readonly TextBox _tempName = new TextBox { Text = "#Temp", Width = 120, Height = 24, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        private readonly ComboBox _empty = new ComboBox { Width = 130, Height = 24, Margin = new Thickness(6, 0, 16, 0) };
        private readonly ComboBox _nullText = new ComboBox { Width = 150, Height = 24, Margin = new Thickness(6, 0, 16, 0) };
        private readonly ComboBox _dateFormat = new ComboBox { Width = 120, Height = 24, Margin = new Thickness(6, 0, 0, 0) };
        private readonly ListView _columns = new ListView();
        private readonly TextBlock _summary = new TextBlock { Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 3) };
        private readonly TextBlock _errors = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick };
        private readonly TextBlock _warnings = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange };
        private readonly TextBox _preview = new TextBox
        {
            IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
        };
        private readonly TextBlock _previewLabel = new TextBlock { Margin = new Thickness(0, 0, 0, 3) };
        private readonly ComboBox _target = new ComboBox { Width = 160, Height = 28 };
        private readonly Button _create = new Button { Content = "สร้าง SQL", Width = 110, Height = 28, Margin = new Thickness(8, 0, 0, 0) };
        /// <summary>What separates cells, in the order of <see cref="_delimiter"/>'s items. Tab is first because that is what Excel
        /// puts on the clipboard; it is never guessed from the text, since a single column of names with commas in them is common.</summary>
        private static readonly (string Label, char Character)[] Delimiters =
        {
            ("Tab (ข้อมูลจาก Excel)", '\t'), ("คอมมา ,", ','), ("เซมิโคลอน ;", ';'), ("ไปป์ |", '|'),
        };

        private readonly DispatcherTimer _timer;
        private readonly List<ColumnRow> _rows = new List<ColumnRow>();
        private DelimitedTable _table = new DelimitedTable(new string[0][], new string[0], new string[0]);
        private bool _inputChanged = true;
        private bool _ready;

        /// <summary>The finished SQL; set when the window closes with OK.</summary>
        public string Script { get; private set; }
        public ScriptTarget Target { get; private set; }
        /// <summary>For the status line, e.g. "VALUES (3 rows)".</summary>
        public string What { get; private set; }

        /// <param name="initialText">Text to start with; when null the window starts from whatever text is on the clipboard.</param>
        public ClipboardSqlWindow(string initialText = null)
        {
            Title = "SqlAssist: CSV to SQL";
            Width = 940;
            Height = 900;
            MinWidth = 720;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            foreach (var delimiter in Delimiters) _delimiter.Items.Add(delimiter.Label);
            _delimiter.SelectedIndex = 0;
            _empty.Items.Add("SQL NULL");
            _empty.Items.Add("สตริงว่าง ''");
            _empty.SelectedIndex = 0;
            _nullText.Items.Add("SQL NULL");
            _nullText.Items.Add("ข้อความ 'NULL'");
            _nullText.SelectedIndex = 0;
            _dateFormat.Items.Add("yyyy-MM-dd");
            _dateFormat.Items.Add("dd/MM/yyyy");
            _dateFormat.Items.Add("MM/dd/yyyy");
            _dateFormat.SelectedIndex = 0;
            _target.Items.Add("New Query Window");
            _target.Items.Add("Clipboard");
            _target.SelectedIndex = 1;
            _columns.ItemsSource = _rows;

            _timer = new DispatcherTimer { Interval = RefreshDelay };
            _timer.Tick += (s, e) => { _timer.Stop(); Refresh(); };

            Content = BuildLayout();

            _input.TextChanged += (s, e) => { _inputChanged = true; Schedule(); };
            _header.Click += (s, e) => { _inputChanged = true; Schedule(); };
            _delimiter.SelectionChanged += (s, e) => { _inputChanged = true; Schedule(); };
            foreach (var radio in new[] { _in, _values, _temp }) radio.Click += (s, e) => { UpdateEnabled(); Schedule(); };
            _distinct.Click += (s, e) => Schedule();
            _inColumn.SelectionChanged += (s, e) => Schedule();
            _tempName.TextChanged += (s, e) => Schedule();
            foreach (var combo in new[] { _empty, _nullText, _dateFormat }) combo.SelectionChanged += (s, e) => Schedule();
            _create.Click += (s, e) => Create();
            _create.IsEnabled = false;
            UpdateEnabled();
            Loaded += (s, e) =>
            {
                _ready = true;
                if (initialText != null) _input.Text = initialText; else PasteFromClipboard();
                Schedule();
                _input.Focus();
            };
            Closed += (s, e) => _timer.Stop();
        }

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 0 input label
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 });  // 1 input
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 2 options
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 });  // 3 columns
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 4 messages
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.4, GridUnitType.Star), MinHeight = 100 }); // 5 preview
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 6 bottom

            var inputBar = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var clear = new Button { Content = "ล้าง", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
            clear.Click += (s, e) => _input.Clear();
            DockPanel.SetDock(clear, Dock.Right);
            inputBar.Children.Add(clear);
            var paste = new Button { Content = "วางจาก Clipboard", Padding = new Thickness(10, 2, 10, 2) };
            paste.Click += (s, e) => PasteFromClipboard();
            DockPanel.SetDock(paste, Dock.Right);
            inputBar.Children.Add(paste);
            inputBar.Children.Add(new TextBlock { Text = "ข้อมูลที่คัดลอกมา (Excel หรือข้อความ CSV) — แก้ไขในช่องนี้ได้", VerticalAlignment = VerticalAlignment.Center });
            Grid.SetRow(inputBar, 0);
            root.Children.Add(inputBar);
            Grid.SetRow(_input, 1);
            root.Children.Add(_input);

            var options = new StackPanel { Margin = new Thickness(0, 8, 0, 6) };
            var line1 = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            line1.Children.Add(_header);
            line1.Children.Add(new TextBlock { Text = "ตัวคั่น cell:", VerticalAlignment = VerticalAlignment.Center });
            line1.Children.Add(_delimiter);
            line1.Children.Add(new TextBlock { Text = "ผลลัพธ์:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            line1.Children.Add(_in);
            line1.Children.Add(_values);
            line1.Children.Add(_temp);
            options.Children.Add(line1);
            var line2 = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            line2.Children.Add(new TextBlock { Text = "IN: คอลัมน์", VerticalAlignment = VerticalAlignment.Center });
            line2.Children.Add(_inColumn);
            line2.Children.Add(_distinct);
            line2.Children.Add(new TextBlock { Text = "ชื่อ temp table", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
            line2.Children.Add(_tempName);
            options.Children.Add(line2);
            var line3 = new WrapPanel();
            line3.Children.Add(new TextBlock { Text = "cell ว่าง =", VerticalAlignment = VerticalAlignment.Center });
            line3.Children.Add(_empty);
            line3.Children.Add(new TextBlock { Text = "ข้อความ NULL (ตัวพิมพ์ใหญ่ทั้งหมดเท่านั้น) =", VerticalAlignment = VerticalAlignment.Center });
            line3.Children.Add(_nullText);
            line3.Children.Add(new TextBlock { Text = "รูปแบบวันที่ในข้อมูล", VerticalAlignment = VerticalAlignment.Center });
            line3.Children.Add(_dateFormat);
            options.Children.Add(line3);
            Grid.SetRow(options, 2);
            root.Children.Add(options);

            var columnsBox = new DockPanel();
            DockPanel.SetDock(_summary, Dock.Top);
            columnsBox.Children.Add(_summary);
            var grid = new GridView();
            grid.Columns.Add(new GridViewColumn { Header = "#", Width = 40, DisplayMemberBinding = new Binding(nameof(ColumnRow.Number)) });
            grid.Columns.Add(new GridViewColumn { Header = "ชื่อคอลัมน์", Width = 220, DisplayMemberBinding = new Binding(nameof(ColumnRow.Name)) });
            grid.Columns.Add(new GridViewColumn { Header = "ตัวอย่างค่า (แถวข้อมูลแรก)", Width = 280, DisplayMemberBinding = new Binding(nameof(ColumnRow.Sample)) });
            grid.Columns.Add(TypeColumn());
            _columns.View = grid;
            columnsBox.Children.Add(_columns);
            Grid.SetRow(columnsBox, 3);
            root.Children.Add(columnsBox);

            var messages = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            messages.Children.Add(_errors);
            messages.Children.Add(_warnings);
            Grid.SetRow(messages, 4);
            root.Children.Add(messages);

            var previewBox = new DockPanel();
            DockPanel.SetDock(_previewLabel, Dock.Top);
            previewBox.Children.Add(_previewLabel);
            previewBox.Children.Add(_preview);
            Grid.SetRow(previewBox, 5);
            root.Children.Add(previewBox);

            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var close = new Button { Content = "ปิด", Width = 90, Height = 28, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            close.Click += (s, e) => DialogResult = false;
            DockPanel.SetDock(close, Dock.Right);
            bottom.Children.Add(close);
            DockPanel.SetDock(_create, Dock.Right);
            bottom.Children.Add(_create);
            DockPanel.SetDock(_target, Dock.Right);
            bottom.Children.Add(_target);
            bottom.Children.Add(new TextBlock { Text = "ส่งไปที่", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0) });
            Grid.SetRow(bottom, 6);
            root.Children.Add(bottom);
            return root;
        }

        private static GridViewColumn TypeColumn()
        {
            var combo = new FrameworkElementFactory(typeof(ComboBox));
            combo.SetValue(ItemsControl.ItemsSourceProperty, Types);
            combo.SetValue(ItemsControl.DisplayMemberPathProperty, nameof(TypeChoice.Label));
            combo.SetValue(Selector.SelectedValuePathProperty, nameof(TypeChoice.Type));
            combo.SetBinding(Selector.SelectedValueProperty, new Binding(nameof(ColumnRow.Type)) { Mode = BindingMode.TwoWay });
            combo.SetValue(WidthProperty, 170d);
            return new GridViewColumn { Header = "ชนิดข้อมูล", Width = 200, CellTemplate = new DataTemplate { VisualTree = combo } };
        }

        private void Schedule()
        {
            if (!_ready) return;
            _timer.Stop();
            _timer.Start();
        }

        private void UpdateEnabled()
        {
            _inColumn.IsEnabled = _distinct.IsEnabled = _in.IsChecked == true;
            _tempName.IsEnabled = _temp.IsChecked == true;
        }

        private void PasteFromClipboard()
        {
            string text = null;
            try
            {
                if (Clipboard.ContainsText()) text = Clipboard.GetText();
            }
            catch (Exception ex)
            {
                // The message of a clipboard failure never holds the clipboard's content.
                Log.Error("CSV to SQL: reading the clipboard", ex);
                _errors.Text = "อ่าน Clipboard ไม่ได้ (อาจมีโปรแกรมอื่นถืออยู่) ลองใหม่อีกครั้ง";
                return;
            }
            if (string.IsNullOrEmpty(text)) { _errors.Text = "ใน Clipboard ไม่มีข้อความ"; return; }
            if (text.Length > DelimitedTextParser.MaxChars)
            {
                _errors.Text = "ข้อมูลใน Clipboard ใหญ่เกิน " + DelimitedTextParser.MaxChars.ToString("N0") + " ตัวอักษร จึงไม่ได้วางให้ — แบ่งคัดลอกทีละส่วน";
                return;
            }
            _input.Text = text;
        }

        /// <summary>Re-reads the text when it changed (keeping the chosen column types if the shape is the same), then
        /// rebuilds the SQL preview from the current options.</summary>
        private void Refresh()
        {
            try
            {
                if (_inputChanged)
                {
                    _inputChanged = false;
                    _table = DelimitedTextParser.Parse(_input.Text, Delimiters[Math.Max(0, _delimiter.SelectedIndex)].Character);
                    SyncColumns();
                }
                UpdateEnabled();

                var messages = new List<string>(_table.Errors);
                var warnings = new List<string>(_table.Warnings);
                string script = "";
                _create.IsEnabled = false;
                _previewLabel.Text = "ตัวอย่าง SQL";

                if (!_table.IsValid)
                {
                    _summary.Text = "";
                }
                else if (_table.Rows.Count == 0)
                {
                    _summary.Text = "ยังไม่มีข้อมูล — วางข้อมูลจาก Excel หรือข้อความ CSV ในช่องด้านบน";
                }
                else
                {
                    int dataRows = _table.Rows.Count - (_header.IsChecked == true ? 1 : 0);
                    _summary.Text = "ข้อมูล " + dataRows.ToString("N0") + " แถว × " + _table.ColumnCount + " คอลัมน์ — เลือกชนิดข้อมูลของแต่ละคอลัมน์ (ค่าเริ่มต้นเป็นข้อความเพื่อไม่ให้ 00123 กลายเป็น 123)";
                    var result = ClipboardSqlBuilder.Build(Request(PreviewRows));
                    messages.AddRange(result.Errors);
                    warnings.AddRange(result.Warnings);
                    if (result.IsValid)
                    {
                        script = result.Script;
                        _create.IsEnabled = true;
                        if (result.IsTruncated) _previewLabel.Text = "ตัวอย่าง SQL (แสดง " + PreviewRows + " แถว/ค่าแรก — ตรวจข้อมูลครบทุกแถวแล้ว)";
                    }
                }
                _errors.Text = string.Join("\r\n", messages.Select(m => "• " + m));
                _warnings.Text = string.Join("\r\n", warnings.Select(m => "⚠ " + m));
                _preview.Text = script;
            }
            catch (Exception ex)
            {
                Log.Error("CSV to SQL: preview", ex);
                _errors.Text = "สร้างตัวอย่างไม่สำเร็จ: " + ex.GetType().Name;
                _create.IsEnabled = false;
            }
        }

        private void SyncColumns()
        {
            int count = _table.IsValid ? _table.ColumnCount : 0;
            bool sameShape = count == _rows.Count;
            if (!sameShape)
            {
                _rows.Clear();
                for (int c = 0; c < count; c++)
                {
                    var row = new ColumnRow(c);
                    row.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ColumnRow.Type)) Schedule(); };
                    _rows.Add(row);
                }
            }
            var names = ClipboardSqlBuilder.HeaderNames(_table.Rows, _header.IsChecked == true, count);
            int firstData = _header.IsChecked == true ? 1 : 0;
            for (int c = 0; c < count; c++)
            {
                _rows[c].Name = names[c];
                string sample = _table.Rows.Count > firstData ? _table.Rows[firstData][c] : "";
                _rows[c].Sample = sample.Length > 40 ? sample.Substring(0, 40) + "…" : sample.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            }
            _columns.Items.Refresh();

            int selected = Math.Max(0, _inColumn.SelectedIndex);
            _inColumn.Items.Clear();
            foreach (string name in names) _inColumn.Items.Add(name);
            if (count > 0) _inColumn.SelectedIndex = Math.Min(selected, count - 1);
        }

        private ClipboardSqlRequest Request(int previewRows) => new ClipboardSqlRequest
        {
            Rows = _table.Rows,
            FirstRowIsHeader = _header.IsChecked == true,
            ColumnTypes = _rows.Select(r => r.Type).ToList(),
            Kind = _temp.IsChecked == true ? ClipboardSqlKind.TempTable : _values.IsChecked == true ? ClipboardSqlKind.Values : ClipboardSqlKind.In,
            InColumn = Math.Max(0, _inColumn.SelectedIndex),
            Distinct = _distinct.IsChecked == true,
            TempTableName = _tempName.Text,
            EmptyCell = _empty.SelectedIndex == 1 ? EmptyCellRule.EmptyString : EmptyCellRule.Null,
            NullText = _nullText.SelectedIndex == 1 ? NullTextRule.Text : NullTextRule.Null,
            DateFormat = (DateInputFormat)Math.Max(0, _dateFormat.SelectedIndex),
            PreviewRows = previewRows,
        };

        private void Create()
        {
            var previous = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                if (_inputChanged) Refresh();
                var request = Request(0);
                var result = ClipboardSqlBuilder.Build(request);
                if (!result.IsValid)
                {
                    _errors.Text = string.Join("\r\n", result.Errors.Select(m => "• " + m));
                    return;
                }
                Script = result.Script;
                Target = _target.SelectedIndex == 0 ? ScriptTarget.NewWindow : ScriptTarget.Clipboard;
                What = (request.Kind == ClipboardSqlKind.In ? "IN list" : request.Kind == ClipboardSqlKind.Values ? "VALUES" : "#Temp script") +
                       " (" + result.RowCount.ToString("N0") + (result.RowCount == 1 ? " row)" : " rows)");
                DialogResult = true;
            }
            catch (Exception ex)
            {
                Log.Error("CSV to SQL: build", ex);
                MessageBox.Show(this, "สร้าง SQL ไม่สำเร็จ: " + ex.GetType().Name, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = previous;
            }
        }
    }
}
