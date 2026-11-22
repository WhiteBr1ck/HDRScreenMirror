using System.Drawing.Drawing2D;
using System.Drawing.Text;
using HDRScreenMirror.Interop;
using Vortice.DXGI;

namespace HDRScreenMirror.UI;

internal sealed class AnalysisOverlayForm : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int LayoutBaselineDpi = 144;
    private const float DesignFontScale = LayoutBaselineDpi / 96f;

    private const int ToastHeight = 42;
    private const int ToastRadius = 10;
    private const float HairlineWidth = 0.5f;

    private static readonly Color TransparencyColor = Color.FromArgb(1, 2, 3);
    private static readonly Color MaximumColor = Color.FromArgb(255, 102, 92);
    private static readonly Color MinimumColor = Color.FromArgb(92, 198, 245);
    private static readonly Color SuccessColor = Color.FromArgb(63, 209, 132);

    private OverlayAppearance _appearance = OverlayAppearance.Default;
    private Color SurfaceColor => _appearance.BackgroundColor == TransparencyColor
        ? Color.FromArgb(2, 3, 4)
        : _appearance.BackgroundColor;
    private Color BorderColor => _appearance.BorderColor;
    private Color PrimaryTextColor => _appearance.PrimaryTextColor;

    private readonly bool _excludeFromCapture;
    private readonly ModeRotation _rotation;
    private readonly Rectangle _outputBounds;

    private readonly Font _markerFont = CreatePixelFont(15f * DesignFontScale, FontStyle.Bold);
    private readonly Font _toastFont = CreatePixelFont(13.5f * DesignFontScale, FontStyle.Bold);

    private readonly System.Windows.Forms.Timer _screenshotToastTimer = new() { Interval = 2000 };
    private bool _showMarkers;
    private bool _showScreenshotToast;
    private string _screenshotToastKey = "ScreenshotSuccessToast";
    private uint _frameWidth;
    private uint _frameHeight;
    private LuminanceTelemetry? _luminance;
    private float _dpiScale = 1f;
    private int _currentDpi = LayoutBaselineDpi;

    public AnalysisOverlayForm(
        Rectangle outputBounds,
        DisplayTarget capture,
        bool showMarkers,
        bool excludeFromCapture = true)
    {
        _rotation = capture.Rotation;
        _outputBounds = outputBounds;
        _showMarkers = showMarkers;
        _excludeFromCapture = excludeFromCapture;

        Text = "HDRScreenMirror Analysis";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = outputBounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = TransparencyColor;
        TransparencyKey = TransparencyColor;
        DoubleBuffered = true;

        _screenshotToastTimer.Tick += (_, _) => HideScreenshotNotification();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate | WsExTransparent;
            return parameters;
        }
    }

    internal bool HasVisibleContent => _showMarkers || _showScreenshotToast;

    internal void SetShowMarkers(bool visible)
    {
        _showMarkers = visible;
        Invalidate();
    }

    internal void ShowScreenshotNotification(string? messageKey = null)
    {
        _screenshotToastKey = string.IsNullOrWhiteSpace(messageKey)
            ? "ScreenshotSuccessToast"
            : messageKey;
        _showScreenshotToast = true;
        _screenshotToastTimer.Stop();
        _screenshotToastTimer.Start();
        if (!Visible)
            Show();
        BringToFront();
        Invalidate();
    }

    internal void HideScreenshotNotification()
    {
        _screenshotToastTimer.Stop();
        if (!_showScreenshotToast)
            return;

        _showScreenshotToast = false;
        Invalidate();
        if (!_showMarkers)
            Hide();
    }

    internal void UpdateMirrorTelemetry(MirrorTelemetry telemetry)
    {
        _frameWidth = telemetry.Width;
        _frameHeight = telemetry.Height;
    }

    internal void UpdateLuminance(LuminanceTelemetry telemetry)
    {
        LuminanceTelemetry? previous = _luminance;
        _luminance = telemetry;
        if (!_showMarkers)
            return;

        if (previous is not null &&
            previous.MaximumX == telemetry.MaximumX &&
            previous.MaximumY == telemetry.MaximumY &&
            previous.MinimumX == telemetry.MinimumX &&
            previous.MinimumY == telemetry.MinimumY &&
            Math.Abs(previous.MaximumNits - telemetry.MaximumNits) < 0.05 &&
            Math.Abs(previous.MinimumNits - telemetry.MinimumNits) < 0.005)
        {
            return;
        }

        Invalidate();
    }

    internal void ApplyLanguage() => Invalidate();

    internal void ApplyAppearance(OverlayAppearance appearance)
    {
        _appearance = appearance;
        ApplyDpi(_currentDpi);
    }

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        ApplyDpi(DeviceDpi);
        SetCaptureExclusion(true);
        BringToFront();
    }

    internal bool SetCaptureExclusion(bool excluded)
    {
        if (!_excludeFromCapture || !IsHandleCreated)
            return !excluded;

        return NativeMethods.SetWindowDisplayAffinity(
            Handle,
            excluded ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs eventArgs)
    {
        base.OnDpiChanged(eventArgs);
        Bounds = _outputBounds;
        ApplyDpi(eventArgs.DeviceDpiNew);
    }

    private void ApplyDpi(int dpi)
    {
        _currentDpi = Math.Max(72, dpi);
        _dpiScale = Math.Max(0.35f, (_currentDpi / (float)LayoutBaselineDpi) * (float)_appearance.Scale);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(TransparencyColor);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        DrawScreenshot(eventArgs.Graphics);
    }

    internal void DrawScreenshot(Graphics graphics)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        if (_showMarkers && _luminance is not null && _frameWidth > 0 && _frameHeight > 0)
            DrawLuminanceMarkers(graphics, _luminance);
        if (_showScreenshotToast)
            DrawScreenshotNotification(graphics);
    }

    private void DrawScreenshotNotification(Graphics graphics)
    {
        string text = Localization.T(_screenshotToastKey);
        using StringFormat typographic = CreateTypographicFormat();
        SizeF textSize = graphics.MeasureString(text, _toastFont, 320, typographic);
        float designWidth = Math.Max(160f, textSize.Width + 56f);
        float scaledWidth = designWidth * _dpiScale;
        float scaledHeight = ToastHeight * _dpiScale;
        float originX = (ClientSize.Width - scaledWidth) * 0.5f;
        float originY = ClientSize.Height - scaledHeight - 40f * _dpiScale;

        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(originX, originY);
        graphics.ScaleTransform(_dpiScale, _dpiScale);

        RectangleF toast = new(0.25f, 0.25f, designWidth - 0.5f, ToastHeight - 0.5f);
        using GraphicsPath toastPath = CreateRoundedRectangle(toast, ToastRadius);
        using SolidBrush toastBrush = new(SurfaceColor);
        using Pen toastOutline = new(BorderColor, HairlineWidth);
        graphics.FillPath(toastBrush, toastPath);
        graphics.DrawPath(toastOutline, toastPath);

        const float sqSize = 7f;
        float sqX = 18f;
        float sqY = (ToastHeight - sqSize) * 0.5f;
        using GraphicsPath sqPath = CreateRoundedRectangle(new RectangleF(sqX, sqY, sqSize, sqSize), 1.5f);
        using SolidBrush sqBrush = new(SuccessColor);
        graphics.FillPath(sqBrush, sqPath);

        using SolidBrush textBrush = new(PrimaryTextColor);
        float textY = (ToastHeight - _toastFont.Height) * 0.5f;
        graphics.DrawString(text, _toastFont, textBrush, sqX + sqSize + 10f, textY, typographic);

        graphics.Restore(state);
    }

    private void DrawLuminanceMarkers(Graphics graphics, LuminanceTelemetry telemetry)
    {
        PointF maximum = MapSourcePosition(telemetry.MaximumX, telemetry.MaximumY);
        PointF minimum = MapSourcePosition(telemetry.MinimumX, telemetry.MinimumY);
        DrawMinimalMarker(graphics, maximum, MaximumColor, Localization.T("MarkerMaximum"), telemetry.MaximumNits, labelAbove: true);
        DrawMinimalMarker(graphics, minimum, MinimumColor, Localization.T("MarkerMinimum"), telemetry.MinimumNits, labelAbove: false);
    }

    // High-visibility clean crosshair + bare number with crisp dark outline (no box).
    private void DrawMinimalMarker(
        Graphics graphics,
        PointF point,
        Color color,
        string prefix,
        double nits,
        bool labelAbove)
    {
        float arm = 10f * _dpiScale;
        float haloWidth = Math.Max(3.2f, 4.0f * _dpiScale);
        float lineWidth = Math.Max(1.6f, 2.0f * _dpiScale);

        using Pen haloPen = new(Color.FromArgb(230, 0, 0, 0), haloWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using Pen linePen = new(color, lineWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        graphics.DrawLine(haloPen, point.X - arm, point.Y, point.X + arm, point.Y);
        graphics.DrawLine(haloPen, point.X, point.Y - arm, point.X, point.Y + arm);
        graphics.DrawLine(linePen, point.X - arm, point.Y, point.X + arm, point.Y);
        graphics.DrawLine(linePen, point.X, point.Y - arm, point.X, point.Y + arm);

        string valueText = nits < 1 ? $"{nits:F3}" : $"{nits:F1}";
        string text = $"{prefix} {valueText}";
        using StringFormat typographic = CreateTypographicFormat();
        SizeF designSize = graphics.MeasureString(text, _markerFont, 260, typographic);
        float scaledW = designSize.Width * _dpiScale;
        float scaledH = _markerFont.Height * _dpiScale;

        float textX = point.X + arm + 7f * _dpiScale;
        if (textX + scaledW + 8f > ClientSize.Width)
            textX = point.X - arm - scaledW - 7f * _dpiScale;
        textX = Math.Clamp(textX, 6f, Math.Max(6f, ClientSize.Width - scaledW - 6f));

        float textY = labelAbove ? point.Y - scaledH - 4f * _dpiScale : point.Y + 4f * _dpiScale;
        textY = Math.Clamp(textY, 6f, Math.Max(6f, ClientSize.Height - scaledH - 6f));

        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(textX, textY);
        graphics.ScaleTransform(_dpiScale, _dpiScale);

        // Crisp 8-direction outline shadow for effortless readability on bright HDR and black scenes.
        using SolidBrush shadowBrush = new(Color.FromArgb(235, 0, 0, 0));
        using SolidBrush textBrush = new(color);
        const float o = 1.6f;
        graphics.DrawString(text, _markerFont, shadowBrush, -o, 0f, typographic);
        graphics.DrawString(text, _markerFont, shadowBrush, o, 0f, typographic);
        graphics.DrawString(text, _markerFont, shadowBrush, 0f, -o, typographic);
        graphics.DrawString(text, _markerFont, shadowBrush, 0f, o, typographic);
        graphics.DrawString(text, _markerFont, shadowBrush, -o, -o, typographic);
        graphics.DrawString(text, _markerFont, shadowBrush, o, -o, typographic);
        graphics.DrawString(text, _markerFont, shadowBrush, -o, o, typographic);
        graphics.DrawString(text, _markerFont, shadowBrush, o, o, typographic);
        graphics.DrawString(text, _markerFont, textBrush, 0f, 0f, typographic);

        graphics.Restore(state);
    }

    private PointF MapSourcePosition(uint sourceX, uint sourceY)
    {
        float sourceU = (sourceX + 0.5f) / _frameWidth;
        float sourceV = (sourceY + 0.5f) / _frameHeight;
        int rotationCode = _rotation switch
        {
            ModeRotation.Rotate90 => 1,
            ModeRotation.Rotate180 => 2,
            ModeRotation.Rotate270 => 3,
            _ => 0
        };
        (float outputU, float outputV) = rotationCode switch
        {
            1 => (1f - sourceV, sourceU),
            2 => (1f - sourceU, 1f - sourceV),
            3 => (sourceV, 1f - sourceU),
            _ => (sourceU, sourceV)
        };

        float rotatedWidth = rotationCode is 1 or 3 ? _frameHeight : _frameWidth;
        float rotatedHeight = rotationCode is 1 or 3 ? _frameWidth : _frameHeight;
        float scale = Math.Min(ClientSize.Width / rotatedWidth, ClientSize.Height / rotatedHeight);
        float width = rotatedWidth * scale;
        float height = rotatedHeight * scale;
        float left = (ClientSize.Width - width) * 0.5f;
        float top = (ClientSize.Height - height) * 0.5f;
        return new PointF(left + outputU * width, top + outputV * height);
    }

    private static Font CreatePixelFont(float size, FontStyle style) =>
        new("Segoe UI", size, style, GraphicsUnit.Pixel);

    private static StringFormat CreateTypographicFormat() => new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Near,
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces
    };

    private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _markerFont.Dispose();
            _toastFont.Dispose();
            _screenshotToastTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
