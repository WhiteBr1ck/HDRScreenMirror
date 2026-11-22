using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using HDRScreenMirror.Interop;

namespace HDRScreenMirror.UI;

internal sealed class CieOverlayForm : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int LayoutBaselineDpi = 144;
    private const float DesignFontScale = LayoutBaselineDpi / 96f;

    private const int PanelWidth = 488;
    private const int PanelHeight = 564;
    private const int OuterPadding = 24;
    private const int OuterRadius = 12;
    private const float HairlineWidth = 0.5f;
    private const float DiagramMaximum = 0.65f;

    private readonly bool _excludeFromCapture;
    private readonly Rectangle _outputBounds;

    // 2 font sizes (12px and 13.5px at 100% DPI -> 1.5x at 144 DPI).
    private readonly Font _smallFont = CreatePixelFont(12f * DesignFontScale, FontStyle.Regular);
    private readonly Font _smallSemiboldFont = CreatePixelFont(12f * DesignFontScale, FontStyle.Bold);
    private readonly Font _titleFont = CreatePixelFont(13.5f * DesignFontScale, FontStyle.Bold);

    private OverlayAppearance _appearance = OverlayAppearance.Default;
    private bool _glassActive;
    private GamutTelemetry? _gamut;
    private Bitmap? _heatmap;
    private float _dpiScale = 1f;
    private int _currentDpi = LayoutBaselineDpi;

    private Color SurfaceColor => _appearance.BackgroundColor;
    private Color BorderColor => _appearance.BorderColor;
    private Color DividerColor => _appearance.DividerColor;
    private Color PrimaryTextColor => _appearance.PrimaryTextColor;
    private Color SecondaryTextColor => _appearance.SecondaryTextColor;
    private Color MutedTextColor => _appearance.MutedTextColor;

    private Color SrgbSwatchColor => _appearance.IsLightSurface
        ? Color.FromArgb(2, 132, 199)
        : Color.FromArgb(122, 199, 232);
    private Color DciP3SwatchColor => _appearance.IsLightSurface
        ? Color.FromArgb(217, 119, 6)
        : Color.FromArgb(255, 199, 56);
    private Color Bt2020SwatchColor => _appearance.IsLightSurface
        ? Color.FromArgb(147, 51, 234)
        : Color.FromArgb(224, 92, 255);
    private Color OutsideSwatchColor => _appearance.IsLightSurface
        ? Color.FromArgb(220, 38, 38)
        : Color.FromArgb(255, 66, 61);

    public CieOverlayForm(Rectangle outputBounds, bool excludeFromCapture = true)
    {
        _outputBounds = outputBounds;
        _excludeFromCapture = excludeFromCapture;

        Text = "HDRScreenMirror CIE";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = ComputeBounds();
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 1.0;
        DoubleBuffered = true;
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

    internal void UpdateGamut(GamutTelemetry telemetry)
    {
        _gamut = telemetry;
        Bitmap? previous = _heatmap;
        _heatmap = CreateHeatmapFast(telemetry);
        previous?.Dispose();
        Invalidate();
    }

    internal void ApplyLanguage() => Invalidate();

    internal void ApplyAppearance(OverlayAppearance appearance)
    {
        bool scaleChanged = Math.Abs(_appearance.Scale - appearance.Scale) > 0.001;
        _appearance = appearance;
        if (IsHandleCreated)
            ApplyWindowBackdrop();
        if (scaleChanged)
            ApplyDpi(_currentDpi);
        else
            Invalidate();
    }

    internal bool SetCaptureExclusion(bool excluded)
    {
        if (!_excludeFromCapture || !IsHandleCreated)
            return !excluded;

        return NativeMethods.SetWindowDisplayAffinity(
            Handle,
            excluded ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowBackdrop();
    }

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        ApplyDpi(DeviceDpi);
        ApplyWindowBackdrop();
        SetCaptureExclusion(true);
        BringToFront();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs eventArgs)
    {
        base.OnDpiChanged(eventArgs);
        ApplyDpi(eventArgs.DeviceDpiNew);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!_glassActive)
            UpdateRoundedRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (!_glassActive)
            e.Graphics.Clear(SurfaceColor);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        if (_glassActive)
        {
            using Bitmap surface = new(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(surface))
            {
                g.Clear(Color.Transparent);
                DrawPanel(g, drawOpaqueBackground: false, drawLiquidGlassSheen: true);
            }
            NativeMethods.BlitPremultipliedBitmap(eventArgs.Graphics, surface);
            return;
        }

        base.OnPaint(eventArgs);
        DrawPanel(eventArgs.Graphics, drawOpaqueBackground: true, drawLiquidGlassSheen: false);
    }

    internal Bitmap RenderScreenshotBitmap()
    {
        Bitmap bitmap = new(
            Math.Max(ClientSize.Width, 1),
            Math.Max(ClientSize.Height, 1),
            PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using GraphicsPath backgroundPath = CreateRoundedRectangle(
            new RectangleF(0, 0, bitmap.Width, bitmap.Height),
            ScaleToDevice(OuterRadius));
        int bgAlpha = _appearance.GlassEnabled
            ? (int)Math.Clamp(Math.Round(_appearance.Opacity * 224.0), 110, 245)
            : 255;
        using SolidBrush background = new(Color.FromArgb(bgAlpha, SurfaceColor));
        graphics.FillPath(background, backgroundPath);
        GraphicsState clipState = graphics.Save();
        graphics.SetClip(backgroundPath);
        DrawPanel(graphics, drawOpaqueBackground: false, drawLiquidGlassSheen: _appearance.GlassEnabled);
        graphics.Restore(clipState);
        return bitmap;
    }

    private void ApplyWindowBackdrop()
    {
        if (!IsHandleCreated)
            return;

        if (_appearance.GlassEnabled)
        {
            if (Region is not null)
            {
                Region.Dispose();
                Region = null;
            }
            BackColor = Color.Black;
            Opacity = 1.0;
            _glassActive = NativeMethods.TryApplyAcrylicGlass(
                Handle,
                enabled: true,
                _appearance.BackgroundColor,
                _appearance.Opacity);
            if (!_glassActive)
            {
                BackColor = SurfaceColor;
                Opacity = _appearance.Opacity;
                UpdateRoundedRegion();
            }
        }
        else
        {
            NativeMethods.TryApplyAcrylicGlass(Handle, enabled: false, _appearance.BackgroundColor, _appearance.Opacity);
            _glassActive = false;
            BackColor = SurfaceColor;
            Opacity = _appearance.Opacity;
            UpdateRoundedRegion();
        }
        Invalidate();
    }

    private void ApplyDpi(int dpi)
    {
        _currentDpi = Math.Max(72, dpi);
        _dpiScale = Math.Max(0.35f, (_currentDpi / (float)LayoutBaselineDpi) * (float)_appearance.Scale);
        Bounds = ComputeBounds();
        if (!_glassActive)
            UpdateRoundedRegion();
        Invalidate();
    }

    private Rectangle ComputeBounds()
    {
        int width = ScaleToDevice(PanelWidth);
        int height = ScaleToDevice(PanelHeight);
        int margin = ScaleToDevice(24);
        int x = _outputBounds.Left + margin;
        int y = Math.Max(_outputBounds.Top + margin, _outputBounds.Bottom - height - margin);
        return new Rectangle(x, y, width, height);
    }

    private void DrawPanel(Graphics graphics, bool drawOpaqueBackground, bool drawLiquidGlassSheen)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = drawLiquidGlassSheen
            ? TextRenderingHint.AntiAliasGridFit
            : TextRenderingHint.ClearTypeGridFit;

        GraphicsState state = graphics.Save();
        graphics.ScaleTransform(_dpiScale, _dpiScale);

        if (drawOpaqueBackground)
        {
            using SolidBrush bg = new(SurfaceColor);
            graphics.FillRectangle(bg, 0, 0, PanelWidth, PanelHeight);
        }

        if (drawLiquidGlassSheen)
        {
            Color sheenTop = _appearance.IsLightSurface
                ? Color.FromArgb(64, 255, 255, 255)
                : Color.FromArgb(30, 255, 255, 255);
            RectangleF sheenRect = new(0, 0, PanelWidth, 46f);
            using LinearGradientBrush sheenBrush = new(sheenRect, sheenTop, Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical);
            graphics.FillRectangle(sheenBrush, sheenRect);

            float dwmCornerRadius = Math.Max(4f, 8f / Math.Max(0.35f, _dpiScale));
            RectangleF rimBounds = new(0.4f, 0.4f, PanelWidth - 0.8f, PanelHeight - 0.8f);
            using GraphicsPath rimPath = CreateRoundedRectangle(rimBounds, dwmCornerRadius);
            using Pen glassBorder = new(BorderColor, HairlineWidth);
            graphics.DrawPath(glassBorder, rimPath);
        }

        const float contentWidth = PanelWidth - OuterPadding * 2f;
        using StringFormat typographic = CreateTypographicFormat();
        using SolidBrush primaryBrush = new(PrimaryTextColor);
        using SolidBrush mutedBrush = new(MutedTextColor);

        // Header with semantic micro-square accent
        DrawMicroSquare(graphics, OuterPadding, 20f + (_titleFont.Height - 6f) * 0.5f, 6f, _appearance.EffectiveAccentColor);
        graphics.DrawString("CIE 1976 UCS", _titleFont, primaryBrush, OuterPadding + 13f, 20f, typographic);

        string statusText = _gamut is null
            ? Localization.T("CieWaiting")
            : Localization.F("CieAnalyzedPixels", _gamut.AnalyzedPixels);
        SizeF statusSize = graphics.MeasureString(statusText, _smallFont, 220, typographic);
        float statusY = 20f + (_titleFont.Height - _smallFont.Height) * 0.5f + 0.5f;
        graphics.DrawString(statusText, _smallFont, mutedBrush, PanelWidth - OuterPadding - statusSize.Width, statusY, typographic);

        using Pen divider = new(DividerColor, HairlineWidth);
        graphics.DrawLine(divider, OuterPadding, 56f, PanelWidth - OuterPadding, 56f);

        // Dedicated non-overlapping margins for v' (top-left above plot), Y ticks (left), X ticks (bottom), u' (bottom-right)
        const float plotLeft = 68f;
        const float plotTop = 86f;
        const float plotSize = 360f;
        RectangleF plot = new(plotLeft, plotTop, plotSize, plotSize);

        // Soft recessed glass plot surface (avoids harsh solid black brick on light glass)
        Color plotBg = _appearance.IsLightSurface
            ? Color.FromArgb(_appearance.GlassEnabled ? 175 : 225, 18, 20, 26)
            : Color.FromArgb(_appearance.GlassEnabled ? 128 : 220, 8, 10, 14);
        using GraphicsPath plotClip = CreateRoundedRectangle(plot, 6f);
        using SolidBrush plotBrush = new(plotBg);
        graphics.FillPath(plotBrush, plotClip);

        GraphicsState plotState = graphics.Save();
        graphics.SetClip(plotClip);
        DrawCieGridLines(graphics, plot);
        if (_heatmap is not null)
        {
            InterpolationMode prevMode = graphics.InterpolationMode;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.DrawImage(_heatmap, plot);
            graphics.InterpolationMode = prevMode;
        }
        DrawGamutTriangle(graphics, plot, Color.FromArgb(122, 199, 232), [(0.64f, 0.33f), (0.30f, 0.60f), (0.15f, 0.06f)]);
        DrawGamutTriangle(graphics, plot, Color.FromArgb(255, 199, 56), [(0.68f, 0.32f), (0.265f, 0.69f), (0.15f, 0.06f)]);
        DrawGamutTriangle(graphics, plot, Color.FromArgb(224, 92, 255), [(0.708f, 0.292f), (0.170f, 0.797f), (0.131f, 0.046f)]);
        graphics.Restore(plotState);

        using Pen plotBorder = new(BorderColor, HairlineWidth);
        graphics.DrawPath(plotBorder, plotClip);
        DrawCieAxisLabels(graphics, plot);

        float legendDividerY = 482f;
        graphics.DrawLine(divider, OuterPadding, legendDividerY, PanelWidth - OuterPadding, legendDividerY);

        double srgb = _gamut?.SrgbPercentage ?? 0;
        double dciP3 = _gamut?.DciP3Percentage ?? 0;
        double bt2020 = _gamut?.Bt2020Percentage ?? 0;
        double outside = _gamut?.OutsidePercentage ?? 0;

        const float colGap = 28f;
        float colWidth = (contentWidth - colGap) * 0.5f;
        float leftColX = OuterPadding;
        float rightColX = OuterPadding + colWidth + colGap;
        float row1Y = 498f;
        float row2Y = 528f;

        DrawLegendCell(graphics, leftColX, row1Y, colWidth, SrgbSwatchColor, "sRGB", srgb);
        DrawLegendCell(graphics, rightColX, row1Y, colWidth, DciP3SwatchColor, "DCI-P3", dciP3);
        DrawLegendCell(graphics, leftColX, row2Y, colWidth, Bt2020SwatchColor, "BT.2020", bt2020);
        DrawLegendCell(graphics, rightColX, row2Y, colWidth, OutsideSwatchColor, Localization.T("CieOutside"), outside);

        if (!_glassActive)
        {
            RectangleF designBounds = new(0.25f, 0.25f, PanelWidth - 0.5f, PanelHeight - 0.5f);
            using GraphicsPath outlinePath = CreateRoundedRectangle(designBounds, OuterRadius);
            using Pen outline = new(BorderColor, HairlineWidth);
            graphics.DrawPath(outline, outlinePath);
        }

        graphics.Restore(state);
    }

    private static void DrawCieGridLines(Graphics graphics, RectangleF plot)
    {
        using Pen gridPen = new(Color.FromArgb(26, 255, 255, 255), HairlineWidth);
        for (float value = 0.1f; value <= 0.6f; value += 0.1f)
        {
            float x = plot.Left + value / DiagramMaximum * plot.Width;
            float y = plot.Bottom - value / DiagramMaximum * plot.Height;
            graphics.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
        }
    }

    private void DrawCieAxisLabels(Graphics graphics, RectangleF plot)
    {
        using SolidBrush labelBrush = new(MutedTextColor);
        using SolidBrush axisBrush = new(SecondaryTextColor);
        using StringFormat centered = CreateTypographicFormat(StringAlignment.Center);
        using StringFormat rightAligned = CreateTypographicFormat(StringAlignment.Far);
        using StringFormat leftAligned = CreateTypographicFormat(StringAlignment.Near);

        for (float value = 0.1f; value <= 0.6f; value += 0.1f)
        {
            float x = plot.Left + value / DiagramMaximum * plot.Width;
            float y = plot.Bottom - value / DiagramMaximum * plot.Height;
            string tick = value.ToString("F1");

            // Generous 44px width so ticks never clip or collide.
            graphics.DrawString(tick, _smallFont, labelBrush, new RectangleF(x - 22f, plot.Bottom + 7f, 44f, 20f), centered);
            graphics.DrawString(tick, _smallFont, labelBrush, new RectangleF(plot.Left - 46f, y - _smallFont.Height * 0.5f, 38f, 20f), rightAligned);
        }

        // Place v' cleanly above the Y axis and u' cleanly to the right of the X axis — zero collision with 0.6!
        graphics.DrawString("v′", _smallSemiboldFont, axisBrush, new RectangleF(plot.Left - 46f, plot.Top - 22f, 38f, 20f), rightAligned);
        graphics.DrawString("u′", _smallSemiboldFont, axisBrush, plot.Right + 10f, plot.Bottom - _smallSemiboldFont.Height * 0.5f, leftAligned);
    }

    private static void DrawGamutTriangle(
        Graphics graphics,
        RectangleF plot,
        Color color,
        (float X, float Y)[] xyPrimaries)
    {
        PointF[] points = xyPrimaries
            .Select(primary => XyToUv(primary.X, primary.Y))
            .Select(uv => new PointF(
                plot.Left + uv.X / DiagramMaximum * plot.Width,
                plot.Bottom - uv.Y / DiagramMaximum * plot.Height))
            .ToArray();
        using Pen outline = new(color, 1.5f) { LineJoin = LineJoin.Round };
        graphics.DrawPolygon(outline, points);
    }

    private void DrawLegendCell(
        Graphics graphics,
        float x,
        float y,
        float width,
        Color color,
        string label,
        double percentage)
    {
        const float swatchSize = 6f;
        float swatchY = y + (_smallFont.Height - swatchSize) * 0.5f;
        DrawMicroSquare(graphics, x, swatchY, swatchSize, color);

        using StringFormat typographic = CreateTypographicFormat();
        using SolidBrush labelBrush = new(SecondaryTextColor);
        using SolidBrush valueBrush = new(PrimaryTextColor);
        graphics.DrawString(label, _smallFont, labelBrush, x + swatchSize + 8f, y, typographic);

        string percentText = $"{percentage:F1}%";
        SizeF percentSize = graphics.MeasureString(percentText, _smallSemiboldFont, 90, typographic);
        graphics.DrawString(percentText, _smallSemiboldFont, valueBrush, x + width - percentSize.Width, y, typographic);
    }

    private static unsafe Bitmap CreateHeatmapFast(GamutTelemetry telemetry)
    {
        int width = telemetry.HistogramWidth;
        int height = telemetry.HistogramHeight;
        Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        uint maximum = 0;
        uint[] histogram = telemetry.Histogram;
        for (int i = 0; i < histogram.Length; i++)
        {
            if (histogram[i] > maximum)
                maximum = histogram[i];
        }
        if (maximum == 0)
            return bitmap;

        double maximumLog = Math.Log(1.0 + maximum);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte* basePtr = (byte*)data.Scan0;
            for (int y = 0; y < height; y++)
            {
                byte* rowPtr = basePtr + (height - 1 - y) * data.Stride;
                int histRow = y * width;
                float v = (y + 0.5f) / height * DiagramMaximum;
                for (int x = 0; x < width; x++)
                {
                    uint count = histogram[histRow + x];
                    if (count == 0)
                        continue;

                    double intensity = Math.Log(1.0 + count) / maximumLog;
                    float u = (x + 0.5f) / width * DiagramMaximum;
                    Color chromaticity = UvToSrgb(u, v);
                    double scale = 0.40 + 0.60 * intensity;
                    byte* px = rowPtr + x * 4;
                    px[0] = (byte)Math.Clamp((int)(chromaticity.B * scale), 0, 255);
                    px[1] = (byte)Math.Clamp((int)(chromaticity.G * scale), 0, 255);
                    px[2] = (byte)Math.Clamp((int)(chromaticity.R * scale), 0, 255);
                    px[3] = (byte)Math.Clamp((int)(55 + 200 * Math.Sqrt(intensity)), 0, 255);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
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
        return Color.FromArgb(ToSrgbByte(red), ToSrgbByte(green), ToSrgbByte(blue));
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

    private static void DrawMicroSquare(Graphics graphics, float x, float y, float size, Color color)
    {
        using GraphicsPath path = CreateRoundedRectangle(new RectangleF(x, y, size, size), 1.5f);
        using SolidBrush brush = new(color);
        graphics.FillPath(brush, path);
    }

    private int ScaleToDevice(int logicalPixels) =>
        Math.Max(1, (int)Math.Round(logicalPixels * _dpiScale));

    private void UpdateRoundedRegion()
    {
        if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0)
            return;

        using GraphicsPath path = CreateRoundedRectangle(
            new RectangleF(0, 0, ClientRectangle.Width, ClientRectangle.Height),
            ScaleToDevice(OuterRadius));
        Region?.Dispose();
        Region = new Region(path);
    }

    private static Font CreatePixelFont(float size, FontStyle style) =>
        new("Segoe UI", size, style, GraphicsUnit.Pixel);

    private static StringFormat CreateTypographicFormat(StringAlignment alignment = StringAlignment.Near) => new(StringFormat.GenericTypographic)
    {
        Alignment = alignment,
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
            _heatmap?.Dispose();
            _smallFont.Dispose();
            _smallSemiboldFont.Dispose();
            _titleFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
