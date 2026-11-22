using System.Drawing.Drawing2D;
using System.Drawing.Text;
using HDRScreenMirror.Interop;
using Microsoft.Win32;

namespace HDRScreenMirror.UI;

internal static class HextaTheme
{
    public const string AutoPreference = "auto";
    public const string LightPreference = "light";
    public const string DarkPreference = "dark";
    public const float BaselineDpi = 144f;
    public const float HairlineWidth = 0.5f;

    private static string _preference = AutoPreference;
    private static bool _isDark = DetectSystemDarkMode();

    public static bool IsDark => _isDark;
    public static string Preference => _preference;

    public static void SetPreference(string? preference)
    {
        _preference = NormalizePreference(preference);
        _isDark = _preference switch
        {
            DarkPreference => true,
            LightPreference => false,
            _ => DetectSystemDarkMode()
        };
    }

    public static bool RefreshSystemTheme()
    {
        if (_preference != AutoPreference)
            return false;
        bool next = DetectSystemDarkMode();
        if (next == _isDark)
            return false;
        _isDark = next;
        return true;
    }

    public static string NormalizePreference(string? preference) => preference switch
    {
        LightPreference => LightPreference,
        DarkPreference => DarkPreference,
        _ => AutoPreference
    };

    private static bool DetectSystemDarkMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return false;
        }
    }

    // HextaUI / Zinc dynamic light & dark tokens.
    public static Color Canvas => _isDark ? Color.FromArgb(9, 9, 11) : Color.FromArgb(247, 247, 248);
    public static Color Surface => _isDark ? Color.FromArgb(17, 17, 20) : Color.FromArgb(255, 255, 255);
    public static Color MutedSurface => _isDark ? Color.FromArgb(28, 28, 33) : Color.FromArgb(244, 244, 245);
    public static Color HoverSurface => _isDark ? Color.FromArgb(39, 39, 44) : Color.FromArgb(236, 236, 239);
    public static Color Border => _isDark ? Color.FromArgb(42, 42, 48) : Color.FromArgb(228, 228, 231);
    public static Color StrongBorder => _isDark ? Color.FromArgb(63, 63, 72) : Color.FromArgb(212, 212, 216);

    public static Color Primary => _isDark ? Color.FromArgb(250, 250, 250) : Color.FromArgb(24, 24, 27);
    public static Color PrimaryHover => _isDark ? Color.FromArgb(228, 228, 231) : Color.FromArgb(39, 39, 42);
    public static Color PrimaryPressed => _isDark ? Color.FromArgb(212, 212, 216) : Color.FromArgb(9, 9, 11);
    public static Color PrimaryForeground => _isDark ? Color.FromArgb(9, 9, 11) : Color.FromArgb(250, 250, 250);

    public static Color TextPrimary => _isDark ? Color.FromArgb(248, 248, 250) : Color.FromArgb(9, 9, 11);
    public static Color TextSecondary => _isDark ? Color.FromArgb(161, 161, 170) : Color.FromArgb(82, 82, 91);
    public static Color TextMuted => _isDark ? Color.FromArgb(113, 113, 122) : Color.FromArgb(113, 113, 122);
    public static Color TextDisabled => _isDark ? Color.FromArgb(82, 82, 91) : Color.FromArgb(161, 161, 170);

    public static Color Danger => _isDark ? Color.FromArgb(248, 113, 113) : Color.FromArgb(220, 38, 38);
    public static Color DangerHover => _isDark ? Color.FromArgb(44, 20, 22) : Color.FromArgb(254, 242, 242);
    public static Color DangerPressed => _isDark ? Color.FromArgb(62, 24, 27) : Color.FromArgb(254, 226, 226);
    public static Color DangerBorder => _isDark ? Color.FromArgb(127, 29, 29) : Color.FromArgb(252, 165, 165);
    public static Color Success => _isDark ? Color.FromArgb(74, 222, 128) : Color.FromArgb(22, 163, 74);
    public static Color Warning => _isDark ? Color.FromArgb(251, 191, 36) : Color.FromArgb(217, 119, 6);

    // Strictly 3 typography sizes across the application (min 12px at 100% DPI -> 9pt / 10pt / 12pt).
    public static Font CreateSmallFont(FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", 9f, style, GraphicsUnit.Point);

    public static Font CreateBodyFont(FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", 10f, style, GraphicsUnit.Point);

    public static Font CreateTitleFont(FontStyle style = FontStyle.Bold) =>
        new("Microsoft YaHei UI", 12f, style, GraphicsUnit.Point);

    public static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
    {
        float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        GraphicsPath path = new();
        if (diameter <= 0.1f)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static float ScaleFactor(Control control) =>
        Math.Max(0.5f, control.DeviceDpi / BaselineDpi);
}

internal enum HextaButtonVariant
{
    Primary,
    Secondary,
    Ghost,
    Danger
}

internal static class HextaHoverTip
{
    private static TipWindow? _window;
    private static bool _tipVisible;
    private static System.Windows.Forms.Timer? _delayTimer;
    private static Control? _pendingOwner;
    private static string? _pendingText;
    private static Point _pendingScreenAnchor;

    public static void Schedule(Control owner, string text, Point screenAnchor)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Hide();
            return;
        }

        if (_tipVisible && _window is not null && !_window.IsDisposed &&
            ReferenceEquals(_pendingOwner, owner) && string.Equals(_pendingText, text, StringComparison.Ordinal))
        {
            return;
        }

        _pendingOwner = owner;
        _pendingText = text;
        _pendingScreenAnchor = screenAnchor;

        _delayTimer ??= CreateTimer();
        _delayTimer.Stop();
        _delayTimer.Start();
    }

    public static void Hide()
    {
        _delayTimer?.Stop();
        _pendingOwner = null;
        _pendingText = null;
        _tipVisible = false;
        if (_window is not null && !_window.IsDisposed && _window.IsHandleCreated)
            NativeMethods.HideWindow(_window.Handle);
    }

    private static System.Windows.Forms.Timer CreateTimer()
    {
        System.Windows.Forms.Timer timer = new() { Interval = 280 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_pendingOwner is null || _pendingOwner.IsDisposed || !_pendingOwner.Visible ||
                string.IsNullOrWhiteSpace(_pendingText))
            {
                return;
            }

            ShowNow(_pendingOwner, _pendingText, _pendingScreenAnchor);
        };
        return timer;
    }

    private static void ShowNow(Control owner, string text, Point screenAnchor)
    {
        if (_window is null || _window.IsDisposed)
            _window = new TipWindow();

        _window.ShowTip(owner, text, screenAnchor);
        _tipVisible = true;
    }

    private sealed class TipWindow : Form
    {
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;

        private string _text = string.Empty;
        private readonly Font _font = HextaTheme.CreateSmallFont();

        public TipWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WsExToolWindow | WsExNoActivate | WsExTransparent;
                return cp;
            }
        }

        public void ShowTip(Control owner, string text, Point screenAnchor)
        {
            _text = text;
            float scale = HextaTheme.ScaleFactor(owner);
            int padX = (int)Math.Round(12f * scale);
            int padY = (int)Math.Round(7f * scale);
            Size textSize;
            using (Graphics g = CreateGraphics())
            {
                textSize = TextRenderer.MeasureText(g, text, _font, new Size(900, 100), TextFormatFlags.SingleLine);
            }

            int width = textSize.Width + padX * 2;
            int height = textSize.Height + padY * 2;
            Rectangle work = Screen.FromPoint(screenAnchor).WorkingArea;
            int x = Math.Clamp(screenAnchor.X, work.Left + 8, Math.Max(work.Left + 8, work.Right - width - 8));
            int y = screenAnchor.Y - height - (int)Math.Round(8f * scale);
            if (y < work.Top + 8)
                y = screenAnchor.Y + (int)Math.Round(24f * scale);
            y = Math.Clamp(y, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - height - 8));

            Bounds = new Rectangle(x, y, width, height);
            using (GraphicsPath path = HextaTheme.CreateRoundedPath(new RectangleF(0, 0, width, height), 7f * scale))
            {
                Region?.Dispose();
                Region = new Region(path);
            }
            if (!IsHandleCreated)
                CreateHandle();
            NativeMethods.ShowTopMostNoActivate(Handle, Bounds);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float scale = HextaTheme.ScaleFactor(this);
            Color bg = HextaTheme.IsDark ? Color.FromArgb(244, 244, 245) : Color.FromArgb(24, 24, 27);
            Color fg = HextaTheme.IsDark ? Color.FromArgb(9, 9, 11) : Color.FromArgb(250, 250, 250);
            Color border = HextaTheme.IsDark ? Color.FromArgb(60, 0, 0, 0) : Color.FromArgb(50, 255, 255, 255);

            RectangleF rect = new(0.25f, 0.25f, Width - 0.5f, Height - 0.5f);
            using GraphicsPath path = HextaTheme.CreateRoundedPath(rect, 7f * scale);
            using SolidBrush fill = new(bg);
            using Pen borderPen = new(border, HextaTheme.HairlineWidth);
            graphics.FillPath(fill, path);
            graphics.DrawPath(borderPen, path);

            TextRenderer.DrawText(
                graphics,
                _text,
                _font,
                ClientRectangle,
                fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _font.Dispose();
            base.Dispose(disposing);
        }
    }
}

