using System.Drawing.Drawing2D;
using HDRScreenMirror.Interop;

namespace HDRScreenMirror.UI;

internal sealed class StatusOverlayForm : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int CornerRadius = 18;

    private readonly bool _mouseThrough;
    private readonly bool _excludeFromCapture;
    private readonly Label _stateLabel;
    private readonly Label _routeLabel;
    private readonly Label _signalLabel;
    private readonly Label _metricsLabel;
    private readonly Label _hotkeyLabel;
    private readonly Panel _statusDot;
    private bool _hasTelemetry;

    public StatusOverlayForm(
        Rectangle outputBounds,
        DisplayTarget capture,
        DisplayTarget present,
        bool mouseThrough,
        bool excludeFromCapture = true)
    {
        _mouseThrough = mouseThrough;
        _excludeFromCapture = excludeFromCapture;
        Text = "HDRScreenMirror Status";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(outputBounds.Left + 24, outputBounds.Top + 24, 390, 196);
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(20, 22, 27);
        Opacity = 0.94;
        Padding = new Padding(20, 17, 20, 16);
        DoubleBuffered = true;

        Label titleLabel = new()
        {
            Text = "HDR SCREEN MIRROR",
            ForeColor = Color.FromArgb(153, 163, 180),
            Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(20, 17),
            BackColor = Color.Transparent
        };

        _statusDot = new Panel
        {
            Size = new Size(10, 10),
            Location = new Point(21, 49),
            BackColor = Color.Transparent
        };
        _statusDot.Paint += (_, eventArgs) =>
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using SolidBrush brush = new(Color.FromArgb(64, 211, 138));
            eventArgs.Graphics.FillEllipse(brush, 0, 0, 9, 9);
        };

        _stateLabel = new Label
        {
            Text = Localization.T("OverlayStarting"),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Regular),
            AutoSize = false,
            Location = new Point(41, 42),
            Size = new Size(325, 28),
            BackColor = Color.Transparent
        };

        _routeLabel = CreateDetailLabel(
            $"{capture.DeviceName}  →  {present.DeviceName}",
            76,
            new Font("Segoe UI", 9.5f, FontStyle.Regular));
        _signalLabel = CreateDetailLabel(
            Localization.T("OverlayWaiting"),
            104,
            new Font("Segoe UI", 9.5f, FontStyle.Regular));
        _metricsLabel = CreateDetailLabel(
            Localization.F(
                "OverlayMetrics",
                0d,
                Localization.T("CursorOff"),
                0),
            132,
            new Font("Consolas", 9.5f, FontStyle.Regular));

        _hotkeyLabel = CreateDetailLabel(
            Localization.T("OverlayHotkeys"),
            161,
            new Font("Segoe UI", 8.5f, FontStyle.Regular));
        _hotkeyLabel.ForeColor = Color.FromArgb(128, 138, 155);

        Controls.Add(titleLabel);
        Controls.Add(_statusDot);
        Controls.Add(_stateLabel);
        Controls.Add(_routeLabel);
        Controls.Add(_signalLabel);
        Controls.Add(_metricsLabel);
        Controls.Add(_hotkeyLabel);
        UpdateRoundedRegion();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            if (_mouseThrough)
                parameters.ExStyle |= WsExTransparent;
            return parameters;
        }
    }

    public void UpdateTelemetry(MirrorTelemetry telemetry)
    {
        _hasTelemetry = true;
        _stateLabel.Text = telemetry.State;
        _signalLabel.Text = $"{telemetry.InputKind}   {telemetry.InputFormat}   {telemetry.Width}×{telemetry.Height}";
        _metricsLabel.Text = Localization.F(
            "OverlayMetrics",
            telemetry.FramesPerSecond,
            Localization.T(telemetry.CursorVisible ? "CursorOn" : "CursorOff"),
            telemetry.Timeouts);
        _statusDot.Invalidate();
    }

    public void ApplyLanguage()
    {
        _hotkeyLabel.Text = Localization.T("OverlayHotkeys");
        if (!_hasTelemetry)
        {
            _stateLabel.Text = Localization.T("OverlayStarting");
            _signalLabel.Text = Localization.T("OverlayWaiting");
            _metricsLabel.Text = Localization.F(
                "OverlayMetrics",
                0d,
                Localization.T("CursorOff"),
                0);
        }
    }

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        if (_excludeFromCapture)
            NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WdaExcludeFromCapture);
        BringToFront();
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        UpdateRoundedRegion();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = CreateRoundedRectangle(ClientRectangle, CornerRadius);
        using Pen outline = new(Color.FromArgb(28, 255, 255, 255), 1);
        eventArgs.Graphics.DrawPath(outline, path);
    }

    private Label CreateDetailLabel(string text, int y, Font font) => new()
    {
        Text = text,
        ForeColor = Color.FromArgb(214, 219, 228),
        Font = font,
        AutoSize = false,
        Location = new Point(20, y),
        Size = new Size(350, 24),
        BackColor = Color.Transparent,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private void UpdateRoundedRegion()
    {
        if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0)
            return;

        using GraphicsPath path = CreateRoundedRectangle(ClientRectangle, CornerRadius);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle rectangle, int radius)
    {
        Rectangle bounds = Rectangle.Inflate(rectangle, -1, -1);
        int diameter = radius * 2;
        GraphicsPath path = new();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
