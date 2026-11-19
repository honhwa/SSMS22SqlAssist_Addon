using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using SqlAssist.Core.Assistant;
using SqlAssist.Settings;

namespace SqlAssist.Assistant
{
    internal sealed class AssistantSettingsWindow : Window
    {
        private sealed class ProviderForm
        {
            public AiProviderKind Kind;
            public ComboBox Model = new ComboBox { IsEditable = true, MinHeight = 28, MaxDropDownHeight = 260 };
            public TextBox Url = new TextBox { MinHeight = 28 };
            public PasswordBox Hidden = new PasswordBox { MinHeight = 28, MaxLength = 1280 };
            public TextBox Visible = new TextBox { MinHeight = 28, MaxLength = 1280, Visibility = Visibility.Collapsed };
            public CheckBox Streaming = new CheckBox { Content = "รับคำตอบแบบ streaming", Margin = new Thickness(0, 10, 0, 8) };
            public TextBlock Source = AssistantUi.Note("");
            public string DraftKey => Visible.Visibility == Visibility.Visible ? Visible.Text : Hidden.Password;
            public void ClearKey() { Hidden.Clear(); Visible.Clear(); Visible.Visibility = Visibility.Collapsed; Hidden.Visibility = Visibility.Visible; }
        }
        private readonly CredentialKeyStore _store = new CredentialKeyStore();
        private readonly ApiKeyResolver _keys;
        private readonly List<ProviderForm> _forms = new List<ProviderForm>();
        private readonly TabControl _tabs = new TabControl();
        private readonly TextBlock _status = AssistantUi.Note("");
        private readonly Button _save, _cancelRequest;
        private CancellationTokenSource _request;
        private bool _closed;