internal sealed class HextaCard : Panel
{
    public float CornerRadius { get; set; } = 12f;

    public HextaCard()
    {
        DoubleBuffered = true;
        BackColor = HextaTheme.Surface;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        if (Width <= 2 || Height <= 2)
            return;

        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float scale = HextaTheme.ScaleFactor(this);
        float radius = CornerRadius * scale;
        RectangleF bounds = new(0.25f, 0.25f, Width - 0.5f, Height - 0.5f);

        using SolidBrush parentBrush = new(Parent?.BackColor ?? HextaTheme.Canvas);
        graphics.FillRectangle(parentBrush, ClientRectangle);

        using GraphicsPath path = HextaTheme.CreateRoundedPath(bounds, radius);
        using SolidBrush fill = new(HextaTheme.Surface);
        using Pen border = new(HextaTheme.Border, HextaTheme.HairlineWidth);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
    }
}

internal sealed class HextaSeparator : Control
{
    public HextaSeparator()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        TabStop = false;
        Height = 1;
        Dock = DockStyle.Top;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        float y = Height * 0.5f;
        using Pen pen = new(HextaTheme.Border, HextaTheme.HairlineWidth);
        graphics.DrawLine(pen, 0, y, Width, y);
    }
}

internal sealed class HextaButton : Control
{
    private bool _hovered;
    private bool _pressed;
    private HextaButtonVariant _variant = HextaButtonVariant.Secondary;

