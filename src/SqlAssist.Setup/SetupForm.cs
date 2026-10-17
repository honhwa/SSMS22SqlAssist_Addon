using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SqlAssist.Setup
{
    /// <summary>The setup window: shows what is installed, offers Install / Update / Uninstall, and keeps the action
    /// button disabled while SSMS is open (checked every second, so it unlocks by itself once SSMS closes).</summary>
    internal sealed class SetupForm : Form
    {
        private static readonly Color Accent = Color.FromArgb(0, 122, 204);
        private static readonly Color WarningBack = Color.FromArgb(255, 244, 206);
        private static readonly Color WarningText = Color.FromArgb(122, 80, 0);
        private static readonly Color Success = Color.FromArgb(16, 124, 16);
        private static readonly Color Error = Color.FromArgb(196, 43, 28);

        private readonly string _ide;
        private string _installed;
        private int? _compare;
        private bool _busy;
        private bool _done;

        private readonly Label _installedValue = ValueLabel();
        private readonly Label _setupValue = ValueLabel();
        private readonly Label _statusValue = ValueLabel();
        private readonly GroupBox _choices = new GroupBox { Text = "ต้องการทำอะไร", Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 6), AutoSize = true };
        private readonly RadioButton _updateOption = new RadioButton { AutoSize = true, Checked = true, Margin = new Padding(3, 4, 3, 4) };
        private readonly RadioButton _uninstallOption = new RadioButton { AutoSize = true, Text = "ถอนการติดตั้ง SqlAssist", Margin = new Padding(3, 4, 3, 4) };
        private readonly Panel _ssmsWarning = new Panel { Dock = DockStyle.Fill, BackColor = WarningBack, Padding = new Padding(10, 8, 10, 8), AutoSize = true, Visible = false };
        // No width limit: the lines are broken explicitly in RefreshState.
        private readonly Label _ssmsWarningText = new Label { AutoSize = true, ForeColor = WarningText };
        private readonly ProgressBar _progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous, Height = 6, Visible = false };
        private readonly System.Diagnostics.Stopwatch _elapsed = new System.Diagnostics.Stopwatch();
        private SetupEngine.Step _step;
        private readonly Label _message = new Label { AutoSize = true, MaximumSize = new Size(490, 0), Margin = new Padding(3, 6, 3, 3) };
        private readonly Button _action = new Button { AutoSize = true, MinimumSize = new Size(130, 32), Padding = new Padding(8, 0, 8, 0) };
        private readonly Button _close = new Button { Text = "ปิด", AutoSize = true, MinimumSize = new Size(90, 32) };
        private readonly Timer _ssmsTimer = new Timer { Interval = 1000 };

        public SetupForm()
        {
            Text = "SqlAssist Setup " + SetupEngine.SetupVersion;
            Font = new Font("Segoe UI", 9.75f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.White;
            Padding = new Padding(18);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* default icon */ }

            _ide = SetupEngine.FindSsmsIde();
            BuildLayout();

            _action.Click += async (s, e) => await RunAsync();
            _close.Click += (s, e) => Close();
            _updateOption.CheckedChanged += (s, e) => RefreshState();
            _ssmsTimer.Tick += (s, e) => RefreshState();
            FormClosing += (s, e) => { if (_busy) e.Cancel = true; };

            LoadInstalledState();
            _ssmsTimer.Start();
        }

        private void BuildLayout()
        {
            var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill, Width = 500 };
            layout.Controls.Add(Header());
            layout.Controls.Add(Separator());
            layout.Controls.Add(InfoGrid());

            var options = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            options.Controls.Add(_updateOption);
            options.Controls.Add(_uninstallOption);
            _choices.Controls.Add(options);
            _choices.Margin = new Padding(3, 10, 3, 3);
            layout.Controls.Add(_choices);

            _ssmsWarning.Controls.Add(_ssmsWarningText);
            _ssmsWarning.Margin = new Padding(3, 10, 3, 3);
            layout.Controls.Add(_ssmsWarning);

            _progress.Margin = new Padding(3, 12, 3, 3);
            layout.Controls.Add(_progress);
            layout.Controls.Add(_message);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0) };
            buttons.Controls.Add(_close);
            buttons.Controls.Add(_action);
            layout.Controls.Add(buttons);

            AcceptButton = _action;
            CancelButton = _close;
            Controls.Add(layout);
        }

        private Control Header()
        {
            var header = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            var logo = new PictureBox { Size = new Size(56, 56), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 12, 0) };
            try
            {
                using (var s = typeof(SetupForm).Assembly.GetManifestResourceStream("icon.png"))
                    if (s != null) logo.Image = Image.FromStream(s);
            }
            catch { /* no logo is fine */ }
            header.Controls.Add(logo, 0, 0);
            header.SetRowSpan(logo, 2);

            var title = new Label { Text = "SqlAssist", AutoSize = true, Font = new Font("Segoe UI Semibold", 16f), ForeColor = Accent, Margin = new Padding(0, 4, 0, 0) };
            var subtitle = new Label { Text = "IntelliSense + Snippets สำหรับ SQL Server Management Studio 22", AutoSize = true, ForeColor = Color.DimGray };
            header.Controls.Add(title, 1, 0);
            header.Controls.Add(subtitle, 1, 1);
            return header;
        }

        private Control InfoGrid()
        {
            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(3, 8, 3, 0) };
            void Row(string caption, Label value)
            {
                grid.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 3, 12, 3) });
                grid.Controls.Add(value);
            }
            Row("เวอร์ชันที่ติดตั้งอยู่", _installedValue);
            Row("เวอร์ชันของตัวติดตั้งนี้", _setupValue);
            Row("สถานะ", _statusValue);
            var ssms = new Label { Text = SsmsDescription(), AutoSize = true, Margin = new Padding(0, 3, 0, 3), ForeColor = _ide == null ? Error : Color.Black };
            if (_ide != null) new ToolTip().SetToolTip(ssms, _ide);
            Row("SSMS", ssms);
            return grid;
        }

        // "พบ SSMS 22.11" rather than the install path, which wraps over several lines (the path is in the tooltip).
        private string SsmsDescription()
        {
            if (_ide == null) return "ไม่พบ SSMS 22 ในเครื่องนี้";
            try
            {
                var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(System.IO.Path.Combine(_ide, "Ssms.exe"));
                return $"พบ SSMS {v.FileMajorPart}.{v.FileMinorPart}";
            }
            catch
            {
                return "พบ SSMS 22";
            }
        }

        private void LoadInstalledState()
        {
            _installed = _ide == null ? null : SetupEngine.FindInstalledVersion(_ide);
            _compare = SetupEngine.CompareInstalled(_installed);
            string v = SetupEngine.SetupVersion;

            _installedValue.Text = _installed ?? "ยังไม่ได้ติดตั้ง";
            _setupValue.Text = v;

            if (_installed == null)
            {
                SetStatus("พร้อมติดตั้ง", Accent);
                _choices.Visible = false;
            }
            else
            {
                _choices.Visible = true;
                switch (_compare)
                {
                    case -1:
                        SetStatus("มีเวอร์ชันใหม่กว่า พร้อมอัปเดต", Accent);
                        _updateOption.Text = $"อัปเดตเป็นเวอร์ชัน {v}  (จาก {_installed})";
                        break;
                    case 0:
                        SetStatus("เป็นเวอร์ชันล่าสุดแล้ว", Success);
                        _updateOption.Text = $"ติดตั้งเวอร์ชัน {v} ซ้ำ (ซ่อมแซม)";
                        break;
                    default:
                        SetStatus("เวอร์ชันที่ติดตั้งอยู่ใหม่กว่าตัวติดตั้งนี้", WarningText);
                        _updateOption.Text = $"ติดตั้งเวอร์ชัน {v} ทับ (ย้อนกลับเวอร์ชัน)";
                        break;
                }
                _updateOption.Checked = true;
            }
            RefreshState();
        }

        private bool UninstallSelected => _installed != null && _uninstallOption.Checked;

        /// <summary>Runs every second: SSMS state, button text and whether the button can be pressed.</summary>
        private void RefreshState()
        {
            if (_done) return;
            if (_busy) ShowStep();
            int ssms = SetupEngine.RunningSsmsCount();
            _ssmsWarning.Visible = ssms > 0 && !_busy;
            _ssmsWarningText.Text =
                $"⚠  SSMS เปิดอยู่ {ssms} หน้าต่าง\n" +
                "กรุณาบันทึกงานแล้วปิด SSMS ทุกหน้าต่างก่อน\n" +
                "ปุ่มด้านล่างจะกดได้เองเมื่อปิดครบแล้ว";

            _action.Text = _installed == null ? "ติดตั้ง"
                : UninstallSelected ? "ถอนการติดตั้ง"
                : _compare == -1 ? "อัปเดต"
                : "ติดตั้งซ้ำ";
            _action.Enabled = _ide != null && ssms == 0 && !_busy;
            _choices.Enabled = !_busy;
        }

        private async Task RunAsync()
        {
            // Checked again here: the timer may be up to a second behind.
            if (SetupEngine.RunningSsmsCount() > 0) { RefreshState(); return; }

            bool uninstall = UninstallSelected;
            if (uninstall && MessageBox.Show(this, "ต้องการถอนการติดตั้ง SqlAssist ใช่ไหม?\n(ไฟล์ snippets ของคุณจะยังเก็บไว้)",
                    "ถอนการติดตั้ง", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _busy = true;
            _step = new SetupEngine.Step(0, 1, uninstall ? "กำลังเริ่มถอนการติดตั้ง" : "กำลังเริ่มติดตั้ง");
            _progress.Value = 0;
            _progress.Visible = true;
            _message.ForeColor = Color.Black;
            _close.Enabled = false;
            _elapsed.Restart();
            RefreshState();

            // Created on the UI thread, so each report lands back here; the 1-second timer keeps the clock moving
            // during the long VSIXInstaller steps.
            var progress = new Progress<SetupEngine.Step>(step =>
            {
                _step = step;
                ShowStep();
            });

            string error;
            try
            {
                error = await Task.Run(() => uninstall ? SetupEngine.Uninstall(_ide, progress) : SetupEngine.Install(_ide, progress));
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            _elapsed.Stop();
            _busy = false;
            _progress.Visible = false;
            _close.Enabled = true;

            if (error != null)
            {
                _message.ForeColor = Error;
                _message.Text = "ไม่สำเร็จ: " + error;
                RefreshState();
                return;
            }

            _done = true;
            _ssmsTimer.Stop();
            _choices.Visible = false;
            _ssmsWarning.Visible = false;
            _action.Visible = false;
            _close.Text = "เสร็จสิ้น";
            _close.Focus();
            _message.ForeColor = Success;
            if (uninstall)
            {
                _installedValue.Text = "ยังไม่ได้ติดตั้ง";
                SetStatus("ถอนการติดตั้งแล้ว", Success);
                _message.Text = "ถอนการติดตั้ง SqlAssist เรียบร้อยแล้ว";
            }
            else
            {
                _installedValue.Text = SetupEngine.SetupVersion;
                SetStatus("ติดตั้งแล้ว", Success);
                _message.Text =
                    $"ติดตั้ง SqlAssist {SetupEngine.SetupVersion} เรียบร้อยแล้ว (ใช้เวลา {(int)_elapsed.Elapsed.TotalSeconds} วินาที)\n\n" +
                    "• เปิด SSMS แล้วตรวจสอบได้ที่ Tools › SqlAssist: About\n" +
                    "• ปิด IntelliSense ของ SSMS: Tools › Options › Text Editor › Transact-SQL › IntelliSense" +
                    (SetupEngine.DbForgeInstalled(_ide) ? "\n• พบ dbForge SQL Complete ควรถอนหรือปิดก่อน เพราะแย่งปุ่ม Tab กัน" : "");
            }
        }

        /// <summary>"ขั้นตอน 3/4: ติดตั้ง SqlAssist 1.2.6 ลงใน SSMS..." with the time so far, and the bar at the step
        /// reached. Steps 2 and 3 run SSMS's VSIXInstaller and take most of the time.</summary>
        private void ShowStep()
        {
            int total = Math.Max(_step.Total, 1);
            _progress.Maximum = total;
            // The bar shows steps started, so it is never empty once work has begun.
            _progress.Value = Math.Min(Math.Max(_step.Number, 0), total);
            string prefix = _step.Number > 0 ? $"ขั้นตอน {_step.Number}/{total}: " : "";
            _message.Text = $"{prefix}{_step.Text}...\nใช้เวลาไปแล้ว {(int)_elapsed.Elapsed.TotalSeconds} วินาที (ทั้งหมดประมาณ 10–30 วินาที)";
        }

        private void SetStatus(string text, Color color)
        {
            _statusValue.Text = text;
            _statusValue.ForeColor = color;
        }

        private static Label ValueLabel() =>
            new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 9.75f), Margin = new Padding(0, 3, 0, 3) };

        private static Control Separator() =>
            new Label { Height = 1, Dock = DockStyle.Fill, BackColor = Color.Gainsboro, Margin = new Padding(0, 12, 0, 4) };

        protected override void Dispose(bool disposing)
        {
            if (disposing) _ssmsTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
