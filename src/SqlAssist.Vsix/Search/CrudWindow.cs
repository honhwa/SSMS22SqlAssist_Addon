using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SqlAssist.Core.Completion;
using SqlAssist.Core.Schema;
using SqlAssist.Core.Search;
using SqlAssist.Core.Settings;
using SqlAssist.Results;

namespace SqlAssist.Search
{
    /// <summary>Search results › Generate CRUD procedures: which procedures, how they are named and where the script goes,
    /// with the script itself previewed as it changes. Nothing is executed. A plain WPF Window (see <see cref="DialogHost"/>)
    /// so it can be rendered outside SSMS: no ThreadHelper calls in here.</summary>
    internal sealed class CrudWindow : Window
    {
        private readonly DbObject _table;
        private readonly CheckBox _select = Check("Select"), _insert = Check("Insert"), _update = Check("Update"), _delete = Check("Delete");
        private readonly CheckBox _createOrAlter = new CheckBox { Content = "CREATE OR ALTER (SQL Server 2016 SP1 ขึ้นไป) — ไม่ติ๊กจะใช้ CREATE", Margin = new Thickness(0, 6, 0, 0) };
        private readonly TextBox _pattern = new TextBox { Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBlock _example = new TextBlock { Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
        private readonly TextBlock _skipped = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange, Margin = new Thickness(0, 6, 0, 0) };
        private readonly TextBox _preview = new TextBox
        {
            IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 13, TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        private readonly ComboBox _target = new ComboBox { Width = 170, Height = 28 };
        private readonly Button _create = new Button { Content = "สร้าง Script", Width = 110, Height = 28, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        private bool _loading = true;

        /// <summary>What the user asked for; valid when the window closed with OK.</summary>
        public CrudOptions Options { get; private set; }
        public ScriptTarget Target { get; private set; }

        public CrudWindow(DbObject table, string database, CrudSettings settings, ScriptTarget target)
        {
            _table = table;
            Title = "SqlAssist: Generate CRUD procedures";
            Width = 780;
            Height = 700;
            MinWidth = 600;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            _select.IsChecked = _insert.IsChecked = _update.IsChecked = _delete.IsChecked = true;
            _pattern.Text = settings.NamePattern;
            _createOrAlter.IsChecked = settings.CreateOrAlter;
            _target.Items.Add("New Query Window");
            _target.Items.Add("Clipboard");
            _target.Items.Add("File...");
            _target.SelectedIndex = (int)target;

            Content = BuildLayout(database);
            foreach (var box in new[] { _select, _insert, _update, _delete, _createOrAlter })
            {
                box.Checked += (s, e) => Refresh();
                box.Unchecked += (s, e) => Refresh();
            }
            _pattern.TextChanged += (s, e) => Refresh();
            _create.Click += (s, e) => Accept();
            Loaded += (s, e) => { _loading = false; Refresh(); _pattern.Focus(); _pattern.SelectAll(); };
        }

        private static CheckBox Check(string text) => new CheckBox { Content = text, Margin = new Thickness(0, 0, 18, 0) };

        private UIElement BuildLayout(string database)
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
            top.Children.Add(new TextBlock
            {
                Text = TableScriptBuilder.Bracket(database) + "." + TableScriptBuilder.Bracket(_table.Schema) + "." + TableScriptBuilder.Bracket(_table.Name),
                FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 8),
            });
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var box in new[] { _select, _insert, _update, _delete }) actions.Children.Add(box);
            top.Children.Add(new TextBlock { Text = "Procedure ที่จะสร้าง", Margin = new Thickness(0, 0, 0, 3) });
            top.Children.Add(actions);
            top.Children.Add(new TextBlock { Text = "รูปแบบชื่อ ({Schema}, {Table}, {Action})", Margin = new Thickness(0, 10, 0, 3) });
            top.Children.Add(_pattern);
            top.Children.Add(_example);
            top.Children.Add(_createOrAlter);
            top.Children.Add(_skipped);
            top.Children.Add(new TextBlock
            {
                Text = "SqlAssist ไม่รัน script นี้ให้ — อ่านแล้วรันเองใน database ที่ต้องการ Update / Delete ใช้ primary key เท่านั้น (ตารางที่ไม่มี key จะข้ามสองตัวนี้)",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 6, 0, 6),
            });
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);
            root.Children.Add(_preview);
            return root;
        }

        private CrudOptions Gather() => new CrudOptions
        {
            Actions = (_select.IsChecked == true ? CrudActions.Select : 0) | (_insert.IsChecked == true ? CrudActions.Insert : 0) |
                      (_update.IsChecked == true ? CrudActions.Update : 0) | (_delete.IsChecked == true ? CrudActions.Delete : 0),
            NamePattern = _pattern.Text,
            CreateOrAlter = _createOrAlter.IsChecked == true,
        };

        private void Refresh()
        {
            if (_loading) return;
            try
            {
                var options = Gather();
                var script = CrudScriptBuilder.Build(_table, options);
                _preview.Text = script.Text;
                _example.Text = "ตัวอย่าง: " + TableScriptBuilder.Bracket(_table.Schema) + "." +
                    TableScriptBuilder.Bracket(CrudScriptBuilder.ProcedureName(options.NamePattern, _table.Schema, _table.Name, CrudActions.Insert));
                _skipped.Text = script.Skipped.Count == 0 ? "" : "ข้าม: " + string.Join("; ", script.Skipped);
                _create.IsEnabled = options.Actions != CrudActions.None;
            }
            catch (Exception ex)
            {
                Log.Error("CRUD window refresh", ex);
                _preview.Text = "";
                _create.IsEnabled = false;
            }
        }

        private void Accept()
        {
            Options = Gather();
            Target = (ScriptTarget)Math.Max(0, _target.SelectedIndex);
            DialogResult = true;
        }
    }
}
