using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SqlAssist.Core.Results;

namespace SqlAssist.Results
{
    /// <summary>Results grid › Generate Script As › UPDATE: asks for the target table and which columns are SET and which
    /// are the key (WHERE), and previews the first rows before the script goes anywhere. Nothing is guessed from the
    /// result set: the table starts empty and no column starts as a key.
    /// A plain WPF Window (see <see cref="DialogHost"/>), like the Snippets Manager.</summary>
    internal sealed class UpdateScriptWindow : Window
    {
        private const int PreviewRows = 10;

        private sealed class ColumnRow : INotifyPropertyChanged
        {
            private bool _isSet;
            private bool _isKey;

            public ColumnRow(int index, string name, string type, bool isSet)
            {
                Index = index; Name = name; Type = type; _isSet = isSet;
            }

            public int Index { get; }
            public string Name { get; }
            public string Type { get; }

            public bool IsSet
            {
                get => _isSet;
                set { if (_isSet == value) return; _isSet = value; Raise(nameof(IsSet)); }
            }

            /// <summary>Ticking a key takes the column out of SET, since a key is normally what finds the row, not
            /// what changes. SET can be ticked again afterwards when that is really wanted.</summary>
            public bool IsKey
            {
                get => _isKey;
                set
                {
                    if (_isKey == value) return;
                    _isKey = value;
                    if (value) IsSet = false;
                    Raise(nameof(IsKey));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        private readonly IReadOnlyList<ResultColumn> _columns;
        private readonly ResultValueCache _cache;
        private readonly List<ColumnRow> _rows = new List<ColumnRow>();
        private readonly TextBox _table = new TextBox { Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly ListView _list = new ListView();
        private readonly TextBlock _problems = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick };
        private readonly TextBlock _warnings = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange };
        private readonly TextBox _preview = new TextBox
        {
            IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
        };
        private readonly ComboBox _target = new ComboBox { Width = 170, Height = 28 };
        private readonly Button _create = new Button { Content = "สร้าง Script", Width = 110, Height = 28, Margin = new Thickness(8, 0, 0, 0) };
        private IReadOnlyList<DuplicateKey> _duplicates = new DuplicateKey[0];
        private string _duplicatesFor;
        private bool _loading = true;

        /// <summary>The finished script; set when the window closes with OK.</summary>
        public string Script { get; private set; }
        public ScriptTarget Target { get; private set; }

        /// <param name="selected">Data columns selected in the grid: they start ticked as SET.</param>
        public UpdateScriptWindow(IReadOnlyList<ResultColumn> columns, IReadOnlyList<int> selected, ResultValueCache cache, ScriptTarget target)
        {
            _columns = columns;
            _cache = cache;
            Title = "SqlAssist: Generate Script As › UPDATE";
            Width = 860;
            Height = 780;
            MinWidth = 640;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            var names = ResultScriptBuilder.ColumnNames(columns);
            for (int i = 0; i < columns.Count; i++)
            {
                var row = new ColumnRow(i, names[i], columns[i].TypeName ?? "", selected.Contains(i));
                row.PropertyChanged += (s, e) => { if (!_loading) Refresh(); };
                _rows.Add(row);
            }
            _list.ItemsSource = _rows;
            _target.Items.Add("New Query Window");
            _target.Items.Add("Clipboard");
            _target.Items.Add("File...");
            _target.SelectedIndex = (int)target;

            Content = BuildLayout();
            _table.TextChanged += (s, e) => { if (!_loading) Refresh(); };
            _create.Click += (s, e) => Create();
            Loaded += (s, e) => { _loading = false; Refresh(); _table.Focus(); };
        }

        private UIElement BuildLayout()
        {
            var root = new DockPanel { Margin = new Thickness(12) };

            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var cancel = new Button { Content = "ยกเลิก", Width = 90, Height = 28, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += (s, e) => DialogResult = false;
            DockPanel.SetDock(cancel, Dock.Right);
            bottom.Children.Add(cancel);
            DockPanel.SetDock(_create, Dock.Right);
            bottom.Children.Add(_create);
            DockPanel.SetDock(_target, Dock.Right);
            bottom.Children.Add(_target);
            bottom.Children.Add(new TextBlock { Text = "ส่งไปที่", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0) });
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            var top = new StackPanel();
            top.Children.Add(new TextBlock { Text = "ชื่อตารางปลายทาง", Margin = new Thickness(0, 0, 0, 3) });
            top.Children.Add(_table);
            top.Children.Add(new TextBlock
            {
                Text = "เช่น dbo.Orders หรือ [Sales].[Order Line] — SqlAssist ไม่เดาชื่อตารางจากผลลัพธ์ จึงต้องระบุเอง",
                Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 2, 0, 10),
            });
            top.Children.Add(new TextBlock
            {
                Text = "คอลัมน์ (" + _columns.Count + ") — SET คือคอลัมน์ที่จะเปลี่ยนค่า, Key คือคอลัมน์ที่ใช้หาแถวใน WHERE (เลือกได้หลายคอลัมน์ ต่อด้วย AND)",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3),
            });
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            var notes = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            notes.Children.Add(new TextBlock
            {
                Text = "Key ที่เลือกเป็นการระบุของคุณเอง SqlAssist ไม่รับประกันว่าไม่ซ้ำ (unique) ในฐานข้อมูลจริง — ถ้า Key ซ้ำ UPDATE จะกระทบหลายแถว ตรวจ PRIMARY KEY / UNIQUE ของตารางก่อนรัน " +
                       "และเมื่อติ๊ก Key คอลัมน์นั้นจะถูกเอาออกจาก SET ให้ก่อน (ติ๊ก SET กลับเองได้)",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11,
            });
            notes.Children.Add(_warnings);
            notes.Children.Add(_problems);