    public HextaButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable |
            ControlStyles.StandardClick |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Font = HextaTheme.CreateSmallFont();
        Cursor = Cursors.Hand;
        Size = new Size(108, 36);
    }

    public HextaButtonVariant Variant
    {
        get => _variant;
        set
        {
            if (_variant == value)
                return;
            _variant = value;
            Invalidate();
        }
    }

    public float CornerRadius { get; set; } = 7f;
    public DialogResult DialogResult { get; set; } = DialogResult.None;

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        if (!Enabled)
        {
            _hovered = false;
            _pressed = false;
        }
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Enabled)
        {
            _pressed = true;
            Focus();
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_pressed)
        {
            _pressed = false;
            Invalidate();
            if (ClientRectangle.Contains(e.Location) && DialogResult != DialogResult.None && FindForm() is Form form)
                form.DialogResult = DialogResult;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
        {
            e.Handled = true;
            OnClick(EventArgs.Empty);
            if (DialogResult != DialogResult.None && FindForm() is Form form)
                form.DialogResult = DialogResult;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        if (Width <= 2 || Height <= 2)
            return;

        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        (Color bg, Color fg, Color? border) = ResolveColors();
        float scale = HextaTheme.ScaleFactor(this);
        RectangleF bounds = new(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = HextaTheme.CreateRoundedPath(bounds, CornerRadius * scale);

        if (bg != Color.Transparent)
        {
            using SolidBrush fill = new(bg);
            graphics.FillPath(fill, path);
        }

        if (border is Color borderColor)
        {
            using Pen pen = new(borderColor, HextaTheme.HairlineWidth);
            graphics.DrawPath(pen, path);
        }

        if (Focused && Enabled)
        {
            using Pen focusPen = new(Color.FromArgb(90, HextaTheme.Primary), 1f);
            RectangleF focusRect = RectangleF.Inflate(bounds, -1.5f, -1.5f);
            using GraphicsPath focusPath = HextaTheme.CreateRoundedPath(focusRect, Math.Max(2f, CornerRadius * scale - 1.5f));
            graphics.DrawPath(focusPen, focusPath);
        }

        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            ClientRectangle,
            fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private (Color Background, Color Foreground, Color? Border) ResolveColors()
    {
        if (!Enabled)
        {
            return _variant switch
            {
                HextaButtonVariant.Ghost => (Color.Transparent, HextaTheme.TextDisabled, null),
                _ => (HextaTheme.MutedSurface, HextaTheme.TextDisabled, HextaTheme.Border)
            };
        }

        return _variant switch
        {
            HextaButtonVariant.Primary => (
                _pressed ? HextaTheme.PrimaryPressed : _hovered ? HextaTheme.PrimaryHover : HextaTheme.Primary,
                HextaTheme.PrimaryForeground,
                null),
            HextaButtonVariant.Danger => (
                _pressed ? HextaTheme.DangerPressed : _hovered ? HextaTheme.DangerHover : HextaTheme.Surface,
                HextaTheme.Danger,
                HextaTheme.DangerBorder),
            HextaButtonVariant.Ghost => (
                _pressed ? HextaTheme.HoverSurface : _hovered ? HextaTheme.MutedSurface : Color.Transparent,
                HextaTheme.TextPrimary,
                null),
            _ => (
                _pressed ? HextaTheme.HoverSurface : _hovered ? HextaTheme.MutedSurface : HextaTheme.Surface,
                HextaTheme.TextPrimary,
                HextaTheme.Border)
        };
    }
}

internal sealed class HextaSwitch : Control
{
    private bool _checked;
    private bool _hovered;

    public HextaSwitch()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Font = HextaTheme.CreateSmallFont();
        Cursor = Cursors.Hand;
        Size = new Size(220, 34);
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool SwitchOnRight { get; set; } = true;

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        if (!Enabled)
            _hovered = false;
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (Enabled && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
        {
            Focus();
            Checked = !Checked;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
        {
            e.Handled = true;
            Checked = !Checked;
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        if (Width <= 4 || Height <= 4)
            return;

        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        float scale = HextaTheme.ScaleFactor(this);
        float trackWidth = 38f * scale;
        float trackHeight = 22f * scale;
        float trackY = (Height - trackHeight) * 0.5f;
        float trackX = SwitchOnRight ? Width - trackWidth - 1f : 1f;

        Color trackColor = !Enabled
            ? HextaTheme.Border
            : _checked
                ? (_hovered ? HextaTheme.PrimaryHover : HextaTheme.Primary)
                : (_hovered ? HextaTheme.TextMuted : HextaTheme.StrongBorder);

        RectangleF trackRect = new(trackX, trackY, trackWidth, trackHeight);
        using GraphicsPath trackPath = HextaTheme.CreateRoundedPath(trackRect, trackHeight * 0.5f);
        using SolidBrush trackBrush = new(trackColor);
        graphics.FillPath(trackBrush, trackPath);

        float thumbInset = 2.5f * scale;
        float thumbSize = trackHeight - thumbInset * 2f;
        float thumbX = _checked ? trackRect.Right - thumbInset - thumbSize : trackRect.Left + thumbInset;
        float thumbY = trackRect.Top + thumbInset;
        Color thumbColor = !Enabled
            ? HextaTheme.MutedSurface
            : _checked
                ? HextaTheme.PrimaryForeground
                : (HextaTheme.IsDark ? Color.FromArgb(244, 244, 245) : Color.White);
        using SolidBrush thumbBrush = new(thumbColor);
        graphics.FillEllipse(thumbBrush, thumbX, thumbY, thumbSize, thumbSize);

        int textPadLeft = (int)Math.Round(2f * scale);
        Rectangle textBounds = SwitchOnRight
            ? new Rectangle(textPadLeft, 0, Math.Max(1, (int)(trackX - 10f * scale) - textPadLeft), Height)
            : new Rectangle((int)(trackX + trackWidth + 10f * scale), 0, Math.Max(1, Width - (int)(trackX + trackWidth + 10f * scale)), Height);

        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            textBounds,
            Enabled ? HextaTheme.TextPrimary : HextaTheme.TextDisabled,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class HextaSegmentedControl : Control
{
    private readonly List<string> _items = [];
    private int _selectedIndex;
    private int _hoveredIndex = -1;

    public HextaSegmentedControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Font = HextaTheme.CreateSmallFont();
        Cursor = Cursors.Hand;
        Size = new Size(260, 36);
    }

    public event EventHandler? SelectedIndexChanged;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            if (_selectedIndex == clamped)
                return;
            _selectedIndex = clamped;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetItems(IEnumerable<string> labels, int selectedIndex = -1)
    {
        _items.Clear();
        _items.AddRange(labels);
        if (selectedIndex >= 0 && _items.Count > 0)
            _selectedIndex = Math.Clamp(selectedIndex, 0, _items.Count - 1);
        else if (_items.Count > 0)
            _selectedIndex = Math.Clamp(_selectedIndex, 0, _items.Count - 1);
        else
            _selectedIndex = -1;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        _hoveredIndex = -1;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Enabled || _items.Count == 0)
            return;
        int index = HitTest(e.Location);
        if (_hoveredIndex != index)
        {
            _hoveredIndex = index;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredIndex != -1)
        {
            _hoveredIndex = -1;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left || _items.Count == 0)
            return;
        Focus();
        int index = HitTest(e.Location);
        if (index >= 0)
            SelectedIndex = index;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled || _items.Count == 0)
            return;
        if (e.KeyCode == Keys.Left && _selectedIndex > 0)
        {
            e.Handled = true;
            SelectedIndex--;
        }
        else if (e.KeyCode == Keys.Right && _selectedIndex < _items.Count - 1)
        {
            e.Handled = true;
            SelectedIndex++;
        }
    }

    private int HitTest(Point point)
    {
        if (_items.Count == 0 || Width <= 0)
            return -1;
        int index = (int)(point.X / (Width / (float)_items.Count));
        return Math.Clamp(index, 0, _items.Count - 1);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        if (Width <= 4 || Height <= 4)
            return;

        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        float scale = HextaTheme.ScaleFactor(this);
        float outerRadius = 8f * scale;
        float pad = 3f * scale;
        float innerRadius = Math.Max(3f, outerRadius - pad);

        RectangleF trackBounds = new(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath trackPath = HextaTheme.CreateRoundedPath(trackBounds, outerRadius);
        using SolidBrush trackBrush = new(HextaTheme.MutedSurface);
        using Pen trackBorder = new(HextaTheme.Border, HextaTheme.HairlineWidth);
        graphics.FillPath(trackBrush, trackPath);
        graphics.DrawPath(trackBorder, trackPath);

        if (_items.Count == 0)
            return;

        float innerWidth = Width - pad * 2f;
        float segWidth = innerWidth / _items.Count;
        float segHeight = Height - pad * 2f;

        for (int i = 0; i < _items.Count; i++)
        {
            RectangleF segRect = new(pad + i * segWidth, pad, segWidth, segHeight);
            bool selected = i == _selectedIndex;
            bool hovered = i == _hoveredIndex && !selected && Enabled;

            if (selected)
            {
                using GraphicsPath pillPath = HextaTheme.CreateRoundedPath(segRect, innerRadius);
                using SolidBrush pillBrush = new(Enabled ? HextaTheme.Surface : HextaTheme.MutedSurface);
                using Pen pillBorder = new(HextaTheme.Border, HextaTheme.HairlineWidth);
                graphics.FillPath(pillBrush, pillPath);
                graphics.DrawPath(pillBorder, pillPath);
            }
            else if (hovered)
            {
                using GraphicsPath hoverPath = HextaTheme.CreateRoundedPath(segRect, innerRadius);
                using SolidBrush hoverBrush = new(HextaTheme.IsDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(120, 255, 255, 255));
                graphics.FillPath(hoverBrush, hoverPath);
            }

            Color textColor = !Enabled
                ? HextaTheme.TextDisabled
                : selected
                    ? HextaTheme.TextPrimary
                    : HextaTheme.TextSecondary;

            TextRenderer.DrawText(
                graphics,
                _items[i],
                Font,
                Rectangle.Round(segRect),
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}

internal sealed class HextaSlider : Control
{
    private double _minimum = 40;
    private double _maximum = 100;
    private double _value = 94;
    private bool _dragging;

    public HextaSlider()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(180, 28);
    }

    public event EventHandler? ValueChanged;
    public event EventHandler? ValueCommitted;

    public double Minimum
    {
        get => _minimum;
        set { _minimum = value; Invalidate(); }
    }

    public double Maximum
    {
        get => _maximum;
        set { _maximum = value; Invalidate(); }
    }

    public double Value
    {
        get => _value;
        set
        {
            double clamped = Math.Clamp(value, _minimum, _maximum);
            if (Math.Abs(_value - clamped) < 0.0001)
                return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Enabled && e.Button == MouseButtons.Left)
        {
            Focus();
            _dragging = true;
            UpdateFromX(e.X);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
            UpdateFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging)
        {
            _dragging = false;
            UpdateFromX(e.X);
            ValueCommitted?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled)
            return;
        if (e.KeyCode == Keys.Left)
        {
            e.Handled = true;
            Value -= 5;
            ValueCommitted?.Invoke(this, EventArgs.Empty);
        }
        else if (e.KeyCode == Keys.Right)
        {
            e.Handled = true;
            Value += 5;
            ValueCommitted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateFromX(int x)
    {
        float scale = HextaTheme.ScaleFactor(this);
        float pad = 9f * scale;
        float trackLen = Math.Max(1f, Width - pad * 2f);
        double t = Math.Clamp((x - pad) / trackLen, 0.0, 1.0);
        Value = Math.Round(_minimum + (_maximum - _minimum) * t);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        if (Width <= 8 || Height <= 4)
            return;

        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float scale = HextaTheme.ScaleFactor(this);
        float pad = 9f * scale;
        float trackHeight = 5f * scale;
        float trackY = (Height - trackHeight) * 0.5f;
        float trackLen = Math.Max(1f, Width - pad * 2f);
        float fraction = _maximum > _minimum
            ? (float)Math.Clamp((_value - _minimum) / (_maximum - _minimum), 0.0, 1.0)
            : 0f;

        RectangleF trackRect = new(pad, trackY, trackLen, trackHeight);
        using GraphicsPath trackPath = HextaTheme.CreateRoundedPath(trackRect, trackHeight * 0.5f);
        using SolidBrush trackBrush = new(HextaTheme.Border);
        graphics.FillPath(trackBrush, trackPath);

        float fillWidth = Math.Max(trackHeight, trackLen * fraction);
        RectangleF fillRect = new(pad, trackY, Math.Min(fillWidth, trackLen), trackHeight);
        using GraphicsPath fillPath = HextaTheme.CreateRoundedPath(fillRect, trackHeight * 0.5f);
        using SolidBrush fillBrush = new(Enabled ? HextaTheme.Primary : HextaTheme.TextDisabled);
        graphics.FillPath(fillBrush, fillPath);

        float thumbSize = 15f * scale;
        float thumbCenterX = pad + trackLen * fraction;
        RectangleF thumbRect = new(thumbCenterX - thumbSize * 0.5f, (Height - thumbSize) * 0.5f, thumbSize, thumbSize);
        using SolidBrush thumbFill = new(HextaTheme.Surface);
        using Pen thumbBorder = new(Enabled ? HextaTheme.Primary : HextaTheme.StrongBorder, 1.5f);
        graphics.FillEllipse(thumbFill, thumbRect);
        graphics.DrawEllipse(thumbBorder, thumbRect);
    }
}

internal sealed class HextaColorSwatch : Control
{
    private bool _selected;
    private bool _hovered;
    private Color _swatchColor = Color.Black;
    private bool _isCustomPicker;

    public HextaColorSwatch()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.StandardClick |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(30, 30);
    }

    public Color SwatchColor
    {
        get => _swatchColor;
        set { _swatchColor = value; Invalidate(); }
    }

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    public bool IsCustomPicker
    {
        get => _isCustomPicker;
        set { _isCustomPicker = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        if (Width <= 4 || Height <= 4)
            return;

        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float scale = HextaTheme.ScaleFactor(this);
        float outerRadius = 7f * scale;
        float pad = 3f * scale;
        RectangleF outer = new(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
        RectangleF inner = RectangleF.Inflate(outer, -pad, -pad);

        if (_selected)
        {
            using GraphicsPath ringPath = HextaTheme.CreateRoundedPath(outer, outerRadius);
            using Pen ringPen = new(HextaTheme.Primary, 1.25f);
            graphics.DrawPath(ringPen, ringPath);
        }
        else if (_hovered)
        {
            using GraphicsPath ringPath = HextaTheme.CreateRoundedPath(outer, outerRadius);
            using Pen ringPen = new(HextaTheme.StrongBorder, HextaTheme.HairlineWidth);
            graphics.DrawPath(ringPen, ringPath);
        }

        using GraphicsPath innerPath = HextaTheme.CreateRoundedPath(inner, Math.Max(2f, outerRadius - pad));
        if (_isCustomPicker && !_selected)
        {
            using SolidBrush bg = new(HextaTheme.MutedSurface);
            using Pen border = new(HextaTheme.Border, HextaTheme.HairlineWidth);
            graphics.FillPath(bg, innerPath);
            graphics.DrawPath(border, innerPath);

            float cx = Width * 0.5f;
            float cy = Height * 0.5f;
            float arm = 4.5f * scale;
            using Pen plusPen = new(HextaTheme.TextSecondary, 1.25f);
            graphics.DrawLine(plusPen, cx - arm, cy, cx + arm, cy);
            graphics.DrawLine(plusPen, cx, cy - arm, cx, cy + arm);
        }
        else
        {
            using SolidBrush fill = new(_swatchColor);
            Color subtleOutline = HextaTheme.IsDark ? Color.FromArgb(64, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0);
            using Pen border = new(subtleOutline, HextaTheme.HairlineWidth);
            graphics.FillPath(fill, innerPath);
            graphics.DrawPath(border, innerPath);
        }
    }
}

internal sealed class HextaSelect : Control
{
    private readonly List<object> _items = [];
    private int _selectedIndex = -1;
    private bool _hovered;
    private bool _open;
    private ToolStripDropDown? _dropDown;
    private DateTime _lastClosedUtc = DateTime.MinValue;

    public HextaSelect()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Font = HextaTheme.CreateSmallFont();
        Cursor = Cursors.Hand;
        Size = new Size(180, 34);
    }

    public event EventHandler? SelectedIndexChanged;

    public int ItemCount => _items.Count;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int next = _items.Count == 0 ? -1 : Math.Clamp(value, -1, _items.Count - 1);
            if (_selectedIndex == next)
                return;
            _selectedIndex = next;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? SelectedItem
    {
        get => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;
        set
        {
            if (value is null)
            {
                SelectedIndex = -1;
                return;
            }
            int idx = _items.IndexOf(value);
            if (idx >= 0)
                SelectedIndex = idx;
        }
    }

    public IReadOnlyList<object> Items => _items;

    public void SetItems(IEnumerable<object> items, int selectedIndex = -1)
    {
        CloseDropDown();
        _items.Clear();
        _items.AddRange(items);
        if (_items.Count == 0)
            _selectedIndex = -1;
        else if (selectedIndex >= 0)
            _selectedIndex = Math.Clamp(selectedIndex, 0, _items.Count - 1);
        else if (_selectedIndex >= _items.Count)
            _selectedIndex = 0;
        Invalidate();
    }

    public void SetSelectedIndexSilently(int index)
    {
        _selectedIndex = _items.Count == 0 ? -1 : Math.Clamp(index, -1, _items.Count - 1);
        Invalidate();
    }

    private bool IsSelectedTextTruncated(out string fullText)
    {
        fullText = SelectedItem?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fullText) || Width <= 20)
            return false;
        float scale = HextaTheme.ScaleFactor(this);
        int avail = Math.Max(1, Width - (int)Math.Round(38f * scale));
        using Graphics g = CreateGraphics();
        Size sz = TextRenderer.MeasureText(g, fullText, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        return sz.Width > avail;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        if (!Enabled)
        {
            _hovered = false;
            CloseDropDown();
            HextaHoverTip.Hide();
        }
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
        if (!_open && IsSelectedTextTruncated(out string fullText))
            HextaHoverTip.Schedule(this, fullText, PointToScreen(new Point(0, 0)));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        HextaHoverTip.Hide();
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        HextaHoverTip.Hide();
        if (!Enabled || e.Button != MouseButtons.Left || _items.Count == 0)
            return;
        Focus();
        if (_open)
        {
            CloseDropDown();
            return;
        }
        if ((DateTime.UtcNow - _lastClosedUtc).TotalMilliseconds < 180)
            return;
        OpenDropDown();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled || _items.Count == 0)
            return;
        if (e.KeyCode == Keys.Down)
        {
            e.Handled = true;
            SelectedIndex = Math.Min(_items.Count - 1, _selectedIndex + 1);
        }
        else if (e.KeyCode == Keys.Up)
        {
            e.Handled = true;
            SelectedIndex = Math.Max(0, _selectedIndex - 1);
        }
        else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            OpenDropDown();
        }
    }

    private void OpenDropDown()
    {
        CloseDropDown();
        if (_items.Count == 0)
            return;

        float scale = HextaTheme.ScaleFactor(this);
        int itemHeight = (int)Math.Round(34f * scale);
        int pad = (int)Math.Round(5f * scale);
        int extraChrome = (int)Math.Round(72f * scale);
        int maxTextWidth = Width;
        using (Graphics g = CreateGraphics())
        {
            foreach (object item in _items)
            {
                Size sz = TextRenderer.MeasureText(
                    g,
                    item.ToString() ?? string.Empty,
                    Font,
                    Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                maxTextWidth = Math.Max(maxTextWidth, sz.Width + extraChrome);
            }
        }
        int screenCap = Math.Max(Width, Screen.FromControl(this).WorkingArea.Width - 32);
        int popupWidth = Math.Min(Math.Max(Width, maxTextWidth), screenCap);
        int visibleCount = Math.Min(_items.Count, 8);
        int popupHeight = visibleCount * itemHeight + pad * 2;

        SelectPopupPanel panel = new(_items, _selectedIndex, Font, itemHeight, pad)
        {
            Size = new Size(popupWidth, popupHeight)
        };
        ToolStripControlHost host = new(panel)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false,
            Size = new Size(popupWidth, popupHeight)
        };
        ToolStripDropDown dropDown = new()
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false,
            Size = new Size(popupWidth, popupHeight),
            BackColor = HextaTheme.Surface,
            DropShadowEnabled = true
        };
        dropDown.Items.Add(host);
        panel.ItemChosen += index =>
        {
            HextaHoverTip.Hide();
            CloseDropDown();
            SelectedIndex = index;
        };
        dropDown.Closed += (_, _) =>
        {
            HextaHoverTip.Hide();
            _open = false;
            _lastClosedUtc = DateTime.UtcNow;
            if (ReferenceEquals(_dropDown, dropDown))
                _dropDown = null;
            Invalidate();
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() =>
                {
                    if (!dropDown.IsDisposed)
                        dropDown.Dispose();
                });
            }
        };

        _dropDown = dropDown;
        _open = true;
        Invalidate();
        dropDown.Show(this, new Point(0, Height + (int)Math.Round(4f * scale)));
    }

    private void CloseDropDown()
    {
        ToolStripDropDown? dropDown = _dropDown;
        _dropDown = null;
        _open = false;
        if (dropDown is not null && !dropDown.IsDisposed)
            dropDown.Close();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        if (Width <= 4 || Height <= 4)
            return;

        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        float scale = HextaTheme.ScaleFactor(this);
        RectangleF bounds = new(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = HextaTheme.CreateRoundedPath(bounds, 7f * scale);

        Color bg = !Enabled
            ? HextaTheme.MutedSurface
            : _hovered || _open
                ? HextaTheme.MutedSurface
                : HextaTheme.Surface;
        Color border = !Enabled
            ? HextaTheme.Border
            : _open || Focused
                ? HextaTheme.Primary
                : _hovered
                    ? HextaTheme.StrongBorder
                    : HextaTheme.Border;

        using SolidBrush fill = new(bg);
        using Pen borderPen = new(border, _open ? 1f : HextaTheme.HairlineWidth);
        graphics.FillPath(fill, path);
        graphics.DrawPath(borderPen, path);

        int padX = (int)Math.Round(10f * scale);
        int chevronArea = (int)Math.Round(26f * scale);
        Rectangle textRect = new(padX, 0, Math.Max(1, Width - padX - chevronArea), Height);
        string text = SelectedItem?.ToString() ?? string.Empty;
        TextRenderer.DrawText(
            graphics,
            text,
            Font,
            textRect,
            Enabled ? HextaTheme.TextPrimary : HextaTheme.TextDisabled,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        float cx = Width - chevronArea * 0.5f;
        float cy = Height * 0.5f;
        float halfW = 3.8f * scale;
        float halfH = 2.1f * scale;
        using Pen chevronPen = new(Enabled ? HextaTheme.TextSecondary : HextaTheme.TextDisabled, 1.35f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        if (_open)
        {
            graphics.DrawLines(chevronPen,
            [
                new PointF(cx - halfW, cy + halfH),
                new PointF(cx, cy - halfH),
                new PointF(cx + halfW, cy + halfH)
            ]);
        }
        else
        {
            graphics.DrawLines(chevronPen,
            [
                new PointF(cx - halfW, cy - halfH),
                new PointF(cx, cy + halfH),
                new PointF(cx + halfW, cy - halfH)
            ]);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            HextaHoverTip.Hide();
            CloseDropDown();
        }
        base.Dispose(disposing);
    }

    private sealed class SelectPopupPanel : Control
    {
        private readonly IReadOnlyList<object> _items;
        private readonly int _selectedIndex;
        private readonly int _itemHeight;
        private readonly int _pad;
        private int _hoveredIndex = -1;
        private int _scrollOffset;

        public SelectPopupPanel(IReadOnlyList<object> items, int selectedIndex, Font font, int itemHeight, int pad)
        {
            _items = items;
            _selectedIndex = selectedIndex;
            _itemHeight = itemHeight;
            _pad = pad;
            Font = font;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);
            BackColor = HextaTheme.Surface;
            Cursor = Cursors.Hand;
        }

        public event Action<int>? ItemChosen;

        private int VisibleCapacity => Math.Max(1, (Height - _pad * 2) / _itemHeight);
        private int MaxScrollOffset => Math.Max(0, _items.Count - VisibleCapacity);

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (MaxScrollOffset <= 0)
                return;
            int delta = e.Delta > 0 ? -1 : 1;
            int next = Math.Clamp(_scrollOffset + delta, 0, MaxScrollOffset);
            if (next != _scrollOffset)
            {
                _scrollOffset = next;
                HextaHoverTip.Hide();
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int slot = (e.Y - _pad) / _itemHeight;
            int idx = _scrollOffset + slot;
            if (e.Y < _pad || idx < 0 || idx >= _items.Count)
                idx = -1;
            if (_hoveredIndex != idx)
            {
                _hoveredIndex = idx;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoveredIndex = -1;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;
            int idx = _scrollOffset + (e.Y - _pad) / _itemHeight;
            if (e.Y >= _pad && idx >= 0 && idx < _items.Count)
                ItemChosen?.Invoke(idx);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float scale = HextaTheme.ScaleFactor(this);
            RectangleF bounds = new(0.25f, 0.25f, Width - 0.5f, Height - 0.5f);
            using SolidBrush bg = new(HextaTheme.Surface);
            using Pen border = new(HextaTheme.Border, HextaTheme.HairlineWidth);
            graphics.FillRectangle(bg, ClientRectangle);
            graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);

            int checkArea = (int)Math.Round(24f * scale);
            int visible = VisibleCapacity;
            for (int slot = 0; slot < visible; slot++)
            {
                int i = _scrollOffset + slot;
                if (i >= _items.Count)
                    break;

                int rowY = _pad + slot * _itemHeight;
                RectangleF rowRect = new(_pad, rowY, Width - _pad * 2, _itemHeight);
                bool selected = i == _selectedIndex;
                bool hovered = i == _hoveredIndex;

                if (hovered || selected)
                {
                    using GraphicsPath rowPath = HextaTheme.CreateRoundedPath(rowRect, 5f * scale);
                    using SolidBrush rowBrush = new(hovered ? HextaTheme.HoverSurface : HextaTheme.MutedSurface);
                    graphics.FillPath(rowBrush, rowPath);
                }

                Rectangle textRect = new(
                    _pad + (int)Math.Round(10f * scale),
                    rowY,
                    Math.Max(1, Width - _pad * 2 - checkArea - (int)Math.Round(12f * scale)),
                    _itemHeight);
                TextRenderer.DrawText(
                    graphics,
                    _items[i].ToString() ?? string.Empty,
                    Font,
                    textRect,
                    HextaTheme.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                if (selected)
                {
                    float cx = Width - _pad - checkArea * 0.5f;
                    float cy = rowY + _itemHeight * 0.5f;
                    using Pen checkPen = new(HextaTheme.Primary, 1.6f)
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round,
                        LineJoin = LineJoin.Round
                    };
                    graphics.DrawLines(checkPen,
                    [
                        new PointF(cx - 4f * scale, cy + 0.2f * scale),
                        new PointF(cx - 1.2f * scale, cy + 3.2f * scale),
                        new PointF(cx + 4.5f * scale, cy - 3.2f * scale)
                    ]);
                }
            }
        }
    }
}

internal sealed class HextaNumberInput : Control
{
    private readonly TextBox _textBox;
    private int _minimum = 80;
    private int _maximum = 500;
    private int _value = 203;
    private int _step = 1;
    private int _hoveredBtn = -1;
    private bool _updatingText;

    public HextaNumberInput()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Font = HextaTheme.CreateSmallFont();

        _textBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            TextAlign = HorizontalAlignment.Center,
            BackColor = HextaTheme.Surface,
            ForeColor = HextaTheme.TextPrimary,
            Font = Font,
            Text = _value.ToString()
        };
        _textBox.Leave += (_, _) => CommitText();
        _textBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CommitText();
            }
            else if (e.KeyCode == Keys.Up)
            {
                e.Handled = true;
                Value += _step;
            }
            else if (e.KeyCode == Keys.Down)
            {
                e.Handled = true;
                Value -= _step;
            }
        };
        Controls.Add(_textBox);
        Size = new Size(116, 34);
    }

    public event EventHandler? ValueChanged;

    public int Minimum
    {
        get => _minimum;
        set { _minimum = value; Value = _value; }
    }

    public int Maximum
    {
        get => _maximum;
        set { _maximum = value; Value = _value; }
    }

    public int Step
    {
        get => _step;
        set => _step = Math.Max(1, value);
    }

    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, _minimum, _maximum);
            bool changed = _value != clamped;
            _value = clamped;
            SyncText();
            if (changed)
                ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RefreshTheme()
    {
        if (_textBox is null)
            return;
        _textBox.BackColor = Enabled ? HextaTheme.Surface : HextaTheme.MutedSurface;
        _textBox.ForeColor = Enabled ? HextaTheme.TextPrimary : HextaTheme.TextDisabled;
        Invalidate();
    }

    private void SyncText()
    {
        if (_textBox is null)
            return;
        _updatingText = true;
        _textBox.Text = _value.ToString();
        _updatingText = false;
    }

    private void CommitText()
    {
        if (_updatingText)
            return;
        if (int.TryParse(_textBox.Text.Trim(), out int parsed))
            Value = parsed;
        else
            SyncText();
    }

    private RectangleF MinusRect => new(1f, 1f, Height - 4f, Height - 2f);
    private RectangleF PlusRect => new(Width - Height + 3f, 1f, Height - 4f, Height - 2f);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_textBox is null)
            return;
        int side = Math.Max(22, Height - 2);
        int textH = _textBox.PreferredHeight;
        _textBox.SetBounds(
            side,
            Math.Max(2, (Height - textH) / 2),
            Math.Max(10, Width - side * 2),
            textH);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (_textBox is null)
            return;
        _textBox.Enabled = Enabled;
        RefreshTheme();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Enabled)
            return;
        int hovered = MinusRect.Contains(e.Location) ? 0 : PlusRect.Contains(e.Location) ? 1 : -1;
        Cursor = hovered >= 0 ? Cursors.Hand : Cursors.Default;
        if (_hoveredBtn != hovered)
        {
            _hoveredBtn = hovered;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredBtn = -1;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left)
            return;
        CommitText();
        if (MinusRect.Contains(e.Location))
            Value -= _step;
        else if (PlusRect.Contains(e.Location))
            Value += _step;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 4 || Height <= 4)
            return;

        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float scale = HextaTheme.ScaleFactor(this);
        RectangleF bounds = new(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = HextaTheme.CreateRoundedPath(bounds, 7f * scale);
        using SolidBrush bg = new(Enabled ? HextaTheme.Surface : HextaTheme.MutedSurface);
        using Pen border = new(HextaTheme.Border, HextaTheme.HairlineWidth);
        graphics.FillPath(bg, path);
        graphics.DrawPath(border, path);

        DrawStepButton(graphics, MinusRect, isPlus: false, hovered: _hoveredBtn == 0 && Enabled, scale);
        DrawStepButton(graphics, PlusRect, isPlus: true, hovered: _hoveredBtn == 1 && Enabled, scale);
    }

    private void DrawStepButton(Graphics graphics, RectangleF rect, bool isPlus, bool hovered, float scale)
    {
        if (hovered)
        {
            RectangleF inset = RectangleF.Inflate(rect, -2f, -2f);
            using GraphicsPath hoverPath = HextaTheme.CreateRoundedPath(inset, 5f * scale);
            using SolidBrush hoverBrush = new(HextaTheme.MutedSurface);
            graphics.FillPath(hoverBrush, hoverPath);
        }

        float cx = rect.Left + rect.Width * 0.5f;
        float cy = rect.Top + rect.Height * 0.5f;
        float arm = 4f * scale;
        using Pen pen = new(Enabled ? HextaTheme.TextSecondary : HextaTheme.TextDisabled, 1.35f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(pen, cx - arm, cy, cx + arm, cy);
        if (isPlus)
            graphics.DrawLine(pen, cx, cy - arm, cx, cy + arm);
    }
}

