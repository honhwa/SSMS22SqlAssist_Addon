using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using SqlAssist.Core.Assistant;
using SqlAssist.Settings;

namespace SqlAssist.Assistant
{
    internal sealed class AssistantControl : UserControl, IDisposable
    {
        private readonly ChatSession _session = new ChatSession();
        private readonly ApiKeyResolver _keys = new ApiKeyResolver(new CredentialKeyStore());
        private readonly ComboBox _provider = new ComboBox { MinWidth = 0, MinHeight = 28, Margin = new Thickness(0, 0, 6, 0), FontSize = 12 };
        private readonly ComboBox _model = new ComboBox { IsEditable = true, MinWidth = 88, MaxWidth = 240, HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 28, MaxDropDownHeight = 240, FontSize = 12 };
        private readonly StackPanel _messages = new StackPanel();
        private readonly ScrollViewer _scroll;
        private readonly TextBox _input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 88, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Padding = new Thickness(6, 10, 6, 10), FontSize = 13 };
        private readonly TextBox _attachment = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 4, 0, 4) };
        private readonly StackPanel _attachmentPanel = new StackPanel { Visibility = Visibility.Collapsed };
        private readonly TextBlock _status = AssistantUi.Note(""), _context = AssistantUi.Note("");
        private readonly Button _send, _stop, _settings;
        private readonly Border _composer;
        private readonly ContextMenu _sqlActions = new ContextMenu();
        private readonly Grid _welcome = new Grid();
        private readonly StackPanel _suggestions = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
        private readonly TextBlock _placeholder = new TextBlock { Text = "พิมพ์คำถามเกี่ยวกับ SQL…", IsHitTestVisible = false, Opacity = 0.55, Margin = new Thickness(6, 10, 6, 0) };
        private CancellationTokenSource _request;
        private AiProviderKind _kind;
        private readonly EventHandler _modelChanged;
        private bool _loading, _disposed;

        public AssistantControl()
        {
            FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            AssistantUi.Theme(this);
            var root = new Grid { Margin = new Thickness(10, 8, 10, 8) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (AiProviderKind kind in Enum.GetValues(typeof(AiProviderKind))) _provider.Items.Add(kind);
            _provider.ToolTip = "Provider"; _model.ToolTip = "Model ID — เลือกหรือกรอกเอง; โหลดรายการได้ที่ Settings";
            AutomationProperties.SetName(_provider, "AI Provider");
            AutomationProperties.SetName(_model, "AI Model");
            AutomationProperties.SetName(_input, "ข้อความถึง AI");
            AutomationProperties.SetName(_attachment, "SQL ที่เลือกแนบ");
            var newChat = AssistantUi.IconButton("New Chat", "M2,2 L18,2 L18,14 L10,14 L6,18 L6,14 L2,14 Z M10,5 L10,11 M7,8 L13,8", () =>
            { ThreadHelper.ThrowIfNotOnUIThread(); Stop(); _session.NewChat(); Render(); _status.Text = ""; UpdateContext(); });
            _sqlActions.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            _sqlActions.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            foreach (string command in new[] { "Explain SQL", "Review SQL", "Suggest Optimization" })
            {
                var item = new MenuItem { Header = command };
                item.SetResourceReference(StyleProperty, "AssistantSqlMenuItemStyle");
                item.Click += (s, e) => Prepare(command);
                _sqlActions.Items.Add(item);
            }
            var more = AssistantUi.IconButton("คำสั่ง SQL เพิ่มเติม", "M3,10 L4,10 M9,10 L10,10 M15,10 L16,10", () => { });
            foreach (var button in new[] { newChat, more })
            {
                button.SetResourceReference(StyleProperty, "AssistantHeaderIconButtonStyle");
                button.Width = 28; button.Height = 28; button.Padding = new Thickness(4);
            }
            var dots = new Canvas { Width = 20, Height = 20 };
            foreach (double left in new[] { 3.0, 9.0, 15.0 })
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 2.5, Height = 2.5 };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, EnvironmentColors.ToolWindowTextBrushKey);
                Canvas.SetLeft(dot, left); Canvas.SetTop(dot, 9); dots.Children.Add(dot);
            }
            more.Content = dots;
            var newThreadIcon = new Canvas { Width = 20, Height = 20 };
            var bubble = new System.Windows.Shapes.Path { Data = Geometry.Parse("M2,5 L16,5 L16,15 L9,15 L5,19 L5,15 L2,15 Z"), StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round };
            bubble.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, EnvironmentColors.ToolWindowTextBrushKey);
            newThreadIcon.Children.Add(bubble);
            var badge = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = Brushes.ForestGreen };
            Canvas.SetLeft(badge, 10); newThreadIcon.Children.Add(badge);
            newThreadIcon.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M15,2 L15,8 M12,5 L18,5"), Stroke = Brushes.White, StrokeThickness = 1.2 });
            newChat.Content = newThreadIcon;
            more.ContextMenu = _sqlActions;
            more.Click += (s, e) => { _sqlActions.PlacementTarget = more; _sqlActions.IsOpen = true; };
            DockPanel.SetDock(more, Dock.Right); header.Children.Add(more);
            DockPanel.SetDock(newChat, Dock.Right); header.Children.Add(newChat);
            header.Children.Add(new TextBlock { Text = "New Thread", VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
            root.Children.Add(header);

            _welcome.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _welcome.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var greeting = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(12, 0, 12, 24) };
            var databaseMark = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M2,5 C2,1 18,1 18,5 C18,9 2,9 2,5 M2,5 L2,15 C2,19 18,19 18,15 L18,5 M2,10 C2,14 18,14 18,10"),
                StrokeThickness = 1.7, Width = 40, Height = 44, Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18)
            };
            databaseMark.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, EnvironmentColors.ToolWindowTextBrushKey);
            greeting.Children.Add(databaseMark);
            greeting.Children.Add(new TextBlock { Text = "ให้ช่วยเรื่อง SQL อะไรดี?", FontSize = 25, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Center });
            greeting.Children.Add(new TextBlock { Text = "ช่วยเขียน อธิบาย ตรวจสอบ และปรับปรุง SQL", TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 6), FontSize = 12 });
            greeting.Children.Add(new TextBlock { Text = "ตรวจสอบคำตอบก่อนนำไปใช้", TextAlignment = TextAlignment.Center, Opacity = 0.6, FontSize = 11 });
            _welcome.Children.Add(greeting);
            foreach (var suggestion in new[] { new[] { "อธิบาย SQL ที่เลือก", "Explain SQL" }, new[] { "ตรวจสอบข้อผิดพลาดและความเสี่ยงของ SQL", "Review SQL" }, new[] { "เสนอแนวทางปรับประสิทธิภาพ SQL", "Suggest Optimization" } })
            {
                string action = suggestion[1];
                var button = AssistantUi.Button(suggestion[0], () => Prepare(action));
                button.Content = new TextBlock { Text = suggestion[0], TextWrapping = TextWrapping.Wrap };
                button.HorizontalAlignment = HorizontalAlignment.Right; button.FontSize = 12; button.MaxWidth = 380;
                button.Margin = new Thickness(0, 3, 0, 3); button.Padding = new Thickness(10, 5, 10, 5);
                button.SetResourceReference(StyleProperty, "AssistantSuggestionButtonStyle");
                _suggestions.Children.Add(button);
            }
            Grid.SetRow(_suggestions, 1); _welcome.Children.Add(_suggestions);
            var conversation = new Grid(); conversation.Children.Add(_welcome);
            _scroll = new ScrollViewer { Content = _messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Visibility = Visibility.Collapsed };
            conversation.Children.Add(_scroll); Grid.SetRow(conversation, 1); root.Children.Add(conversation);

            var footer = new StackPanel();
            var composerContent = new StackPanel();
            var attachmentActions = new DockPanel();
            var attach = AssistantUi.IconButton("แนบ SQL ที่เลือก", "M10,2 L10,18 M2,10 L18,10", Attach);
            DockPanel.SetDock(attach, Dock.Left); attachmentActions.Children.Add(attach);
            attachmentActions.Children.Add(new TextBlock { Text = "แนบเฉพาะ SQL ที่เลือก", FontSize = 11, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            composerContent.Children.Add(attachmentActions);
            var attachmentHeader = new DockPanel();
            var remove = AssistantUi.IconButton("ลบ SQL แนบ", "M4,4 L16,16 M16,4 L4,16", () => { _attachment.Clear(); _attachmentPanel.Visibility = Visibility.Collapsed; UpdateContext(); });
            DockPanel.SetDock(remove, Dock.Right); attachmentHeader.Children.Add(remove);
            attachmentHeader.Children.Add(new TextBlock { Text = "SQL ที่เลือกแนบ", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            _attachmentPanel.Children.Add(attachmentHeader);
            _attachmentPanel.Children.Add(_attachment);
            composerContent.Children.Add(_attachmentPanel);
            var inputArea = new Grid(); inputArea.Children.Add(_input); inputArea.Children.Add(_placeholder); composerContent.Children.Add(inputArea);
            var toolbar = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var selectors = new Grid { Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            selectors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(114) });
            selectors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            selectors.Children.Add(_provider); Grid.SetColumn(_model, 1); selectors.Children.Add(_model); toolbar.Children.Add(selectors);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            _settings = AssistantUi.IconButton("Settings / API & Keys", "M2,5 L7,5 M11,5 L18,5 M2,10 L11,10 M15,10 L18,10 M2,15 L5,15 M9,15 L18,15 M7,2 L7,8 M13,7 L13,13 M7,12 L7,18", OpenSettings);
            actions.Children.Add(_settings);
            _send = AssistantUi.IconButton("Send (Enter)", "M2,2 L18,10 L2,18 L6,10 Z M6,10 L18,10", () => { });
            _send.Click += SendClicked;
            _stop = AssistantUi.IconButton("Stop", "M4,4 L16,4 L16,16 L4,16 Z", Stop); _stop.IsEnabled = false; _stop.Visibility = Visibility.Collapsed;
            actions.Children.Add(_send); actions.Children.Add(_stop); Grid.SetColumn(actions, 1); toolbar.Children.Add(actions); composerContent.Children.Add(toolbar);
            _composer = new Border { Child = composerContent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(7) };
            _composer.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            footer.Children.Add(_composer);
            _context.FontSize = 10; _context.Opacity = 0.65; _context.Margin = new Thickness(2, 6, 2, 0); footer.Children.Add(_context);
            _status.FontSize = 11; _status.Margin = new Thickness(2, 4, 2, 0); footer.Children.Add(_status);
            Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
            LoadSelection();
            _provider.SelectionChanged += (s, e) => DestinationChanged();
            _modelChanged = (s, e) => UpdateContext();
            DependencyPropertyDescriptor.FromProperty(ComboBox.TextProperty, typeof(ComboBox)).AddValueChanged(_model, _modelChanged);
            _input.TextChanged += (s, e) => { _placeholder.Visibility = string.IsNullOrEmpty(_input.Text) ? Visibility.Visible : Visibility.Collapsed; UpdateContext(); };
            _input.PreviewKeyDown += InputKey;
            Unloaded += (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); Stop(); };
            Render(); UpdateContext();
        }
        private void LoadSelection()
        {
            _loading = true;
            try
            {
                var options = SettingsProvider.Load().Assistant;
                _kind = options.ActiveProvider; _provider.SelectedItem = _kind;
                _model.Items.Clear();
                if (!string.IsNullOrWhiteSpace(options.For(_kind).ModelId)) _model.Items.Add(options.For(_kind).ModelId);
                _model.Text = options.For(_kind).ModelId;
            }
            catch (Exception ex) { _status.Text = AssistantUi.Error(ex); }
            finally { _loading = false; }
        }
        private void DestinationChanged()
        {
            if (_loading || _provider.SelectedItem == null) return;
            _kind = (AiProviderKind)_provider.SelectedItem;
            _loading = true;
            _model.Items.Clear(); _model.Text = SettingsProvider.Load().Assistant.For(_kind).ModelId;
            _loading = false;
            _status.Text = _session.Messages.Count > 0 ? "เปลี่ยน provider แล้ว แชตเดิมยังเป็นบริบทของคำขอถัดไป" : "";
            UpdateContext();
        }
        private void OpenSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { DialogHost.ShowDialog(new AssistantSettingsWindow()); LoadSelection(); UpdateContext(); }
            catch (Exception ex) { _status.Text = AssistantUi.Error(ex); }
        }
        private void Attach()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { _attachment.Text = AssistantEditorBridge.SelectedSql(); _attachmentPanel.Visibility = Visibility.Visible; UpdateContext(); }
            catch (Exception ex) { _status.Text = AssistantUi.Error(ex); }
        }
        private void Prepare(string action)
        {
            _input.Text = action == "Explain SQL" ? "อธิบายการทำงานของ SQL ที่แนบอย่างเป็นขั้นตอน" : action == "Review SQL" ? "ตรวจสอบ SQL ที่แนบ ระบุข้อผิดพลาดและความเสี่ยง พร้อมเหตุผล" : "เสนอแนวทางปรับประสิทธิภาพ SQL ที่แนบ โดยรักษาผลลัพธ์เดิม และระบุสิ่งที่ต้องตรวจสอบเพิ่ม";
            _input.Focus(); _input.CaretIndex = _input.Text.Length;
        }
        private string Draft() => _input.Text + (_attachment.Text.Length == 0 ? "" : "\n\nSQL ที่ผู้ใช้เลือกแนบ:\n```sql\n" + _attachment.Text + "\n```");
        private void UpdateContext()
        {
            if (_loading) return;
            try
            {
                int omitted = _session.PreviewOmittedTurns(Draft());
                _context.Text = "ปลายทางถัดไป: " + _kind + " / " + _model.Text +
                    (_session.Messages.Count > 0 ? " • จะส่งบริบทแชตเดิมรวม SQL ที่เคยแนบ; ใช้ New Chat หากไม่ต้องการ" : " • ส่งเฉพาะข้อความและ SQL ที่แนบเอง") +
                    (omitted > 0 ? "\nจะละบทสนทนาเก่าสุด " + omitted + " คู่เพื่อจำกัดบริบท (แชตที่แสดงยังอยู่)" : "");
            }
            catch (ArgumentException ex) { _context.Text = ex.Message; }
            _send.IsEnabled = !_session.IsBusy && !string.IsNullOrWhiteSpace(Draft()) && Draft().Length <= ChatSession.DefaultContextLimit;
        }
        private void SendClicked(object sender, RoutedEventArgs e) { _ = SendAsync(); }
        private void InputKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            { e.Handled = true; _ = SendAsync(); }
        }
        private async Task SendAsync()
        {
            if (_disposed || _session.IsBusy || string.IsNullOrWhiteSpace(Draft())) return;
            CancellationTokenSource request = null;
            ChatTurn turn = null;
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var settings = SettingsProvider.Load();
                settings.Assistant.ActiveProvider = _kind;
                settings.Assistant.For(_kind).ModelId = _model.Text.Trim();
                if (string.IsNullOrWhiteSpace(_model.Text)) throw new AiException("model");
                var provider = AiProviderFactory.Create(_kind, settings.Assistant.For(_kind), _keys.Get(_kind));
                SettingsProvider.Save(settings);
                turn = _session.Begin(Draft());
                request = new CancellationTokenSource(); _request = request;
                _input.Clear(); _attachment.Clear(); _attachmentPanel.Visibility = Visibility.Collapsed;
                SetBusy(true); Render();
                var output = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Padding = new Thickness(0), FontSize = 13 };
                var live = _messages.Children[_messages.Children.Count - 1] as Border;
                var livePanel = live?.Child as StackPanel;
                livePanel?.Children.Add(output);
                _status.Text = "กำลังเชื่อมต่อ…";
                await Task.Run(() => provider.GenerateAsync(turn.Context, text =>
                {
                    // Progress is fire-and-forget UI work, never synchronously dispatched from the network thread.
#pragma warning disable VSTHRD001
                    if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        if (!_disposed && _session.TryAppend(turn.Id, text))
                        { output.AppendText(text); _status.Text = "กำลังตอบ…"; _scroll.ScrollToEnd(); }
                    }));