            var previewBox = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var previewLabel = new TextBlock { Text = "ตัวอย่าง Script (" + PreviewRows + " แถวแรก)", Margin = new Thickness(0, 0, 0, 3) };
            DockPanel.SetDock(previewLabel, Dock.Top);
            previewBox.Children.Add(previewLabel);
            previewBox.Children.Add(_preview);
            previewBox.Height = 200;
            DockPanel.SetDock(previewBox, Dock.Bottom);
            root.Children.Add(previewBox);

            var grid = new GridView();
            grid.Columns.Add(new GridViewColumn { Header = "คอลัมน์", Width = 240, DisplayMemberBinding = new Binding(nameof(ColumnRow.Name)) });
            grid.Columns.Add(new GridViewColumn { Header = "ชนิดข้อมูล", Width = 200, DisplayMemberBinding = new Binding(nameof(ColumnRow.Type)) });
            grid.Columns.Add(CheckColumn("SET", nameof(ColumnRow.IsSet)));
            grid.Columns.Add(CheckColumn("Key (WHERE)", nameof(ColumnRow.IsKey)));
            _list.View = grid;
            root.Children.Add(new DockPanel { LastChildFill = true, Children = { WithDock(notes, Dock.Bottom), _list } });
            return root;
        }

        private static UIElement WithDock(UIElement element, Dock dock)
        {
            DockPanel.SetDock(element, dock);
            return element;
        }

        private static GridViewColumn CheckColumn(string header, string path)
        {
            var box = new FrameworkElementFactory(typeof(CheckBox));
            box.SetBinding(CheckBox.IsCheckedProperty, new Binding(path) { Mode = BindingMode.TwoWay });
            box.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            return new GridViewColumn { Header = header, Width = 100, CellTemplate = new DataTemplate { VisualTree = box } };
        }

        private List<int> Columns(Func<ColumnRow, bool> pick) => _rows.Where(pick).Select(r => r.Index).ToList();

        private UpdateScriptOptions Options() => new UpdateScriptOptions(_table.Text, Columns(r => r.IsSet), Columns(r => r.IsKey));

        private void Refresh()
        {
            try
            {
                var options = Options();
                var needed = options.SetColumns.Union(options.KeyColumns).ToList();
                var result = UpdateScriptBuilder.Build(_columns, _cache.Rows(needed, PreviewRows), options, "\r\n");

                _problems.Text = result.IsValid ? "" : "ยังสร้าง Script ไม่ได้:\r\n" + string.Join("\r\n", result.Errors.Select(e => "• " + e));
                _preview.Text = result.IsValid ? result.Script + (_cache.RowCount > PreviewRows
                    ? "-- ... และอีก " + (_cache.RowCount - PreviewRows).ToString("N0") + " แถว (ตัวอย่างแสดงแค่ " + PreviewRows + " แถวแรก)\r\n" : "") : "";
                _create.IsEnabled = result.IsValid;
                RefreshWarnings(options);
            }
            catch (Exception ex)
            {
                Log.Error("UPDATE script preview", ex);
                _problems.Text = "สร้างตัวอย่างไม่สำเร็จ: " + ex.Message;
                _create.IsEnabled = false;
            }
        }

        /// <summary>Key duplicates need every row's key values, so they are read once per set of key columns.</summary>
        private void RefreshWarnings(UpdateScriptOptions options)
        {
            var warnings = new StringBuilder();
            string signature = string.Join(",", options.KeyColumns);
            if (signature != _duplicatesFor)
            {
                var previous = Mouse.OverrideCursor;
                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    _duplicates = options.KeyColumns.Count == 0 ? new DuplicateKey[0]
                        : UpdateScriptBuilder.FindDuplicateKeys(_columns, _cache.Rows(options.KeyColumns, _cache.RowCount), options.KeyColumns);
                    _duplicatesFor = signature;
                }
                finally
                {
                    Mouse.OverrideCursor = previous;
                }
            }
            if (_duplicates.Count > 0)
            {
                int rows = _duplicates.Sum(d => d.Rows.Count);
                warnings.Append("⚠ Key ซ้ำ ").Append(_duplicates.Count.ToString("N0")).Append(" ชุด (รวม ").Append(rows.ToString("N0"))
                    .Append(" แถว): หลายแถวใช้เงื่อนไข WHERE เดียวกัน คำสั่ง UPDATE ของแถวเหล่านั้นจะชนกัน เช่น");
                foreach (var d in _duplicates.Take(3))
                    warnings.Append("\r\n   ").Append(d.Condition).Append("  (แถวที่ ").Append(string.Join(", ", d.Rows.Take(6))).Append(d.Rows.Count > 6 ? ", ..." : "").Append(')');
            }
            var both = _rows.Where(r => r.IsSet && r.IsKey).Select(r => r.Name).ToList();
            if (both.Count > 0)
                warnings.Append(warnings.Length > 0 ? "\r\n" : "").Append("⚠ ").Append(string.Join(", ", both)).Append(" ถูกใช้ทั้ง SET และ Key");
            _warnings.Text = warnings.ToString();
        }

        private void Create()
        {
            var previous = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var options = Options();
                var needed = options.SetColumns.Union(options.KeyColumns).ToList();
                var result = UpdateScriptBuilder.Build(_columns, _cache.Rows(needed, _cache.RowCount), options);
                if (!result.IsValid)
                {
                    _problems.Text = string.Join("\r\n", result.Errors.Select(e => "• " + e));
                    return;
                }
                Script = result.Script;
                Target = (ScriptTarget)Math.Max(0, _target.SelectedIndex);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                Log.Error("UPDATE script", ex);
                MessageBox.Show(this, "สร้าง Script ไม่สำเร็จ:\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = previous;
            }
        }
    }
}