internal sealed class HextaReadOnlyField : Control
{
    public HextaReadOnlyField()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Font = HextaTheme.CreateSmallFont();
        Size = new Size(200, 34);
        TabStop = false;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (!string.IsNullOrWhiteSpace(Text))
        {
            float scale = HextaTheme.ScaleFactor(this);
            int avail = Math.Max(1, Width - (int)Math.Round(20f * scale));
            using Graphics g = CreateGraphics();
            Size sz = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (sz.Width > avail)
                HextaHoverTip.Schedule(this, Text, PointToScreen(new Point(0, 0)));
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        HextaHoverTip.Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 4 || Height <= 4)
            return;

        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        float scale = HextaTheme.ScaleFactor(this);
        RectangleF bounds = new(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = HextaTheme.CreateRoundedPath(bounds, 7f * scale);
        using SolidBrush bg = new(HextaTheme.MutedSurface);
        using Pen border = new(HextaTheme.Border, HextaTheme.HairlineWidth);
        graphics.FillPath(bg, path);
        graphics.DrawPath(border, path);

        int padX = (int)Math.Round(10f * scale);
        Rectangle textRect = new(padX, 0, Math.Max(1, Width - padX * 2), Height);
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            textRect,
            Enabled ? HextaTheme.TextSecondary : HextaTheme.TextDisabled,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
    }
}
