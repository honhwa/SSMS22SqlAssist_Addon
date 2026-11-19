using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using SqlAssist.Core.Assistant;

/// <summary>Standalone tests of the real net48 WPF controls, not a substitute for SSMS/editor integration tests.</summary>
internal static class Program
{
    private static readonly Assembly Extension = typeof(SqlAssist.SqlAssistPackage).Assembly;
    private static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [STAThread]
    private static int Main(string[] args)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "SqlAssist-Assistant-Smoke-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(temporary);
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            typeof(ThreadHelper).GetMethod("SetUIThread", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
            // The real host provides JTF. Standalone tests supply its normal WPF synchronization context.
#pragma warning disable VSSDK005 // Standalone test process: no VS shell singleton exists.
            typeof(ThreadHelper).GetField("_joinableTaskContextCache", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, new Microsoft.VisualStudio.Threading.JoinableTaskContext(Thread.CurrentThread, SynchronizationContext.Current));
#pragma warning restore VSSDK005
            // Redirect this process's settings to a disposable test directory. Never save to the user's real settings.
            Extension.GetType("SqlAssist.Settings.SettingsProvider").GetField("FilePath", BindingFlags.Static | BindingFlags.Public).SetValue(null, Path.Combine(temporary, "settings.xml"));
            SetTheme(application, false);
            var control = (UserControl)Activator.CreateInstance(Extension.GetType("SqlAssist.Assistant.AssistantControl"), true);
            var input = Field<TextBox>(control, "_input");
            var provider = Field<ComboBox>(control, "_provider");
            var model = Field<ComboBox>(control, "_model");
            var session = Field<ChatSession>(control, "_session");
            Require(AllText(control).Any(t => t.Text == "ให้ช่วยเรื่อง SQL อะไรดี?"), "Empty chat has the reference-style welcome heading");
            Invoke(control, "Prepare", "Explain SQL");
            Require(input.Text.Contains("อธิบาย"), "Explain prepares a draft");
            Require(session.Messages.Count == 0, "Shortcut never sends");
            provider.SelectedItem = AiProviderKind.Ollama;
            model.Text = "manual-model";
            Require(Field<TextBlock>(control, "_context").Text.Contains("manual-model"), "Destination notice tracks model");
            var turn = session.Begin("example"); session.TryAppend(turn.Id, "# Example\n```sql\nSELECT 1;\n```"); session.Stop();
            Invoke(control, "Render");
            var sqlActions = Field<ContextMenu>(control, "_sqlActions");
            var reviewAction = sqlActions.Items.OfType<MenuItem>().Single(item => item.Header as string == "Review SQL");
            reviewAction.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Require(input.Text.Contains("ตรวจสอบ SQL") && session.Messages.Count == 2, "SQL shortcuts remain available during a conversation and only prepare a draft");
            Require(AllButtons(control).Any(b => b.Content as string == "Insert into Editor"), "SQL response exposes Insert");
            Require(AllButtons(control).Any(b => b.Content as string == "Copy"), "Code response exposes Copy");
            var settingsProvider = Extension.GetType("SqlAssist.Settings.SettingsProvider");
            var persisted = (SqlAssist.Core.Settings.SqlAssistSettings)settingsProvider.GetMethod("Load", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
            persisted.Assistant.ActiveProvider = AiProviderKind.Ollama;
            settingsProvider.GetMethod("Save", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { persisted });
            var settings = (Window)Activator.CreateInstance(Extension.GetType("SqlAssist.Assistant.AssistantSettingsWindow"), true);
            Require(Field<TabControl>(settings, "_tabs").Items.Count == 2, "Settings has OpenRouter and Ollama only");
            Require(Field<TabControl>(settings, "_tabs").SelectedIndex == 1, "Ollama's persisted ID selects the second tab rather than an enum index");
            settings.Close();
            SetTheme(application, true);
            var preview = new Window { Title = "SqlAssist AI Assistant — standalone UI test", Width = 860, Height = 760, Content = control };
            preview.Show();
            var frame = new DispatcherFrame();
#pragma warning disable VSTHRD001 // Test-only WPF message pump; no VS shell is running.
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
#pragma warning restore VSTHRD001
            Dispatcher.PushFrame(frame);
            preview.UpdateLayout();
            Require(control.ActualWidth > 500, "Assistant renders at a usable width; control=" + control.ActualWidth + ", window=" + preview.ActualWidth + ", desired=" + control.DesiredSize);
            var modelEditor = model.Template.FindName("PART_EditableTextBox", model) as TextBox;
            Require((modelEditor?.Background as SolidColorBrush)?.Color == Color.FromRgb(32, 32, 32), "Editable model field follows dark theme background");
            CheckOptionsAppearance(application);
            CheckSearchAppearance(application);
            CheckChatCancellation(control, provider, model, input, session);
            foreach (double width in new[] { 340.0, 460.0, 700.0 })
            {
                preview.Width = width;
                PumpUntil(() => Math.Abs(preview.ActualWidth - width) < 2);
                preview.UpdateLayout();
                var composer = Field<Border>(control, "_composer");
                Require(provider.IsDescendantOf(composer) && model.IsDescendantOf(composer) && input.IsDescendantOf(composer), "Provider/model and input belong to one composer");
                var inputBounds = input.TransformToAncestor(control).TransformBounds(new Rect(input.RenderSize));
                var modelBounds = model.TransformToAncestor(control).TransformBounds(new Rect(model.RenderSize));
                Require(inputBounds.Right <= control.ActualWidth + 1 && modelBounds.Right <= control.ActualWidth + 1, "Composer fits narrow docking widths");
            }
            Console.WriteLine("PASS: standalone WPF chat and settings, five Options tabs, Light/Dark, minimum-size scrolling, Save/Cancel, and Send/Stop/New Chat with test-only HTTP.");
            if (args.Contains("--preview"))
            {
                if (args.Contains("--light")) SetTheme(application, false);
                if (args.Contains("--dark")) SetTheme(application, true);
                if (args.Contains("--search"))
                {
                    preview.Close(); ((IDisposable)control).Dispose();
                    var searchPreview = CreateSearchPreview();
                    if (args.Contains("--dropdown")) searchPreview.Loaded += (s, e) =>
                    {
                        var view = (FrameworkElement)searchPreview.Content;
                        Field<System.Collections.Generic.SortedSet<string>>(view, "_selected").Add("Orders_Archive");
                        var databaseMenu = (ContextMenu)view.GetType().GetMethod("BuildDatabaseMenu", Instance)
                            .Invoke(view, new object[] { new[] { "Orders", "Orders_Archive", "Customers", "Reporting", "Sales_Production" } });
                        databaseMenu.IsOpen = true;
                    };
                    searchPreview.ShowDialog(); application.Shutdown();
                }
                else if (args.Contains("--options"))
                {
                    preview.Close(); ((IDisposable)control).Dispose();
                    var optionsPreview = (Window)Activator.CreateInstance(Extension.GetType("SqlAssist.Settings.OptionsWindow"), true);
                    optionsPreview.ShowDialog(); application.Shutdown();
                }
                else
                {
                    preview.Width = 460; preview.Height = 820;
                    application.ShutdownMode = ShutdownMode.OnLastWindowClose; application.Run();
                }
            }
            else { preview.Close(); ((IDisposable)control).Dispose(); application.Shutdown(); }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
    private static void SetTheme(Application app, bool dark)
    {
        app.Resources[EnvironmentColors.ToolWindowBackgroundBrushKey] = new SolidColorBrush(dark ? Color.FromRgb(32, 32, 32) : Colors.White);
        app.Resources[EnvironmentColors.ToolWindowTextBrushKey] = new SolidColorBrush(dark ? Colors.WhiteSmoke : Colors.Black);
        app.Resources[EnvironmentColors.ToolWindowBorderBrushKey] = new SolidColorBrush(dark ? Colors.DimGray : Colors.LightGray);
    }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Instance).GetValue(value);
    private static void Invoke(object value, string method, params object[] args) => value.GetType().GetMethod(method, Instance | BindingFlags.DeclaredOnly).Invoke(value, args);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); }

    /// <summary>Test-only sample results exercise real grid selection without contacting a database.</summary>
    private static Window CreateSearchPreview()
    {
        var search = (FrameworkElement)Activator.CreateInstance(Extension.GetType("SqlAssist.Search.SearchControl"), true);
        var window = new Window { Title = "SqlAssist Search — UI preview (sample data)", Width = 1100, Height = 740, Content = search };
        var rows = Field<System.Collections.IList>(search, "_results");
        foreach (var name in new[] { "Orders", "OrderDetails", "Customers" })
        {
            var item = new SqlAssist.Core.Search.SearchItem("SampleDB", "dbo", name, SqlAssist.Core.Search.SearchObjectType.Table,
                script: "CREATE TABLE [dbo].[" + name + "]\n(\n    [Id] int NOT NULL PRIMARY KEY,\n    [Name] nvarchar(100) NULL\n);\n");
            var match = new SqlAssist.Core.Search.SearchMatch(item, new SqlAssist.Core.Search.TextSpan[0], new SqlAssist.Core.Search.TextSpan[0]);
            rows.Add(Activator.CreateInstance(Extension.GetType("SqlAssist.Search.ResultRow"), new object[] { match }));
        }
        Field<DataGrid>(search, "_grid").SelectedIndex = 0;
        return window;
    }

    private static void CheckSearchAppearance(Application app)
    {
        var window = CreateSearchPreview();
        window.Show(); window.UpdateLayout();
        var search = (FrameworkElement)window.Content;
        var grid = Field<DataGrid>(search, "_grid");
        var code = Field<RichTextBox>(search, "_preview");
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(search).TransformBounds(new Rect(element.RenderSize));
        Require(Bounds(grid).Bottom < Bounds(code).Top, "Search restores results above the SQL preview at wide widths");
        var input = Field<TextBox>(search, "_searchBox");
        var types = Field<Button>(search, "_typesButton");
        var databases = Field<Button>(search, "_databasesButton");
        var server = Field<Button>(search, "_serverButton");
        foreach (var field in new Control[] { input, types, databases, server,
            AllButtons(search).Single(button => button.Content as string == "Refresh") })
        {
            Require(field.Height == 24 && field.ActualHeight == 24 && field.FontSize == 13,
                "Search toolbar fields and buttons use option A: 24-DIP height with unchanged 13-DIP text");
        }
        Require(input.Style != null, "Search input uses the same rounded field appearance as its dropdowns");
        Require(new[] { types, databases, server }.All(button => Math.Abs(Bounds(button).Top - Bounds(input).Top) < 1), "Search keeps its original single-row toolbar");
        Require(types.Width == 150 && databases.Width == 170 && server.Width == 170, "Search restores original filter widths");
        Require(!AllElements<TextBlock>(search).Any(text => text.Text == "ผลการค้นหา" || text.Text == "ตัวอย่าง SQL"), "Search removes the added section headings");
        Require(grid.Columns[0].Width.IsAbsolute && code.FontSize == 12, "Search preserves original grid and preview appearance");
        grid.SelectedIndex = 1;
        Require(new System.Windows.Documents.TextRange(code.Document.ContentStart, code.Document.ContentEnd).Text.Contains("[OrderDetails]"), "Selecting a search result updates the SQL preview");
        foreach (bool dark in new[] { true, false })
        {
            SetTheme(app, dark); window.UpdateLayout();
            CheckDatabaseAutocomplete(search);
            Require((server.Background as SolidColorBrush)?.Color == (dark ? Color.FromRgb(32, 32, 32) : Colors.White), "Search dropdowns follow the host theme");
            Require(server.Style != null && server.MinHeight == 24, "Compact Search dropdowns override the shared minimum height");
        }
        window.Close();
    }
    private static void CheckDatabaseAutocomplete(FrameworkElement search)
    {
        var selected = Field<System.Collections.Generic.SortedSet<string>>(search, "_selected");
        selected.Add("Orders_Archive");
        var builder = search.GetType().GetMethod("BuildDatabaseMenu", Instance);
        Require(builder != null, "Database dropdown exposes its filterable choices independently of server loading");
        var menu = (ContextMenu)builder.Invoke(search, new object[] { new[] { "Orders", "Orders_Archive", "Customers" } });
        var input = menu.Items.OfType<MenuItem>().Select(item => item.Header).OfType<TextBox>().Single();
        menu.IsOpen = true;
        PumpUntil(() => input.IsKeyboardFocusWithin);
        var choices = menu.Items.OfType<MenuItem>().Where(item => item.Tag is string).ToArray();
        menu.UpdateLayout();
        Require(choices.All(item => item.ActualHeight == 24 && item.FontSize == 13),
            "Open dropdown choices use option B: 24-DIP rows with unchanged text size");
        Require(input.ActualHeight == 32, "Compact choice rows preserve the database filter input height");
        var surface = menu.Template.FindName("DropdownSurface", menu) as Border;
        Require(surface != null && surface.CornerRadius.TopLeft == 8 && surface.Effect != null,
            "Database suggestions use the reference's rounded surface and soft shadow");
        var checkedItem = choices.Single(item => (string)item.Tag == "Orders_Archive");
        var indicator = checkedItem.Template.FindName("SelectionIndicator", checkedItem) as Border;
        Require(indicator != null && indicator.Visibility == Visibility.Visible && indicator.Width == 3,
            "Checked databases have a slim selection indicator on the left");
        var unselectedItem = choices.Single(item => (string)item.Tag == "Customers");
        unselectedItem.Focus();
        Require(((Border)unselectedItem.Template.FindName("SelectionFill", unselectedItem)).Opacity == 0.08 &&
            ((Border)unselectedItem.Template.FindName("SelectionIndicator", unselectedItem)).Visibility == Visibility.Collapsed,
            "Keyboard focus highlights a row without marking it as selected");
        input.Focus();
        string capturePath = Environment.GetEnvironmentVariable("SQLASSIST_TEST_DROPDOWN_CAPTURE");
        if (!string.IsNullOrEmpty(capturePath))
        {
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth * 2),
                (int)Math.Ceiling(menu.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(menu);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            bool dark = ((SolidColorBrush)menu.Background).Color.R < 128;
            string themedPath = Path.Combine(Path.GetDirectoryName(capturePath),
                Path.GetFileNameWithoutExtension(capturePath) + (dark ? "-dark" : "-light") + ".png");
            using (var file = File.Create(themedPath)) encoder.Save(file);
        }
        input.Text = "ARCH";
        Require(choices.Single(item => (string)item.Tag == "Orders_Archive").Visibility == Visibility.Visible &&
            choices.Single(item => (string)item.Tag == "Customers").Visibility == Visibility.Collapsed, "Database autocomplete filters by a case-insensitive substring");
        Require(selected.Contains("Orders_Archive") && choices.Single(item => (string)item.Tag == "Orders_Archive").IsChecked, "Filtering preserves checked databases");
        input.Text = "no matching database";
        Require(choices.All(item => item.Visibility == Visibility.Collapsed) && menu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "ไม่พบฐานข้อมูล") && item.Visibility == Visibility.Visible), "No-match autocomplete gives feedback");
        input.Clear();
        Require(choices.All(item => item.Visibility == Visibility.Visible), "Clearing autocomplete restores all databases");
        menu.IsOpen = false;
    }
    private static void CheckOptionsAppearance(Application app)
    {
        var options = (Window)Activator.CreateInstance(Extension.GetType("SqlAssist.Settings.OptionsWindow"), true);
        Require((options.Background as SolidColorBrush)?.Color == Color.FromRgb(32, 32, 32), "Options follows the host dark theme rather than using a fixed white surface");
        int selected = Field<ComboBox>(options, "_keywordCase").SelectedIndex;
        SetTheme(app, false);
        Require((options.Background as SolidColorBrush)?.Color == Colors.White, "Options responds to a host theme change");
        Require(Field<ComboBox>(options, "_keywordCase").SelectedIndex == selected, "Theme changes preserve formatting choices");
        options.Show();
        var tabs = AllElements<TabControl>(options).Single();
        Require(tabs.Items.Count == 5, "All five Options pages remain available");
        tabs.SelectedIndex = 0; options.UpdateLayout();
        var keywordControl = Field<ComboBox>(options, "_keywordCase");
        var previewControl = AllElements<FrameworkElement>(options).SingleOrDefault(element => element.Name == "SqlFormatPreview");
        Require(previewControl != null, "Formatting has a dedicated preview region");
        var keywordBounds = keywordControl.TransformToAncestor(options).TransformBounds(new Rect(keywordControl.RenderSize));
        var previewBounds = previewControl.TransformToAncestor(options).TransformBounds(new Rect(previewControl.RenderSize));
        Require(keywordBounds.Right < previewBounds.Left && previewBounds.Height > 250, "Settings and SQL preview occupy separate side-by-side regions");
        Require(!AllElements<TextBlock>(options).Any(text => text.Text.Contains("การตั้งค่า › SqlAssist")), "Options removes the redundant breadcrumb");
        var richPreview = Field<RichTextBox>(options, "_preview");
        var lineNumbers = Field<RichTextBox>(options, "_lineNumbers");
        var codeStart = richPreview.Document.ContentStart.GetNextInsertionPosition(System.Windows.Documents.LogicalDirection.Forward);
        var numberStart = lineNumbers.Document.ContentStart.GetNextInsertionPosition(System.Windows.Documents.LogicalDirection.Forward);
        Require(Math.Abs(codeStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward).Top -
            numberStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward).Top) < 1, "Line numbers align with the SQL text baseline");
        keywordControl.SelectedIndex = 1;
        Require(new System.Windows.Documents.TextRange(richPreview.Document.ContentStart, richPreview.Document.ContentEnd).Text.Contains("select"), "Preview reacts to keyword case changes");
        keywordControl.SelectedIndex = selected;
        CheckSettingsHover(options, tabs, app);
        foreach (double width in new[] { 560.0, 760.0 })
        {
            options.Width = width; options.Height = 520;
            PumpUntil(() => Math.Abs(options.ActualWidth - width) < 2);
            foreach (TabItem tab in tabs.Items)
            {
                tab.IsSelected = true; options.UpdateLayout();
                Require(((FrameworkElement)tab.Content).ActualWidth > 0, "Every Options page renders at minimum window size");
            }
            tabs.SelectedIndex = 0; options.UpdateLayout();
            var scroller = AllElements<ScrollViewer>((DependencyObject)((TabItem)tabs.Items[0]).Content).First();
            Require(scroller.ScrollableHeight > 0, "Long settings remain scrollable at the minimum height");
            scroller.ScrollToEnd(); PumpUntil(() => scroller.VerticalOffset > 0);
            scroller.ScrollToHome(); options.UpdateLayout();
            var keyword = Field<ComboBox>(options, "_keywordCase");
            Require(keyword.TransformToAncestor(options).TransformBounds(new Rect(keyword.RenderSize)).Right <= options.ActualWidth, "Formatting controls fit the minimum window width");
            var saveButton = AllButtons(options).Single(button => button.Content as string == "บันทึก");
            var saveBounds = saveButton.TransformToAncestor(options).TransformBounds(new Rect(saveButton.RenderSize));
            Require(saveBounds.Bottom < options.ActualHeight && saveBounds.Top > 0, "Save stays visible at minimum window size");
        }
        options.Close();
        var api = (Window)Activator.CreateInstance(Extension.GetType("SqlAssist.Assistant.AssistantSettingsWindow"), true);
        api.Show();
        CheckSettingsHover(api, Field<TabControl>(api, "_tabs"), app);
        api.Close();
        CheckOptionsSaveAndCancel();
        SetTheme(app, true);
    }
    private static void CheckSettingsHover(Window options, TabControl tabs, Application app)
    {
        // Exercise the hover trigger without moving the user's pointer in this standalone process.
        var mouseOverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (bool dark in new[] { true, false })
        {
            SetTheme(app, dark);
            foreach (TabItem tab in tabs.Items)
            {
                tab.IsSelected = true; options.UpdateLayout();
                var texts = AllElements<TextBlock>((DependencyObject)tab.Content).ToArray();
                Require(texts.Length > 0, "Hover regression checks rendered settings text");
                var appearances = texts.Select(text => new { text.Foreground, text.FontFamily, text.FontSize, text.FontWeight }).ToArray();
                tab.SetValue(mouseOverKey, true); options.UpdateLayout();
                Require(texts.Select((text, i) => Equals(text.Foreground, appearances[i].Foreground) &&
                    Equals(text.FontFamily, appearances[i].FontFamily) && text.FontSize == appearances[i].FontSize &&
                    text.FontWeight == appearances[i].FontWeight).All(stable => stable), "Hovering a settings tab preserves page text color and font");
                tab.SetValue(mouseOverKey, false);
            }
        }
    }
    private static void CheckOptionsSaveAndCancel()
    {
        var provider = Extension.GetType("SqlAssist.Settings.SettingsProvider");
        var load = provider.GetMethod("Load", BindingFlags.Static | BindingFlags.Public);
        var save = provider.GetMethod("Save", BindingFlags.Static | BindingFlags.Public);
        var before = (SqlAssist.Core.Settings.SqlAssistSettings)load.Invoke(null, null);
        var cancel = (Window)Activator.CreateInstance(Extension.GetType("SqlAssist.Settings.OptionsWindow"), true);
        cancel.Loaded += (s, e) =>
        {
            Field<ComboBox>(cancel, "_keywordCase").SelectedIndex = 1;
            Field<ComboBox>(cancel, "_indent").SelectedItem = 2;
            AllButtons(cancel).Single(button => button.Content as string == "ยกเลิก").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        Require(cancel.ShowDialog() == false, "Cancel closes the Options dialog");
        Require(((SqlAssist.Core.Settings.SqlAssistSettings)load.Invoke(null, null)).ToXml() == before.ToXml(), "Cancel does not persist changed controls");
        var confirm = (Window)Activator.CreateInstance(Extension.GetType("SqlAssist.Settings.OptionsWindow"), true);
        confirm.Loaded += (s, e) =>
        {
            Field<ComboBox>(confirm, "_keywordCase").SelectedIndex = 1;
            Field<ComboBox>(confirm, "_indent").SelectedItem = 2;
            Field<RadioButton>(confirm, "_trailing").IsChecked = true;
            Field<CheckBox>(confirm, "_warn").IsChecked = false;
            Field<RadioButton>(confirm, "_identitySkip").IsChecked = true;
            AllButtons(confirm).Single(button => button.Content as string == "บันทึก").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        Require(confirm.ShowDialog() == true, "Save closes the Options dialog successfully");
        var after = (SqlAssist.Core.Settings.SqlAssistSettings)load.Invoke(null, null);
        Require(after.Format.KeywordCase == SqlAssist.Core.Formatting.KeywordCase.Lower && after.Format.IndentSize == 2 &&
            after.Format.CommaStyle == SqlAssist.Core.Formatting.CommaStyle.Trailing && after.Results.InsertIdentity == SqlAssist.Core.Results.InsertIdentityMode.Skip,
            "Save persists selections from multiple Options tabs");
        save.Invoke(null, new object[] { before });
    }
    private static void CheckChatCancellation(UserControl control, ComboBox provider, ComboBox model, TextBox input, ChatSession session)
    {
        var shared = typeof(AiProviderFactory).GetField("Shared", BindingFlags.Static | BindingFlags.NonPublic);
        var previousClient = (HttpClient)shared.GetValue(null);
        string previousKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        using (var handler = new StreamingFixture())
        using (var client = new HttpClient(handler))
        {
            shared.SetValue(null, client);
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "test-only-key");
            try
            {
                session.NewChat(); provider.SelectedItem = AiProviderKind.OpenRouter; model.Text = "fixture-model"; input.Text = "first";
                var first = (Task)control.GetType().GetMethod("SendAsync", Instance).Invoke(control, null);
                try { PumpUntil(() => session.Messages.LastOrDefault()?.Text == "partial"); }
                catch { Console.Error.WriteLine("UI diagnostic: task=" + first.Status + "; requests=" + handler.Requests + "; busy=" + session.IsBusy + "; status=" + Field<TextBlock>(control, "_status").Text); throw; }
                Require(session.IsBusy && !Field<Button>(control, "_send").IsEnabled, "Send disabled while streaming");
                Require(Field<Button>(control, "_stop").Visibility == Visibility.Visible && Field<Button>(control, "_send").Visibility == Visibility.Collapsed, "Stop replaces Send while generating");
                var duplicate = (Task)control.GetType().GetMethod("SendAsync", Instance).Invoke(control, null);
                Require(duplicate.IsCompleted && handler.Requests == 1, "Duplicate send issues no request");
                Field<Button>(control, "_stop").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(!session.IsBusy && session.Messages.Last().State == AiMessageState.Stopped && session.Messages.Last().Text == "partial", "Stop immediately preserves partial response");
                PumpUntil(() => first.IsCompleted);
                input.Text = "second";
                var second = (Task)control.GetType().GetMethod("SendAsync", Instance).Invoke(control, null);
                PumpUntil(() => session.IsBusy && session.Messages.LastOrDefault()?.Text == "partial");
                AllButtons(control).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "New Chat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => second.IsCompleted);
                Require(session.Messages.Count == 0 && !session.IsBusy, "Old network cleanup cannot repopulate New Chat");
            }
            finally { shared.SetValue(null, previousClient); Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", previousKey); }
        }
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (s, e) => { if (condition() || DateTime.UtcNow > deadline) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Require(condition(), "Asynchronous WPF action completed within five seconds");
    }
    private static System.Collections.Generic.IEnumerable<Button> AllButtons(DependencyObject parent)
    {
        if (parent is Button button) yield return button;
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
            if (child is DependencyObject dependency)
                foreach (var descendant in AllButtons(dependency)) yield return descendant;
    }
    private static System.Collections.Generic.IEnumerable<TextBlock> AllText(DependencyObject parent)
    {
        if (parent is TextBlock text) yield return text;
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
            if (child is DependencyObject dependency)
                foreach (var descendant in AllText(dependency)) yield return descendant;
    }
    private static System.Collections.Generic.IEnumerable<T> AllElements<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T element) yield return element;
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
            if (child is DependencyObject dependency)
                foreach (var descendant in AllElements<T>(dependency)) yield return descendant;
    }
}