#pragma warning restore VSTHRD001
                }, request.Token));
                if (_session.Complete(turn.Id)) { Render(); _status.Text = "ตอบเสร็จแล้ว"; }
            }
            catch (OperationCanceledException) { /* Stop invalidates the turn immediately, before transport cleanup finishes. */ }
            catch (Exception ex)
            {
                if (!_disposed && (turn == null || _session.Fail(turn.Id))) { _status.Text = AssistantUi.Error(ex); Render(); }
            }
            finally
            {
                if (request != null)
                {
                    if (ReferenceEquals(_request, request)) { _request = null; if (!_disposed) SetBusy(false); }
                    request.Dispose();
                }
            }
        }
        private void SetBusy(bool busy)
        {
            _provider.IsEnabled = _model.IsEnabled = _settings.IsEnabled = !busy;
            _stop.IsEnabled = busy; _stop.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            _send.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            UpdateContext();
        }
        private void Stop()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _session.Stop();
            var request = _request; _request = null; request?.Cancel();
            if (!_disposed) { SetBusy(false); Render(); if (request != null) _status.Text = "หยุดกลางทางแล้ว เก็บข้อความที่ได้รับไว้"; }
        }
        private void Render()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _messages.Children.Clear();
            bool empty = _session.Messages.Count == 0;
            _welcome.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            _scroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            foreach (var message in _session.Messages)
            {
                var body = new StackPanel();
                body.Children.Add(new TextBlock { Text = message.Role == "user" ? "คุณ" : "Assistant" + (message.State == AiMessageState.Stopped ? " · หยุดกลางทาง" : message.State == AiMessageState.Failed ? " · ไม่สมบูรณ์" : ""), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
                if (message.State != AiMessageState.Responding)
                {
                    body.Children.Add(AssistantMarkdownView.Create(message.Text, text => _status.Text = text));
                    if (message.Role == "assistant" && message.Text.Length > 0) body.Children.Add(AssistantUi.Button("Copy answer", () => AssistantMarkdownView.Copy(message.Text, text => _status.Text = text)));
                }
                var border = new Border { Child = body, BorderThickness = new Thickness(message.Role == "user" ? 1 : 0), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Margin = new Thickness(0, 4, 0, 12) };
                border.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
                _messages.Children.Add(border);
            }
            _scroll.ScrollToEnd();
        }
        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            _disposed = true; Stop();
            DependencyPropertyDescriptor.FromProperty(ComboBox.TextProperty, typeof(ComboBox)).RemoveValueChanged(_model, _modelChanged);
        }
    }
}
