using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SqlAssist.Setup
{
    /// <summary>The setup window, in three pages: what is installed and what to do (Install / Update / Uninstall),
    /// progress while VSIXInstaller runs, and the result. The sidebar on the left tracks the steps.
    /// The action button stays disabled while SSMS is open (checked every second, so it unlocks by itself).</summary>
    internal sealed class SetupForm : Form
    {
        private const string ChangesUrl = "https://github.com/kriangdet-chuaboonjun/SSMS22SqlAssist_Addon/commits/main";

        // Layout in 96-dpi pixels; D() scales to the screen.
        private const int SidebarWidth = 230, ContentWidth = 630, WindowHeight = 580, Pad = 36, ButtonHeight = 36;
        private const int Inner = ContentWidth - 2 * Pad;
        private const int FooterTop = WindowHeight - Pad - ButtonHeight;

        private static readonly string[] InstallSteps = { "检查系统", "卸载旧版本", "安装新文件", "向 SSMS 注册" };
        private static readonly string[] InstallTitles = { "正在准备安装文件...", "正在卸载旧版本...", "正在安装新文件...", "正在向 SSMS 注册..." };
        private static readonly string[] UninstallSteps = { "检查系统", "卸载 SqlAssist", "清理 SSMS 缓存" };
        private static readonly string[] UninstallTitles = { "", "正在卸载 SqlAssist...", "正在清理 SSMS 缓存..." };

        private readonly string _ide;
        private readonly string _ssmsVersion;
        private string _installed;
        private int? _compare;
        private bool _busy, _done, _uninstalling;

        private readonly Sidebar _sidebar;
        private readonly Panel _content = new Panel { BackColor = Color.White };
        private readonly Panel _welcome = Page(), _working = Page(), _finish = Page();
        private Panel _page;

        // Page 1: welcome.
        private readonly Badge _badge = new Badge();
        private readonly Label _welcomeTitle = Title();
        private readonly Label _welcomeText = Body();
        private readonly VersionCard _versions = new VersionCard();
        private readonly Label _choicesCaption = Caption("要做什么");
        private readonly OptionCard _updateOption = new OptionCard();
        private readonly OptionCard _uninstallOption = new OptionCard
        {
            Title = "卸载 SqlAssist",
            Description = "从 SSMS 22 中移除 SqlAssist（你的代码片段文件会保留）",
        };
        private readonly NoteBox _features = new NoteBox();

        // Page 2: working.
        private readonly Label _stepCaption = Caption("");
        private readonly Label _workingTitle = Title();
        private readonly Label _workingText = Body();
        private readonly Label _percent = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 26f), ForeColor = Theme.Accent };
        private readonly Label _elapsedText = new Label { AutoSize = true, Font = Theme.Ui(8.5f), ForeColor = Theme.Muted };
        private readonly ProgressLine _bar = new ProgressLine();
        private readonly Label _detailsCaption = Caption("详细信息");
        private readonly LogBox _log = new LogBox();

        // Page 3: finished or failed.
        private readonly StatusMark _mark = new StatusMark();
        private readonly Label _finishTitle = Title();
        private readonly RunsLabel _finishLine = new RunsLabel();
        private readonly Label _finishText = Body();
        private readonly NoteBox _finishNote = new NoteBox();
        private readonly NoteBox _finishWarning = new NoteBox();
        private readonly CheckBox _openSsms = new CheckBox { Text = "关闭此窗口后启动 SSMS 22", AutoSize = true, Checked = true, Font = Theme.Ui(9f), ForeColor = Theme.Text };

        // Footer, shared by the pages.
        private readonly Label _warning = new Label { AutoSize = true, Font = Theme.Ui(8.5f), ForeColor = Theme.Warning };
        private readonly LinkLabel _changes = new LinkLabel
        {
            Text = "查看此版本的更新内容", AutoSize = true, Font = Theme.Ui(8.75f),
            LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, VisitedLinkColor = Theme.Accent,
        };
        private readonly FlatButton _primary = new FlatButton { Primary = true };
        private readonly FlatButton _secondary = new FlatButton();
        private readonly ToolTip _tips = new ToolTip();
        private Action _primaryAction, _secondaryAction;

        private readonly Timer _ssmsTimer = new Timer { Interval = 1000 };
        private readonly Timer _animTimer = new Timer { Interval = 80 };
        private readonly Stopwatch _elapsed = new Stopwatch();
        private readonly Stopwatch _stepClock = new Stopwatch();
        private SetupEngine.Step _step;
        private double _shownProgress;

        public SetupForm(bool preferUninstall = false)
        {
            Text = "SqlAssist 安装程序";
            Font = Theme.Ui(9f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.White;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* default icon */ }

            _ide = SetupEngine.FindSsmsIde();
            _ssmsVersion = SsmsVersion(_ide);
            _sidebar = new Sidebar(LoadLogo());
            BuildLayout();

            _primary.Click += (s, e) => _primaryAction?.Invoke();
            _secondary.Click += (s, e) => _secondaryAction?.Invoke();
            _updateOption.CheckedChanged += (s, e) => RefreshWelcome();
            _uninstallOption.CheckedChanged += (s, e) => RefreshWelcome();
            _changes.LinkClicked += (s, e) => OpenUrl(ChangesUrl);
            _ssmsTimer.Tick += (s, e) => RefreshWelcome();
            _animTimer.Tick += (s, e) => ShowProgress();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && !_busy) Close(); };
            FormClosing += (s, e) => { if (_busy) e.Cancel = true; };
            FormClosed += (s, e) => LaunchSsmsIfAsked();

            LoadInstalledState();
            if (preferUninstall && _installed != null) _uninstallOption.Checked = true;
        }

        private static int D(int v) => (int)Math.Round(v * Theme.Scale);

        private void BuildLayout()
        {
            SuspendLayout();
            ClientSize = new Size(D(SidebarWidth + ContentWidth), D(WindowHeight));
            _sidebar.Bounds = new Rectangle(0, 0, D(SidebarWidth), D(WindowHeight));
            _content.Bounds = new Rectangle(D(SidebarWidth), 0, D(ContentWidth), D(WindowHeight));
            Controls.Add(_content);
            Controls.Add(_sidebar);

            foreach (var page in new[] { _welcome, _working, _finish })
            {
                page.Bounds = new Rectangle(0, 0, D(ContentWidth), D(FooterTop - 8));
                _content.Controls.Add(page);
            }

            Place(_welcome, _badge, 34);
            Place(_welcome, _welcomeTitle, 62);
            Place(_welcome, _welcomeText, 100);
            Place(_welcome, _versions, 134, 84);
            Place(_welcome, _choicesCaption, 238);
            Place(_welcome, _updateOption, 262, 66);
            Place(_welcome, _uninstallOption, 338, 66);
            Place(_welcome, _features, 238, 0);

            Place(_working, _stepCaption, 34);
            Place(_working, _workingTitle, 56);
            Place(_working, _workingText, 94);
            Place(_working, _percent, 126);
            Place(_working, _elapsedText, 150);
            Place(_working, _bar, 182, 6);
            Place(_working, _detailsCaption, 208);
            Place(_working, _log, 232, 150);

            Place(_finish, _mark, 34, 60);
            _mark.Width = D(60);
            Place(_finish, _finishTitle, 112);
            Place(_finish, _finishLine, 152);
            Place(_finish, _finishText, 176);
            Place(_finish, _finishNote, 214, 0);
            Place(_finish, _finishWarning, 280, 0);
            Place(_finish, _openSsms, 300);

            foreach (var b in new[] { _primary, _secondary }) b.Font = Theme.Ui(9f);
            _content.Controls.AddRange(new Control[] { _warning, _changes, _primary, _secondary });
            foreach (Control c in new Control[] { _warning, _changes, _primary, _secondary }) c.BringToFront();

            AcceptButton = _primary;
            ResumeLayout(false);
        }

        /// <summary>Adds a control at the page's left margin; a height of 0 leaves it to the control.</summary>
        private void Place(Panel page, Control control, int top, int height = -1)
        {
            control.Location = new Point(D(Pad), D(top));
            if (height >= 0) control.Size = new Size(D(Inner), height == 0 ? control.Height : D(height));
            if (control is Label label && label.AutoSize) label.MaximumSize = new Size(D(Inner), 0);
            page.Controls.Add(control);
        }

        // ---- Page 1: welcome ----------------------------------------------------------------------------------

        private void LoadInstalledState()
        {
            _installed = _ide == null ? null : SetupEngine.FindInstalledVersion(_ide);
            _compare = SetupEngine.CompareInstalled(_installed);
            ShowWelcome();
        }

        private void ShowWelcome()
        {
            string v = SetupEngine.SetupVersion;
            _done = false;
            _sidebar.SetSteps(InstallSteps);
            _sidebar.SetCurrent(0);
            _sidebar.Footer = "IntelliSense + 代码片段\n适用于 SQL Server Management Studio";

            _versions.FromValue = _installed;
            _versions.ToValue = v;
            _versions.ToCaption = _compare == -1 || _installed == null ? "新版本" : "此版本";
            _versions.Status = _ide == null ? "未找到 SSMS 22" : "已找到 " + _ssmsVersion;
            _versions.StatusOk = _ide != null;
            _versions.Invalidate();

            bool hasChoices = _ide != null && _installed != null;
            if (_ide == null)
            {
                _badge.Set("未找到 SSMS 22", Theme.ErrorSoft, Theme.Error);
                _welcomeTitle.Text = "无法安装 SqlAssist";
                _welcomeText.Text = "此计算机上未找到 SQL Server Management Studio 22。请先安装 SSMS 22，然后重新打开此安装程序";
            }
            else if (_installed == null)
            {
                _badge.Set("全新安装", Theme.AccentSoft, Theme.Accent);
                _welcomeTitle.Text = "可以安装 SqlAssist";
                _welcomeText.Text = "为当前用户在 SSMS 22 中安装，无需管理员权限";
            }
            else
            {
                switch (_compare)
                {
                    case -1:
                        _badge.Set("有新版本", Theme.SuccessSoft, Theme.SuccessText);
                        _welcomeTitle.Text = "可以更新 SqlAssist";
                        _welcomeText.Text = "安装程序检测到本机已有旧版本，将自动替换";
                        SetUpdateOption($"更新到 {v}", "推荐", "保留你的设置和代码片段");
                        break;
                    case 0:
                        _badge.Set("已安装", Theme.AccentSoft, Theme.Accent);
                        _welcomeTitle.Text = "SqlAssist 已是最新版本";
                        _welcomeText.Text = "可重新安装以修复，或从 SSMS 22 中卸载 SqlAssist";
                        SetUpdateOption($"重新安装 {v}", "修复", "当 SqlAssist 的菜单或 IntelliSense 不工作时使用");
                        break;
                    default:
                        _badge.Set("安装程序版本较旧", Theme.WarningSoft, Theme.Warning);
                        _welcomeTitle.Text = "本机已安装更新的版本";
                        _welcomeText.Text = $"已安装的 SqlAssist {_installed} 比此安装程序 ({v}) 更新";
                        SetUpdateOption($"降级到 {v}", null, "用此版本覆盖当前版本");
                        break;
                }
                _updateOption.Checked = true;
            }

            _choicesCaption.Visible = _updateOption.Visible = _uninstallOption.Visible = hasChoices;
            _features.Visible = _ide != null && _installed == null;
            _features.Set(NoteBox.Kind.Plain,
                "•  按 Tab 把 * 展开为列名列表，并自动为表设置别名",
                "•  根据外键推荐 JOIN，并提供模糊匹配的自动补全",
                "•  代码片段，以及数据库对象搜索窗口");

            _changes.Visible = false;
            _secondary.Visible = true;
            _secondary.Text = "取消";
            _secondary.Enabled = true;
            _secondaryAction = Close;
            _primaryAction = () => { var _ = RunAsync(); };
            ShowPage(_welcome);
            RefreshWelcome();
            _ssmsTimer.Start();
        }

        private void SetUpdateOption(string title, string hint, string description)
        {
            _updateOption.Title = title;
            _updateOption.Hint = hint;
            _updateOption.Description = description;
            _updateOption.Invalidate();
        }

        private bool UninstallSelected => _installed != null && _uninstallOption.Checked;

        /// <summary>Runs every second on the first page: SSMS state, button text and whether it can be pressed.</summary>
        private void RefreshWelcome()
        {
            if (_busy || _done || _page != _welcome) return;
            int ssms = SetupEngine.RunningSsmsCount();
            _warning.Visible = ssms > 0 && _ide != null;
            _warning.Text = $"⚠  开始前请先关闭 SSMS（当前打开 {ssms} 个窗口）\n     全部关闭后按钮会自动可用";

            _primary.Text = _installed == null ? "立即安装"
                : UninstallSelected ? "卸载"
                : _compare == -1 ? "立即更新"
                : _compare == 0 ? "重新安装"
                : "覆盖安装";
            _primary.Enabled = _ide != null && ssms == 0;
            LayoutFooter();
        }

        // ---- Page 2: working ----------------------------------------------------------------------------------

        private async Task RunAsync()
        {
            // Checked again here: the timer may be up to a second behind.
            if (SetupEngine.RunningSsmsCount() > 0) { RefreshWelcome(); return; }

            bool uninstall = UninstallSelected;
            if (uninstall && MessageBox.Show(this, "确定要卸载 SqlAssist 吗？\n（你的代码片段文件会保留）",
                    "卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            BeginRun(uninstall);
            // Created on the UI thread, so each report lands back here.
            var progress = new Progress<SetupEngine.Step>(OnStep);

            string error;
            try
            {
                error = await Task.Run(() => uninstall ? SetupEngine.Uninstall(_ide, progress) : SetupEngine.Install(_ide, progress));
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (error != null) ShowFailed(error);
            else ShowFinished();
        }

        private void BeginRun(bool uninstall)
        {
            _busy = true;
            _uninstalling = uninstall;
            _ssmsTimer.Stop();
            _sidebar.SetSteps(uninstall ? UninstallSteps : InstallSteps);
            _sidebar.SetCurrent(uninstall ? 1 : 0);
            _sidebar.Footer = uninstall ? $"卸载 {_installed}"
                : _installed == null ? $"安装 {SetupEngine.SetupVersion}"
                : $"更新 {_installed} → {SetupEngine.SetupVersion}";

            _log.Clear();
            _log.Add("检测到 " + _ssmsVersion);
            if (_installed != null) _log.Add("检测到 SqlAssist " + _installed);

            _workingText.Text = "安装过程中请勿关闭此窗口或启动 SSMS";
            _step = new SetupEngine.Step(0, uninstall ? 2 : 4, "");
            _shownProgress = 0;
            ShowStepHeader();

            _warning.Visible = false;
            _secondary.Text = "取消";
            _secondary.Enabled = false;
            _primary.Text = uninstall ? "正在卸载..." : _installed == null ? "正在安装..." : "正在更新...";
            _primary.Enabled = false;
            ShowPage(_working);

            _elapsed.Restart();
            _stepClock.Restart();
            ShowProgress();
            _animTimer.Start();
        }

        private void OnStep(SetupEngine.Step step)
        {
            _step = step;
            _stepClock.Restart();
            _log.Add(step.Text + "...", active: true);
            ShowStepHeader();
            ShowProgress();
        }

        /// <summary>Install steps line up with the sidebar one to one; uninstall's sidebar has "检查系统" in front.</summary>
        private int SidebarIndex => Math.Max(_step.Number - 1, 0) + (_uninstalling ? 1 : 0);

        private void ShowStepHeader()
        {
            var steps = _uninstalling ? UninstallSteps : InstallSteps;
            var titles = _uninstalling ? UninstallTitles : InstallTitles;
            int i = Math.Min(SidebarIndex, steps.Length - 1);
            _sidebar.SetCurrent(i);
            _stepCaption.Text = $"第 {i + 1} 步，共 {steps.Length} 步";
            _workingTitle.Text = i == 1 && !_uninstalling && _installed == null ? "正在检查旧版本..." : titles[i];
        }

        /// <summary>The engine only reports step boundaries, so within a step the bar eases toward the next one
        /// (never reaching it) to show that work is still going on.</summary>
        private void ShowProgress()
        {
            int total = Math.Max(_step.Total, 1);
            double within = 0.9 * (1 - Math.Exp(-_stepClock.Elapsed.TotalSeconds / 4.0));
            double value = (Math.Max(_step.Number - 1, 0) + (_step.Number > 0 ? within : 0)) / total;
            _shownProgress = Math.Max(_shownProgress, value);
            SetProgress(_shownProgress);
        }

        private void SetProgress(double value)
        {
            _bar.Value = value;
            _percent.Text = $"{(int)Math.Round(value * 100)}%";
            _elapsedText.Text = $"已用时 {(int)_elapsed.Elapsed.TotalSeconds} 秒 · 预计共 10–30 秒";
            _elapsedText.Location = new Point(D(Pad + Inner) - _elapsedText.Width, _percent.Bottom - _elapsedText.Height - D(8));
        }

        // ---- Page 3: finished or failed -----------------------------------------------------------------------

        private void EndRun()
        {
            _animTimer.Stop();
            _elapsed.Stop();
            _busy = false;
        }

        private void ShowFinished()
        {
            EndRun();
            _done = true;
            string v = SetupEngine.SetupVersion;
            bool updated = !_uninstalling && _installed != null;
            _sidebar.SetCurrent(int.MaxValue);
            _sidebar.Footer = $"总用时 {(int)_elapsed.Elapsed.TotalSeconds} 秒";
            _log.CompleteActive();
            SetProgress(1);

            _mark.Ok = true;
            _mark.Invalidate();
            var body = Theme.Ui(9.5f);
            _finishLine.Clear();
            if (_uninstalling)
            {
                _finishTitle.Text = "卸载完成";
                _finishLine.Add("已将 SqlAssist 从 SSMS 22 中卸载", body, Theme.Text);
                _finishText.Text = @"代码片段文件仍保存在 %APPDATA%\SqlAssist，以后重新安装可直接继续使用";
            }
            else
            {
                _finishTitle.Text = updated && _compare == -1 ? "更新完成" : "安装完成";
                _finishLine.Add("SqlAssist 现在的版本是 ", body, Theme.Text)
                           .Add(v, Theme.Mono(10f, FontStyle.Bold), Theme.Text)
                           .Add("", body, Theme.Text);
                _finishText.Text = updated ? "原有设置和代码片段都保留着" : "菜单位于 SSMS 的 工具 › SqlAssist";
            }

            int y = D(214);
            bool note = !_uninstalling;
            _finishNote.Visible = note;
            if (note)
            {
                _finishNote.Top = y;
                _finishNote.Set(NoteBox.Kind.Info,
                    "重新打开 SSMS 22，然后在查询编辑器中开始输入即可使用 IntelliSense",
                    "可在 工具 › 选项 › 文本编辑器 › Transact-SQL › IntelliSense 中关闭 SSMS 自带的 IntelliSense");
                y = _finishNote.Bottom + D(10);
            }
            // Not "if (_finishWarning.Visible)": Visible reads false while the page itself is still hidden.
            bool dbForge = !_uninstalling && SetupEngine.DbForgeInstalled(_ide);
            _finishWarning.Visible = dbForge;
            if (dbForge)
            {
                _finishWarning.Top = y;
                _finishWarning.Set(NoteBox.Kind.Warning, "检测到 dbForge SQL Complete —— 建议先卸载或禁用它，因为两者会争用 Tab 键");
                y = _finishWarning.Bottom + D(10);
            }
            _openSsms.Visible = !_uninstalling;
            _openSsms.Top = y + D(8);

            _changes.Visible = !_uninstalling;
            _warning.Visible = false;
            _secondary.Visible = false;
            _primary.Text = "完成";
            _primary.Enabled = true;
            _primaryAction = Close;
            ShowPage(_finish);
            _primary.Focus();
        }

        private void ShowFailed(string error)
        {
            EndRun();
            _sidebar.SetCurrent(Math.Min(SidebarIndex, (_uninstalling ? UninstallSteps : InstallSteps).Length - 1), failed: true);
            _sidebar.Footer = $"在 {(int)_elapsed.Elapsed.TotalSeconds} 秒后中止";

            _mark.Ok = false;
            _mark.Invalidate();
            _finishTitle.Text = _uninstalling ? "卸载失败" : "安装失败";
            _finishLine.Clear().Add("失败的步骤：" + _step.Text, Theme.Ui(9.5f), Theme.Text);
            _finishText.Text = error;
            _finishNote.Visible = true;
            _finishNote.Top = _finishText.Bottom + D(16);
            _finishNote.Set(NoteBox.Kind.Warning,
                "请关闭所有 SSMS 窗口（包括任务管理器中可能残留的 Ssms.exe），然后点击“重试”");
            _finishWarning.Visible = false;
            _openSsms.Visible = false;

            _changes.Visible = false;
            _secondary.Visible = true;
            _secondary.Text = "关闭";
            _secondary.Enabled = true;
            _secondaryAction = Close;
            _primary.Text = "重试";
            _primary.Enabled = true;
            _primaryAction = LoadInstalledState;
            ShowPage(_finish);
        }

        // ---- Shared -------------------------------------------------------------------------------------------

        private void ShowPage(Panel page)
        {
            _page = page;
            foreach (var p in new[] { _welcome, _working, _finish }) p.Visible = p == page;
            LayoutFooter();
        }

        private void LayoutFooter()
        {
            int y = D(FooterTop), right = D(ContentWidth - Pad);
            foreach (var b in new[] { _primary, _secondary })
            {
                b.Size = new Size(b.FitWidth(b == _primary ? 120 : 96), D(ButtonHeight));
                b.Location = new Point(right - b.Width, y);
                right = b.Left - D(10);
            }
            _warning.Location = new Point(D(Pad), y + (D(ButtonHeight) - _warning.Height) / 2);
            _tips.SetToolTip(_warning, "请先保存工作，再关闭所有 SSMS 窗口（包括任务管理器中可能残留的 Ssms.exe）");
            _changes.Location = new Point(D(Pad), y + (D(ButtonHeight) - _changes.Height) / 2);
        }

        private void LaunchSsmsIfAsked()
        {
            if (!_done || _uninstalling || !_openSsms.Checked || _ide == null) return;
            try { Process.Start(new ProcessStartInfo(Path.Combine(_ide, "Ssms.exe")) { UseShellExecute = true }); }
            catch { /* SSMS can be opened by hand */ }
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* no browser registered */ }
        }

        // "SSMS 22.11" rather than the install path (the path is in the version card's tooltip).
        private static string SsmsVersion(string ide)
        {
            if (ide == null) return "SSMS 22";
            try
            {
                var v = FileVersionInfo.GetVersionInfo(Path.Combine(ide, "Ssms.exe"));
                return $"SSMS {v.FileMajorPart}.{v.FileMinorPart}";
            }
            catch
            {
                return "SSMS 22";
            }
        }

        private static Image LoadLogo()
        {
            try
            {
                using (var s = typeof(SetupForm).Assembly.GetManifestResourceStream("icon.png"))
                    return s == null ? null : new Bitmap(Image.FromStream(s));
            }
            catch
            {
                return null; // no logo is fine
            }
        }

        private static Panel Page() => new Panel { BackColor = Color.White, Visible = false };
        private static Label Title() => new Label { AutoSize = true, Font = Theme.Ui(15f, FontStyle.Bold), ForeColor = Theme.Text };
        private static Label Body() => new Label { AutoSize = true, Font = Theme.Ui(9.5f), ForeColor = Theme.Muted };
        private static Label Caption(string text) => new Label { AutoSize = true, Text = text, Font = Theme.Ui(8.75f), ForeColor = Theme.Muted };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _ssmsTimer.Dispose();
                _animTimer.Dispose();
                _tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
