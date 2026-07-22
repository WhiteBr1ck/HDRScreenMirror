using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HDRScreenMirror.Interop;
using Vortice.DXGI;

namespace HDRScreenMirror.UI;

internal sealed class AnalysisOverlayForm : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int CiePanelWidth = 580;
    private const int CiePanelHeight = 650;
    private const int ScreenshotToastWidth = 320;
    private const int ScreenshotToastHeight = 58;
    private const float DiagramMaximum = 0.65f;

    private static readonly Color TransparencyColor = Color.FromArgb(1, 2, 3);
    private static readonly Color SrgbColor = Color.FromArgb(71, 214, 255);
    private static readonly Color DciP3Color = Color.FromArgb(255, 213, 79);
    private static readonly Color Bt2020Color = Color.FromArgb(229, 105, 255);
    private static readonly Color MaximumColor = Color.FromArgb(255, 104, 92);
    private static readonly Color MinimumColor = Color.FromArgb(74, 210, 255);

    private readonly bool _excludeFromCapture;
    private readonly ModeRotation _rotation;
    private readonly Font _titleFont = new("Segoe UI", 11f, FontStyle.Bold);
    private readonly Font _bodyFont = new("Segoe UI", 9.5f, FontStyle.Regular);
    private readonly Font _smallFont = new("Segoe UI", 8.5f, FontStyle.Regular);
    private readonly Font _markerFont = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _toastFont = new("Segoe UI", 11f, FontStyle.Bold);
    private readonly System.Windows.Forms.Timer _screenshotToastTimer = new() { Interval = 2000 };
    private bool _showCie;
    private bool _showMarkers;
    private bool _showScreenshotToast;
    private uint _frameWidth;
    private uint _frameHeight;
    private GamutTelemetry? _gamut;
    private LuminanceTelemetry? _luminance;
    private Bitmap? _heatmap;

    public AnalysisOverlayForm(
        Rectangle outputBounds,
        DisplayTarget capture,
        bool showCie,
        bool showMarkers,
        bool excludeFromCapture = true)
    {
        _rotation = capture.Rotation;
        _showCie = showCie;
        _showMarkers = showMarkers;
        _excludeFromCapture = excludeFromCapture;

        Text = "HDRScreenMirror Analysis";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
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

    internal bool HasVisibleContent => _showCie || _showMarkers || _showScreenshotToast;

    internal void SetShowCie(bool visible)
    {
        _showCie = visible;
        Invalidate();
    }

    internal void SetShowMarkers(bool visible)
    {
        _showMarkers = visible;
        Invalidate();
    }

    internal void ShowScreenshotNotification()
    {
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
        if (!_showCie && !_showMarkers)
            Hide();
    }

    internal void UpdateMirrorTelemetry(MirrorTelemetry telemetry)
    {
        _frameWidth = telemetry.Width;
        _frameHeight = telemetry.Height;
    }

    internal void UpdateLuminance(LuminanceTelemetry telemetry)
    {
        _luminance = telemetry;
        if (_showMarkers)
            Invalidate();
    }

    internal void UpdateGamut(GamutTelemetry telemetry)
    {
        _gamut = telemetry;
        Bitmap? previous = _heatmap;
        _heatmap = CreateHeatmap(telemetry);
        previous?.Dispose();
        if (_showCie)
            Invalidate();
    }

    internal void ApplyLanguage() => Invalidate();

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        SetCaptureExclusion(true);
        BringToFront();
    }

    internal void SetCaptureExclusion(bool excluded)
    {
        if (!_excludeFromCapture || !IsHandleCreated)
            return;

        NativeMethods.SetWindowDisplayAffinity(
            Handle,
            excluded ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(TransparencyColor);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        if (_showCie)
            DrawCiePanel(eventArgs.Graphics);
        if (_showMarkers && _luminance is not null && _frameWidth > 0 && _frameHeight > 0)
            DrawLuminanceMarkers(eventArgs.Graphics, _luminance);
        if (_showScreenshotToast)
            DrawScreenshotNotification(eventArgs.Graphics);
    }

    private void DrawScreenshotNotification(Graphics graphics)
    {
        Rectangle toast = new(
            (ClientSize.Width - ScreenshotToastWidth) / 2,
            ClientSize.Height - ScreenshotToastHeight - 52,
            ScreenshotToastWidth,
            ScreenshotToastHeight);
        using GraphicsPath toastPath = CreateRoundedRectangle(toast, 18);
        using SolidBrush toastBrush = new(Color.FromArgb(20, 22, 28));
        using Pen toastOutline = new(Color.FromArgb(54, 255, 255, 255), 1);
        graphics.FillPath(toastBrush, toastPath);
        graphics.DrawPath(toastOutline, toastPath);

        const int iconSize = 28;
        Rectangle icon = new(toast.Left + 18, toast.Top + 15, iconSize, iconSize);
        using SolidBrush iconBrush = new(Color.FromArgb(43, 190, 105));
        graphics.FillEllipse(iconBrush, icon);
        using Pen checkPen = new(Color.White, 2.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawLines(checkPen,
        [
            new PointF(icon.Left + 7, icon.Top + 14),
            new PointF(icon.Left + 12, icon.Top + 19),
            new PointF(icon.Left + 21, icon.Top + 9)
        ]);

        using SolidBrush textBrush = new(Color.FromArgb(242, 245, 250));
        using StringFormat format = new()
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center
        };
        Rectangle textBounds = new(icon.Right + 13, toast.Top, toast.Width - 76, toast.Height);
        graphics.DrawString(
            Localization.T("ScreenshotSuccessToast"),
            _toastFont,
            textBrush,
            textBounds,
            format);
    }

    private void DrawCiePanel(Graphics graphics)
    {
        Rectangle panel = new(24, ClientSize.Height - CiePanelHeight - 24, CiePanelWidth, CiePanelHeight);
        using GraphicsPath panelPath = CreateRoundedRectangle(panel, 18);
        using SolidBrush panelBrush = new(Color.FromArgb(20, 22, 28));
        using Pen panelOutline = new(Color.FromArgb(54, 255, 255, 255), 1);
        graphics.FillPath(panelBrush, panelPath);
        graphics.DrawPath(panelOutline, panelPath);

        using SolidBrush primaryBrush = new(Color.FromArgb(242, 245, 250));
        using SolidBrush secondaryBrush = new(Color.FromArgb(164, 174, 190));
        graphics.DrawString("CIE 1976 UCS  u′v′", _titleFont, primaryBrush, panel.Left + 20, panel.Top + 15);
        graphics.DrawString(Localization.T("CieFrameUsage"), _smallFont, secondaryBrush, panel.Left + 20, panel.Top + 36);

        Rectangle plot = new(panel.Left + 65, panel.Top + 62, 450, 450);
        using SolidBrush plotBrush = new(Color.FromArgb(10, 12, 17));
        graphics.FillRectangle(plotBrush, plot);
        DrawCieGrid(graphics, plot);

        if (_heatmap is not null)
        {
            InterpolationMode previousMode = graphics.InterpolationMode;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.DrawImage(_heatmap, plot);
            graphics.InterpolationMode = previousMode;
        }

        DrawGamutTriangle(graphics, plot, SrgbColor, [(0.64f, 0.33f), (0.30f, 0.60f), (0.15f, 0.06f)]);
        DrawGamutTriangle(graphics, plot, DciP3Color, [(0.68f, 0.32f), (0.265f, 0.69f), (0.15f, 0.06f)]);
        DrawGamutTriangle(graphics, plot, Bt2020Color, [(0.708f, 0.292f), (0.170f, 0.797f), (0.131f, 0.046f)]);

        double srgb = _gamut?.SrgbPercentage ?? 0;
        double dciP3 = _gamut?.DciP3Percentage ?? 0;
        double bt2020 = _gamut?.Bt2020Percentage ?? 0;
        double outside = _gamut?.OutsidePercentage ?? 0;
        int legendY = panel.Top + 545;
        DrawLegend(graphics, panel.Left + 24, legendY, SrgbColor, "sRGB", srgb);
        DrawLegend(graphics, panel.Left + 300, legendY, DciP3Color, "DCI-P3", dciP3);
        DrawLegend(graphics, panel.Left + 24, legendY + 34, Bt2020Color, "BT.2020", bt2020);
        DrawLegend(graphics, panel.Left + 300, legendY + 34, Color.FromArgb(142, 151, 168), Localization.T("CieOutside"), outside);

        string footer = _gamut is null
            ? Localization.T("CieWaiting")
            : Localization.F("CieAnalyzedPixels", _gamut.AnalyzedPixels);
        graphics.DrawString(footer, _smallFont, secondaryBrush, panel.Left + 24, panel.Bottom - 28);
    }

    private void DrawCieGrid(Graphics graphics, Rectangle plot)
    {
        using Pen gridPen = new(Color.FromArgb(30, 255, 255, 255), 1);
        using Pen borderPen = new(Color.FromArgb(75, 255, 255, 255), 1);
        using SolidBrush labelBrush = new(Color.FromArgb(125, 136, 154));
        for (float value = 0.1f; value <= 0.6f; value += 0.1f)
        {
            float x = plot.Left + value / DiagramMaximum * plot.Width;
            float y = plot.Bottom - value / DiagramMaximum * plot.Height;
            graphics.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            graphics.DrawString(value.ToString("F1"), _smallFont, labelBrush, x - 9, plot.Bottom + 3);
            graphics.DrawString(value.ToString("F1"), _smallFont, labelBrush, plot.Left - 28, y - 7);
        }
        graphics.DrawRectangle(borderPen, plot);
        graphics.DrawString("u′", _bodyFont, labelBrush, plot.Right - 14, plot.Bottom + 3);
        graphics.DrawString("v′", _bodyFont, labelBrush, plot.Left - 27, plot.Top - 3);
    }

    private void DrawGamutTriangle(
        Graphics graphics,
        Rectangle plot,
        Color color,
        (float X, float Y)[] xyPrimaries)
    {
        PointF[] points = xyPrimaries
            .Select(primary => XyToUv(primary.X, primary.Y))
            .Select(uv => new PointF(
                plot.Left + uv.X / DiagramMaximum * plot.Width,
                plot.Bottom - uv.Y / DiagramMaximum * plot.Height))
            .ToArray();
        using Pen outline = new(color, 2f) { LineJoin = LineJoin.Round };
        graphics.DrawPolygon(outline, points);
    }

    private void DrawLegend(Graphics graphics, int x, int y, Color color, string label, double percentage)
    {
        using SolidBrush dotBrush = new(color);
        using SolidBrush textBrush = new(Color.FromArgb(225, 230, 239));
        graphics.FillEllipse(dotBrush, x, y + 4, 9, 9);
        graphics.DrawString($"{label}  {percentage:F1}%", _bodyFont, textBrush, x + 16, y);
    }

    private void DrawLuminanceMarkers(Graphics graphics, LuminanceTelemetry telemetry)
    {
        PointF maximum = MapSourcePosition(telemetry.MaximumX, telemetry.MaximumY);
        PointF minimum = MapSourcePosition(telemetry.MinimumX, telemetry.MinimumY);
        DrawMarker(graphics, maximum, MaximumColor, Localization.T("MarkerMaximum"), telemetry.MaximumNits, true);
        DrawMarker(graphics, minimum, MinimumColor, Localization.T("MarkerMinimum"), telemetry.MinimumNits, false);
    }

    private void DrawMarker(
        Graphics graphics,
        PointF point,
        Color color,
        string label,
        double nits,
        bool labelAbove)
    {
        using Pen shadow = new(Color.Black, 5f);
        using Pen marker = new(color, 2.5f);
        graphics.DrawEllipse(shadow, point.X - 11, point.Y - 11, 22, 22);
        graphics.DrawLine(shadow, point.X - 16, point.Y, point.X + 16, point.Y);
        graphics.DrawLine(shadow, point.X, point.Y - 16, point.X, point.Y + 16);
        graphics.DrawEllipse(marker, point.X - 11, point.Y - 11, 22, 22);
        graphics.DrawLine(marker, point.X - 16, point.Y, point.X + 16, point.Y);
        graphics.DrawLine(marker, point.X, point.Y - 16, point.X, point.Y + 16);

        string value = nits < 1 ? $"{nits:F3} nits" : $"{nits:F1} nits";
        string text = $"{label}  {value}";
        SizeF textSize = graphics.MeasureString(text, _markerFont);
        float labelX = point.X + 20;
        if (labelX + textSize.Width + 18 > ClientSize.Width)
            labelX = point.X - textSize.Width - 30;
        float labelY = labelAbove ? point.Y - textSize.Height - 25 : point.Y + 18;
        labelY = Math.Clamp(labelY, 8, ClientSize.Height - textSize.Height - 18);
        RectangleF background = new(labelX - 7, labelY - 4, textSize.Width + 14, textSize.Height + 8);
        using GraphicsPath backgroundPath = CreateRoundedRectangle(Rectangle.Round(background), 7);
        using SolidBrush backgroundBrush = new(Color.FromArgb(24, 27, 34));
        using Pen outline = new(color, 1.5f);
        using SolidBrush textBrush = new(Color.White);
        graphics.FillPath(backgroundBrush, backgroundPath);
        graphics.DrawPath(outline, backgroundPath);
        graphics.DrawString(text, _markerFont, textBrush, labelX, labelY);
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

    private static Bitmap CreateHeatmap(GamutTelemetry telemetry)
    {
        Bitmap bitmap = new(telemetry.HistogramWidth, telemetry.HistogramHeight, PixelFormat.Format32bppArgb);
        uint maximum = telemetry.Histogram.Length == 0 ? 0 : telemetry.Histogram.Max();
        if (maximum == 0)
            return bitmap;

        double maximumLog = Math.Log(1.0 + maximum);
        for (int y = 0; y < telemetry.HistogramHeight; y++)
        {
            for (int x = 0; x < telemetry.HistogramWidth; x++)
            {
                uint count = telemetry.Histogram[y * telemetry.HistogramWidth + x];
                if (count == 0)
                    continue;

                double intensity = Math.Log(1.0 + count) / maximumLog;
                float u = (x + 0.5f) / telemetry.HistogramWidth * DiagramMaximum;
                float v = (y + 0.5f) / telemetry.HistogramHeight * DiagramMaximum;
                Color chromaticity = UvToSrgb(u, v);
                int alpha = (int)Math.Clamp(45 + 210 * Math.Sqrt(intensity), 0, 255);
                int red = (int)Math.Clamp(chromaticity.R * (0.35 + 0.65 * intensity), 0, 255);
                int green = (int)Math.Clamp(chromaticity.G * (0.35 + 0.65 * intensity), 0, 255);
                int blue = (int)Math.Clamp(chromaticity.B * (0.35 + 0.65 * intensity), 0, 255);
                bitmap.SetPixel(x, telemetry.HistogramHeight - 1 - y, Color.FromArgb(alpha, red, green, blue));
            }
        }
        return bitmap;
    }

    private static Color UvToSrgb(float u, float v)
    {
        if (v <= 1e-5f)
            return Color.White;
        double x = 9.0 * u / (4.0 * v);
        double y = 1.0;
        double z = (12.0 - 3.0 * u - 20.0 * v) / (4.0 * v);
        double red = 3.24096994 * x - 1.53738318 * y - 0.49861076 * z;
        double green = -0.96924364 * x + 1.87596750 * y + 0.04155506 * z;
        double blue = 0.05563008 * x - 0.20397696 * y + 1.05697151 * z;
        return Color.FromArgb(
            ToSrgbByte(red),
            ToSrgbByte(green),
            ToSrgbByte(blue));
    }

    private static int ToSrgbByte(double linear)
    {
        linear = Math.Clamp(linear, 0, 1);
        double encoded = linear <= 0.0031308
            ? linear * 12.92
            : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
        return (int)Math.Round(encoded * 255);
    }

    private static PointF XyToUv(float x, float y)
    {
        float denominator = -2f * x + 12f * y + 3f;
        return new PointF(4f * x / denominator, 9f * y / denominator);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle rectangle, int radius)
    {
        int diameter = radius * 2;
        GraphicsPath path = new();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _heatmap?.Dispose();
            _titleFont.Dispose();
            _bodyFont.Dispose();
            _smallFont.Dispose();
            _markerFont.Dispose();
            _toastFont.Dispose();
            _screenshotToastTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
