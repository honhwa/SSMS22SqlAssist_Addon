using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SqlAssist.Setup
{
    /// <summary>Colours, fonts and shape helpers shared by the setup window's hand-drawn controls.</summary>
    internal static class Theme
    {
        public static readonly Color Navy = Color.FromArgb(14, 27, 46);
        public static readonly Color NavyActive = Color.FromArgb(31, 49, 77);
        public static readonly Color NavyText = Color.FromArgb(214, 222, 235);
        public static readonly Color NavyMuted = Color.FromArgb(128, 145, 170);
        public static readonly Color Accent = Color.FromArgb(10, 102, 194);
        public static readonly Color AccentHover = Color.FromArgb(8, 84, 162);
        public static readonly Color AccentSoft = Color.FromArgb(239, 246, 255);
        public static readonly Color Text = Color.FromArgb(17, 24, 39);
        public static readonly Color Muted = Color.FromArgb(100, 112, 128);
        public static readonly Color Border = Color.FromArgb(222, 226, 232);
        public static readonly Color BorderHover = Color.FromArgb(176, 186, 202);
        public static readonly Color Surface = Color.FromArgb(246, 248, 250);
        public static readonly Color Disabled = Color.FromArgb(226, 231, 238);
        public static readonly Color DisabledText = Color.FromArgb(146, 156, 172);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color SuccessSoft = Color.FromArgb(220, 252, 231);
        public static readonly Color SuccessText = Color.FromArgb(21, 128, 61);
        public static readonly Color Warning = Color.FromArgb(200, 110, 6);
        public static readonly Color WarningSoft = Color.FromArgb(255, 245, 222);
        public static readonly Color Error = Color.FromArgb(196, 43, 28);
        public static readonly Color ErrorSoft = Color.FromArgb(254, 234, 231);

        /// <summary>Screen DPI / 96, for laying out in 96-dpi pixels. Fonts are in points, so GDI already draws them at
        /// this DPI. Not Control.DeviceDpi: on .NET Framework without per-monitor mode it reports 96 at any scaling,
        /// which left the text at 200% inside a layout still sized for 100%.</summary>
        public static readonly float Scale = ReadScale();

        private static float ReadScale()
        {
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    return Math.Max(1f, g.DpiX / 96f);
            }
            catch
            {
                return 1f;
            }
        }

        // Segoe UI has no Thai glyphs; GDI text (TextRenderer, Label) falls back to Leelawadee UI for them.
        public static Font Ui(float size, FontStyle style = FontStyle.Regular) => new Font("Segoe UI", size, style);
        public static Font Mono(float size, FontStyle style = FontStyle.Regular) => new Font("Consolas", size, style);

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
        {
            using (var path = RoundRect(r, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static void DrawRound(Graphics g, Color color, float width, RectangleF r, float radius)
        {
            // Inset by half the pen so the whole stroke stays inside the control.
            r.Inflate(-width / 2, -width / 2);
            using (var path = RoundRect(r, radius))
            using (var pen = new Pen(color, width))
                g.DrawPath(pen, path);
        }

        /// <summary>A tick drawn inside <paramref name="box"/> (usually a circle's bounds).</summary>
        public static void DrawCheck(Graphics g, Color color, RectangleF box, float width)
        {
            using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[]
                {
                    new PointF(box.X + box.Width * 0.28f, box.Y + box.Height * 0.52f),
                    new PointF(box.X + box.Width * 0.44f, box.Y + box.Height * 0.67f),
                    new PointF(box.X + box.Width * 0.73f, box.Y + box.Height * 0.36f),
                });
        }

        public static void DrawCross(Graphics g, Color color, RectangleF box, float width)
        {
            using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float a = 0.33f, b = 0.67f;
                g.DrawLine(pen, box.X + box.Width * a, box.Y + box.Height * a, box.X + box.Width * b, box.Y + box.Height * b);
                g.DrawLine(pen, box.X + box.Width * b, box.Y + box.Height * a, box.X + box.Width * a, box.Y + box.Height * b);
            }
        }
    }

    /// <summary>Base for the owner-drawn controls: double-buffered, white by default, with DPI helpers.</summary>
    internal abstract class PaintedControl : Control
    {
        protected PaintedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        protected float K => Theme.Scale;
        protected float D(float v) => v * K;

        protected Graphics Prepare(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return g;
        }

        protected static void DrawRun(Graphics g, string text, Font font, Color color, float x, float y) =>
            TextRenderer.DrawText(g, text, font, new Point((int)x, (int)y), color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        protected static Size Measure(string text, Font font) =>
            TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    /// <summary>The dark left column: logo, the list of steps with their state, and a note at the bottom.</summary>
    internal sealed class Sidebar : PaintedControl
    {
        public enum StepState { Pending, Active, Done, Failed }

        private readonly Image _logo;
        private readonly Font _name = Theme.Ui(12f, FontStyle.Bold);
        private readonly Font _small = Theme.Ui(8f);
        private readonly Font _step = Theme.Ui(9f);
        private readonly Font _stepActive = Theme.Ui(9f, FontStyle.Bold);
        private readonly Font _number = Theme.Ui(7.5f, FontStyle.Bold);
        private string[] _steps = new string[0];
        private StepState[] _states = new StepState[0];
        private string _footer = "";

        public Sidebar(Image logo)
        {
            _logo = logo;
            BackColor = Theme.Navy;
        }

        public void SetSteps(params string[] steps)
        {
            _steps = steps;
            _states = new StepState[steps.Length];
            Invalidate();
        }

        /// <summary>Steps before <paramref name="current"/> are done; past the end means all are done.</summary>
        public void SetCurrent(int current, bool failed = false)
        {
            for (int i = 0; i < _states.Length; i++)
                _states[i] = i < current ? StepState.Done : i == current ? (failed ? StepState.Failed : StepState.Active) : StepState.Pending;
            Invalidate();
        }

        public string Footer
        {
            get => _footer;
            set { _footer = value ?? ""; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);

            var tile = new RectangleF(D(22), D(26), D(40), D(40));
            Theme.FillRound(g, Color.White, tile, D(9));
            if (_logo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var inner = tile;
                inner.Inflate(-D(5), -D(5));
                g.DrawImage(_logo, inner);
            }
            DrawRun(g, "SqlAssist", _name, Color.White, D(74), D(26));
            DrawRun(g, "สำหรับ SSMS 22", _small, Theme.NavyMuted, D(74), D(50));

            float y = D(104);
            for (int i = 0; i < _steps.Length; i++)
            {
                var row = new RectangleF(D(14), y, Width - D(28), D(36));
                var state = _states[i];
                if (state == StepState.Active || state == StepState.Failed) Theme.FillRound(g, Theme.NavyActive, row, D(6));

                var dot = new RectangleF(D(26), row.Y + (row.Height - D(20)) / 2, D(20), D(20));
                switch (state)
                {
                    case StepState.Done:
                        using (var b = new SolidBrush(Theme.Success)) g.FillEllipse(b, dot);
                        Theme.DrawCheck(g, Color.White, dot, D(1.8f));
                        break;
                    case StepState.Failed:
                        using (var b = new SolidBrush(Theme.Error)) g.FillEllipse(b, dot);
                        Theme.DrawCross(g, Color.White, dot, D(1.8f));
                        break;
                    case StepState.Active:
                        using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, dot);
                        DrawNumber(g, i + 1, dot, Color.White);
                        break;
                    default:
                        using (var p = new Pen(Theme.NavyMuted, D(1))) g.DrawEllipse(p, dot.X + D(0.5f), dot.Y + D(0.5f), dot.Width - D(1), dot.Height - D(1));
                        DrawNumber(g, i + 1, dot, Theme.NavyMuted);
                        break;
                }

                bool current = state == StepState.Active || state == StepState.Failed;
                var font = current ? _stepActive : _step;
                var color = current ? Color.White : state == StepState.Done ? Theme.NavyText : Theme.NavyMuted;
                var size = Measure(_steps[i], font);
                DrawRun(g, _steps[i], font, color, D(56), row.Y + (row.Height - size.Height) / 2);
                y += D(42);
            }

            var footer = new Rectangle((int)D(22), 0, Width - (int)D(36), Height - (int)D(24));
            TextRenderer.DrawText(g, _footer, _small, footer, Theme.NavyMuted,
                TextFormatFlags.Bottom | TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        private void DrawNumber(Graphics g, int n, RectangleF dot, Color color) =>
            TextRenderer.DrawText(g, n.ToString(), _number, Rectangle.Round(dot), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>A rounded button: filled blue when <see cref="Primary"/>, outlined otherwise.</summary>
    internal sealed class FlatButton : PaintedControl, IButtonControl
    {
        private bool _hover, _pressed;

        public FlatButton()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public bool Primary { get; set; }
        public DialogResult DialogResult { get; set; }

        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled && Visible) OnClick(EventArgs.Empty); }

        /// <summary>Width that fits the text, at least <paramref name="min"/> (96-dpi pixels).</summary>
        public int FitWidth(int min) => Math.Max((int)D(min), Measure(Text, CurrentFont).Width + (int)D(40));

        private Font _bold;
        private Font CurrentFont => Primary ? (_bold ?? (_bold = new Font(Font, FontStyle.Bold))) : Font;

        protected override void OnFontChanged(EventArgs e) { _bold = null; base.OnFontChanged(e); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) PerformClick();
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var r = new RectangleF(0, 0, Width, Height);
            Color fore;
            if (!Enabled)
            {
                Theme.FillRound(g, Primary ? Theme.Disabled : Color.White, r, D(6));
                if (!Primary) Theme.DrawRound(g, Theme.Border, D(1), r, D(6));
                fore = Theme.DisabledText;
            }
            else if (Primary)
            {
                Theme.FillRound(g, _pressed ? Theme.AccentHover : _hover ? Theme.AccentHover : Theme.Accent, r, D(6));
                fore = Color.White;
            }
            else
            {
                Theme.FillRound(g, _hover ? Theme.Surface : Color.White, r, D(6));
                Theme.DrawRound(g, _hover ? Theme.BorderHover : Theme.Border, D(1), r, D(6));
                fore = Theme.Text;
            }
            if (Focused && ShowFocusCues)
            {
                var ring = r;
                ring.Inflate(-D(2), -D(2));
                Theme.DrawRound(g, Primary ? Color.FromArgb(150, 255, 255, 255) : Theme.Accent, D(1), ring, D(5));
            }
            TextRenderer.DrawText(g, Text, CurrentFont, ClientRectangle, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>A radio-button card: title (with an optional blue tag after it) and a muted description line.
    /// Checking one unchecks its sibling cards.</summary>
    internal sealed class OptionCard : PaintedControl
    {
        private readonly Font _title = Theme.Ui(9.5f, FontStyle.Bold);
        private readonly Font _tag = Theme.Ui(8.5f);
        private readonly Font _desc = Theme.Ui(8.5f);
        private bool _checked, _hover;

        public OptionCard()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public string Title { get; set; }
        public string Hint { get; set; }
        public string Description { get; set; }
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                if (value && Parent != null)
                    foreach (Control c in Parent.Controls)
                        if (c is OptionCard other && other != this) other.Checked = false;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { Focus(); Checked = true; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) Checked = true;
            if ((e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) && Parent != null)
            {
                var cards = new List<OptionCard>();
                foreach (Control c in Parent.Controls)
                    if (c is OptionCard card && card.Visible) cards.Add(card);
                cards.Sort((a, b) => a.Top.CompareTo(b.Top));
                int i = cards.IndexOf(this) + (e.KeyCode == Keys.Down ? 1 : -1);
                if (i >= 0 && i < cards.Count) { cards[i].Focus(); cards[i].Checked = true; }
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var r = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(g, _checked ? Theme.AccentSoft : Color.White, r, D(8));
            Theme.DrawRound(g, _checked ? Theme.Accent : _hover || Focused ? Theme.BorderHover : Theme.Border,
                _checked ? D(1.5f) : D(1), r, D(8));

            var titleSize = Measure(Title ?? "", _title);
            float titleY = D(14);
            var ring = new RectangleF(D(18), titleY + (titleSize.Height - D(16)) / 2, D(16), D(16));
            using (var pen = new Pen(_checked ? Theme.Accent : Theme.BorderHover, D(1.5f)))
                g.DrawEllipse(pen, ring.X + D(0.75f), ring.Y + D(0.75f), ring.Width - D(1.5f), ring.Height - D(1.5f));
            if (_checked)
            {
                var dot = ring;
                dot.Inflate(-D(4.5f), -D(4.5f));
                using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, dot);
            }

            DrawRun(g, Title ?? "", _title, Theme.Text, D(44), titleY);
            if (!string.IsNullOrEmpty(Hint))
            {
                var tagSize = Measure(Hint, _tag);
                DrawRun(g, "· " + Hint, _tag, Theme.Accent, D(44) + titleSize.Width + D(6), titleY + (titleSize.Height - tagSize.Height) / 2);
            }
            DrawRun(g, Description ?? "", _desc, Theme.Muted, D(44), titleY + titleSize.Height + D(5));
        }
    }

    /// <summary>"ติดตั้งอยู่ 1.2.15 → เวอร์ชันใหม่ 1.2.17" with the SSMS check on the right.</summary>
    internal sealed class VersionCard : PaintedControl
    {
        private readonly Font _caption = Theme.Ui(8f);
        private readonly Font _value = Theme.Mono(15f, FontStyle.Bold);
        private readonly Font _status = Theme.Ui(8.5f);

        public string FromCaption { get; set; } = "ติดตั้งอยู่";
        public string FromValue { get; set; }
        public string ToCaption { get; set; } = "เวอร์ชันใหม่";
        public string ToValue { get; set; }
        public string Status { get; set; }
        public bool StatusOk { get; set; } = true;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var r = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(g, Color.FromArgb(251, 252, 253), r, D(8));
            Theme.DrawRound(g, Theme.Border, D(1), r, D(8));

            float x = D(20), captionY = D(17), valueY = D(37);
            string from = FromValue ?? "—";
            DrawRun(g, FromCaption, _caption, Theme.Muted, x, captionY);
            DrawRun(g, from, _value, FromValue == null ? Theme.Muted : Theme.Text, x, valueY);
            float fromWidth = Math.Max(Measure(FromCaption, _caption).Width, Measure(from, _value).Width);

            // The arrow sits on the value line.
            float ax = x + fromWidth + D(22), ay = valueY + Measure(from, _value).Height / 2f;
            using (var pen = new Pen(Theme.Muted, D(1.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLine(pen, ax, ay, ax + D(22), ay);
                g.DrawLines(pen, new[] { new PointF(ax + D(16), ay - D(5)), new PointF(ax + D(22), ay), new PointF(ax + D(16), ay + D(5)) });
            }

            float tx = ax + D(44);
            DrawRun(g, ToCaption, _caption, Theme.Muted, tx, captionY);
            DrawRun(g, ToValue ?? "", _value, Theme.Accent, tx, valueY);

            if (string.IsNullOrEmpty(Status)) return;
            var size = Measure(Status, _status);
            float sx = Width - D(20) - size.Width, cy = Height / 2f;
            var icon = new RectangleF(sx - D(22), cy - D(8), D(16), D(16));
            var color = StatusOk ? Theme.Success : Theme.Error;
            using (var pen = new Pen(color, D(1.4f))) g.DrawEllipse(pen, icon);
            if (StatusOk) Theme.DrawCheck(g, color, icon, D(1.5f));
            else Theme.DrawCross(g, color, icon, D(1.5f));
            DrawRun(g, Status, _status, StatusOk ? Theme.Text : Theme.Error, sx, cy - size.Height / 2f);
        }
    }

    /// <summary>A small rounded label such as "มีเวอร์ชันใหม่"; sizes itself to the text.</summary>
    internal sealed class Badge : PaintedControl
    {
        private readonly Font _font = Theme.Ui(8f, FontStyle.Bold);
        private Color _fill = Theme.SuccessSoft;

        public void Set(string text, Color fill, Color fore)
        {
            Text = text;
            _fill = fill;
            ForeColor = fore;
            var size = Measure(text, _font);
            Size = new Size(size.Width + (int)D(18), size.Height + (int)D(8));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            Theme.FillRound(g, _fill, new RectangleF(0, 0, Width, Height), Height / 2f);
            TextRenderer.DrawText(g, Text, _font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>The thin progress track under the percentage.</summary>
    internal sealed class ProgressLine : PaintedControl
    {
        private double _value;

        public double Value
        {
            get => _value;
            set { _value = Math.Max(0, Math.Min(1, value)); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            float radius = Height / 2f;
            Theme.FillRound(g, Color.FromArgb(229, 233, 239), new RectangleF(0, 0, Width, Height), radius);
            float w = (float)(Width * _value);
            if (w > 0) Theme.FillRound(g, Theme.Accent, new RectangleF(0, 0, Math.Max(w, Height), Height), radius);
        }
    }

    /// <summary>The "รายละเอียด" box: one line per thing done (✓) and the step running now (›, bold).</summary>
    internal sealed class LogBox : PaintedControl
    {
        // Not monospace: Consolas has no Thai, and its fallback renders the Thai words noticeably smaller.
        private readonly Font _font = Theme.Ui(8.75f);
        private readonly Font _bold = Theme.Ui(8.75f, FontStyle.Bold);
        private readonly List<KeyValuePair<string, bool>> _lines = new List<KeyValuePair<string, bool>>();

        public void Clear() { _lines.Clear(); Invalidate(); }

        /// <summary>Adds a line; an active line shows as the one in progress until <see cref="CompleteActive"/>.</summary>
        public void Add(string text, bool active = false)
        {
            CompleteActive();
            _lines.Add(new KeyValuePair<string, bool>(text, active));
            Invalidate();
        }

        public void CompleteActive()
        {
            for (int i = 0; i < _lines.Count; i++)
                if (_lines[i].Value) _lines[i] = new KeyValuePair<string, bool>(_lines[i].Key, false);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var r = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(g, Theme.Surface, r, D(8));
            Theme.DrawRound(g, Theme.Border, D(1), r, D(8));

            float y = D(14), lineHeight = D(21);
            foreach (var line in _lines)
            {
                var font = line.Value ? _bold : _font;
                float h = Measure(line.Key, font).Height;
                var mark = new RectangleF(D(14), y + (h - D(12)) / 2, D(12), D(12));
                if (line.Value) DrawRun(g, "›", _bold, Theme.Accent, D(16), y);
                else Theme.DrawCheck(g, Theme.Success, mark, D(1.5f));
                DrawRun(g, line.Key, font, line.Value ? Theme.Text : Theme.Muted, D(32), y);
                y += lineHeight;
            }
        }
    }

    /// <summary>A bordered note with an info or warning mark; <see cref="FitHeight"/> sizes it to its lines.</summary>
    internal sealed class NoteBox : PaintedControl
    {
        public enum Kind { Info, Warning, Plain }

        private readonly Font _font = Theme.Ui(8.75f);
        private readonly Font _mark = Theme.Ui(8f, FontStyle.Bold);
        private string[] _lines = new string[0];
        private Kind _kind;

        public void Set(Kind kind, params string[] lines)
        {
            _kind = kind;
            _lines = lines;
            FitHeight();
        }

        private float TextLeft => _kind == Kind.Plain ? D(18) : D(44);
        private int TextWidth => Math.Max(1, Width - (int)TextLeft - (int)D(18));

        private const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        public void FitHeight()
        {
            float h = D(14) * 2;
            for (int i = 0; i < _lines.Length; i++)
                h += TextRenderer.MeasureText(_lines[i], _font, new Size(TextWidth, 0), Flags).Height + (i > 0 ? D(6) : 0);
            Height = (int)Math.Ceiling(h);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var r = new RectangleF(0, 0, Width, Height);
            Color fill = _kind == Kind.Warning ? Theme.WarningSoft : Color.White;
            Color border = _kind == Kind.Warning ? Color.FromArgb(246, 214, 160) : Theme.Border;
            Theme.FillRound(g, fill, r, D(8));
            Theme.DrawRound(g, border, D(1), r, D(8));

            float y = D(14);
            if (_kind != Kind.Plain)
            {
                var color = _kind == Kind.Warning ? Theme.Warning : Theme.Accent;
                var circle = new RectangleF(D(18), y + D(1), D(15), D(15));
                using (var pen = new Pen(color, D(1.3f))) g.DrawEllipse(pen, circle);
                TextRenderer.DrawText(g, _kind == Kind.Warning ? "!" : "i", _mark, Rectangle.Round(circle), color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            foreach (var line in _lines)
            {
                int h = TextRenderer.MeasureText(line, _font, new Size(TextWidth, 0), Flags).Height;
                TextRenderer.DrawText(g, line, _font, new Rectangle((int)TextLeft, (int)y, TextWidth, h + 2), Theme.Text, Flags);
                y += h + D(6);
            }
        }
    }

    /// <summary>The big round mark on the last page: a green tick or a red cross.</summary>
    internal sealed class StatusMark : PaintedControl
    {
        public bool Ok { get; set; } = true;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(Ok ? Theme.SuccessSoft : Theme.ErrorSoft)) g.FillEllipse(b, r);
            if (Ok) Theme.DrawCheck(g, Theme.Success, r, D(3.2f));
            else Theme.DrawCross(g, Theme.Error, r, D(3.2f));
        }
    }

    /// <summary>One line of text in several styles, e.g. "เป็นเวอร์ชัน <b>1.2.17</b> แล้ว".</summary>
    internal sealed class RunsLabel : PaintedControl
    {
        private readonly List<Tuple<string, Font, Color>> _runs = new List<Tuple<string, Font, Color>>();

        public RunsLabel Clear() { _runs.Clear(); Invalidate(); return this; }

        public RunsLabel Add(string text, Font font, Color color)
        {
            _runs.Add(Tuple.Create(text, font, color));
            int w = 0, h = 0;
            foreach (var run in _runs)
            {
                var size = Measure(run.Item1, run.Item2);
                w += size.Width;
                h = Math.Max(h, size.Height);
            }
            Size = new Size(w + 2, h + 2);
            Invalidate();
            return this;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int x = 0;
            foreach (var run in _runs)
            {
                var size = Measure(run.Item1, run.Item2);
                // Bottom-align runs of different fonts so they share a baseline closely enough.
                DrawRun(e.Graphics, run.Item1, run.Item2, run.Item3, x, Height - 2 - size.Height);
                x += size.Width;
            }
        }
    }
}