        public AssistantSettingsWindow()
        {
            _keys = new ApiKeyResolver(_store);
            Title = "SqlAssist: AI API & Keys";
            Width = 720; Height = 680; MinWidth = 540; MinHeight = 540;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SettingsAppearance.Apply(this);
            var root = new DockPanel { Margin = new Thickness(16) };
            var footer = new StackPanel();
            footer.Children.Add(_status);
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            _cancelRequest = AssistantUi.Button("หยุดคำขอ", () => _request?.Cancel()); _cancelRequest.IsEnabled = false;
            _save = AssistantUi.Button("บันทึกการตั้งค่า", Save);
            _save.SetResourceReference(StyleProperty, "SettingsPrimaryButton");
            var cancel = AssistantUi.Button("ปิด / ยกเลิกค่าที่ยังไม่บันทึก", Close); cancel.IsCancel = true;
            actions.Children.Add(_cancelRequest); actions.Children.Add(_save); actions.Children.Add(cancel);
            footer.Children.Add(actions);
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var heading = SettingsAppearance.Heading("API & Keys");
            DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            var intro = AssistantUi.Note("ตั้งค่า API และโมเดลแยกแต่ละ provider • ส่งเฉพาะข้อความแชตและ SQL ที่แนบเอง\nTest Connection ส่งข้อความทดสอบสั้นและอาจใช้โควตา การบันทึก settings ไม่ใช่ผลทดสอบการเชื่อมต่อ");
            DockPanel.SetDock(intro, Dock.Top); root.Children.Add(intro);
            var options = SettingsProvider.Load().Assistant;
            foreach (AiProviderKind kind in Enum.GetValues(typeof(AiProviderKind)))
            {
                var form = new ProviderForm { Kind = kind };
                form.Model.Text = options.For(kind).ModelId;
                form.Url.Text = options.For(kind).BaseUrl;
                form.Streaming.IsChecked = options.For(kind).UseStreaming;
                _forms.Add(form);
                _tabs.Items.Add(new TabItem { Header = kind.ToString(), Content = BuildForm(form) });
                RefreshSource(form);
                AutomationProperties.SetName(form.Model, kind + " Model ID");
                AutomationProperties.SetName(form.Hidden, kind + " API Key (ซ่อน)");
                AutomationProperties.SetName(form.Visible, kind + " API Key (แสดง)");
                AutomationProperties.SetName(form.Url, "Ollama Base URL");
                form.Url.TextChanged += (s, e) => Changed();
                form.Model.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((s, e) => Changed()));
                form.Streaming.Checked += (s, e) => Changed();
                form.Streaming.Unchecked += (s, e) => Changed();
            }
            _tabs.SelectedIndex = _forms.FindIndex(form => form.Kind == options.ActiveProvider);
            root.Children.Add(_tabs); Content = root;
            Closed += (s, e) => { _closed = true; _request?.Cancel(); foreach (var form in _forms) form.ClearKey(); };
        }
        private UIElement BuildForm(ProviderForm form)
        {
            var panel = new StackPanel { Margin = new Thickness(14) };
            if (form.Kind == AiProviderKind.Ollama)
            {
                panel.Children.Add(AssistantUi.Note("Base URL (เช่น http://localhost:11434 หรือ https://host/prefix)"));
                panel.Children.Add(form.Url);
                panel.Children.Add(AssistantUi.Note("ไม่ต้องมี key สำหรับ Ollama local ปกติ • รุ่นนี้ไม่รองรับ remote gateway ที่บังคับ authentication"));
            }
            else
            {
                panel.Children.Add(AssistantUi.Note("API Key ใหม่ (ใช้กับ Test/Load ได้ก่อนบันทึก)"));
                var entry = new Grid(); entry.Children.Add(form.Hidden); entry.Children.Add(form.Visible); panel.Children.Add(entry);
                var keyActions = new WrapPanel();
                var show = AssistantUi.Button("Show / Hide", () =>
                {
                    bool visible = form.Visible.Visibility == Visibility.Visible;
                    if (visible) { form.Hidden.Password = form.Visible.Text; form.Visible.Clear(); }
                    else { form.Visible.Text = form.Hidden.Password; form.Hidden.Clear(); }
                    form.Visible.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
                    form.Hidden.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                });
                keyActions.Children.Add(show);
                keyActions.Children.Add(AssistantUi.Button("Save Key", () => KeyAction(form, false)));
                keyActions.Children.Add(AssistantUi.Button("Delete Saved Key", () => KeyAction(form, true)));
                panel.Children.Add(keyActions); panel.Children.Add(form.Source);
                panel.Children.Add(AssistantUi.Note("Environment มีผลเหนือ key ที่บันทึก • Save Key / Delete Saved Key มีผลทันที แม้ปิดหน้านี้โดยไม่บันทึก settings\nไม่แสดง key เดิมกลับในช่อง และไม่เขียน key ลงไฟล์ settings"));
            }
            panel.Children.Add(AssistantUi.Note("Model ID (เลือกจากรายการ หรือกรอกเอง)"));
            panel.Children.Add(form.Model);
            var operations = new WrapPanel();
            var load = new Button { Content = "Load Models", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 8, 6, 0) };
            load.Click += (s, e) => { _ = RunAsync(form, false); };
            var test = new Button { Content = "Test Connection", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 8, 0, 0) };
            test.Click += (s, e) => { _ = RunAsync(form, true); };
            operations.Children.Add(load); operations.Children.Add(test); panel.Children.Add(operations);
            panel.Children.Add(form.Streaming);
            panel.Children.Add(AssistantUi.Note("หากโมเดลไม่รองรับ streaming ให้ปิดตัวเลือกนี้ก่อนส่ง • ไม่มีการ retry คำขออัตโนมัติ"));
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        private void RefreshSource(ProviderForm form)
        {
            try { form.Source.Text = "Key ที่มีผล: " + _keys.Source(form.Kind); }
            catch (Exception ex) { form.Source.Text = AssistantUi.Error(ex); }
        }
        private void Changed()
        {
            if (_request == null && !_closed) _status.Text = "มีการตั้งค่าที่ยังไม่บันทึก • กดบันทึกการตั้งค่าเพื่อใช้ในแชต";
        }
        private void KeyAction(ProviderForm form, bool delete)
        {
            try
            {
                if (delete) _store.Delete(form.Kind); else _store.Save(form.Kind, form.DraftKey);
                form.ClearKey(); RefreshSource(form);
                _status.Text = delete ? "ลบ key ที่บันทึกแล้ว หากมี environment key จะยังใช้งานอยู่" : "บันทึก key แล้ว ยังไม่ได้ทดสอบการเชื่อมต่อ";
            }
            catch (Exception ex) { _status.Text = AssistantUi.Error(ex); }
        }
        private AiProviderSettings Read(ProviderForm form) => new AiProviderSettings
        { ModelId = form.Model.Text.Trim(), BaseUrl = form.Url.Text.Trim(), UseStreaming = form.Streaming.IsChecked == true };
        private async Task RunAsync(ProviderForm form, bool test)
        {
            if (_request != null) return;
            var request = new CancellationTokenSource(); _request = request;
            _tabs.IsEnabled = false; _save.IsEnabled = false; _cancelRequest.IsEnabled = true;
            _status.Text = test ? "กำลังทดสอบโมเดลจริง…" : "กำลังโหลดรายชื่อโมเดล…";
            try
            {
                var settings = Read(form);
                string key = _keys.Source(form.Kind).StartsWith("Environment:", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(form.DraftKey) ? _keys.Get(form.Kind) : form.DraftKey.Trim();
                var provider = AiProviderFactory.Create(form.Kind, settings, key);
                if (test) await provider.TestConnectionAsync(request.Token);
                else
                {
                    var models = await provider.ListModelsAsync(request.Token);
                    if (!_closed && !request.IsCancellationRequested)
                    {
                        string selected = form.Model.Text;
                        form.Model.ItemsSource = models.Select(m => m.Id).ToList();
                        form.Model.Text = selected;
                        _status.Text = "โหลดได้ " + models.Count + " โมเดล เลือกโมเดลก่อน Test Connection";
                    }
                }
                if (test && !_closed && !request.IsCancellationRequested) _status.Text = "เชื่อมต่อและได้รับคำตอบจากโมเดลที่เลือกแล้ว";
            }
            catch (OperationCanceledException) { if (!_closed) _status.Text = "ยกเลิกคำขอแล้ว"; }
            catch (Exception ex) { if (!_closed) _status.Text = AssistantUi.Error(ex); }
            finally
            {
                if (ReferenceEquals(_request, request)) _request = null;
                request.Dispose();
                if (!_closed) { _tabs.IsEnabled = true; _save.IsEnabled = true; _cancelRequest.IsEnabled = false; }
            }
        }
        private void Save()
        {
            try
            {
                var settings = SettingsProvider.Load();
                foreach (var form in _forms)
                {
                    var value = Read(form);
                    if (form.Kind == AiProviderKind.Ollama) AiProviderFactory.Create(form.Kind, value, null);
                    var target = settings.Assistant.For(form.Kind);
                    target.ModelId = value.ModelId; target.BaseUrl = value.BaseUrl; target.UseStreaming = value.UseStreaming;
                }
                settings.Assistant.ActiveProvider = _forms[_tabs.SelectedIndex].Kind;
                SettingsProvider.Save(settings);
                DialogResult = true;
            }
            catch (Exception ex) { _status.Text = AssistantUi.Error(ex); }
        }
    }
}
