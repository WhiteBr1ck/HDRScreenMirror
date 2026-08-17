using System.Drawing.Drawing2D;
using System.Drawing.Text;
using HDRScreenMirror.Interop;

namespace HDRScreenMirror.UI;

internal sealed class StatusOverlayForm : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int HighDpiLayoutBaseline = 144;
    private const int PanelWidth = 540;
    private const int OuterPadding = 20;
    private const int OuterRadius = 20;
    private const int InnerRadius = 10;

    private static readonly Color SurfaceColor = Color.FromArgb(18, 20, 26);
    private static readonly Color RaisedSurfaceColor = Color.FromArgb(27, 30, 38);
    private static readonly Color PrimaryTextColor = Color.FromArgb(242, 245, 250);
    private static readonly Color SecondaryTextColor = Color.FromArgb(171, 181, 197);
    private static readonly Color MutedTextColor = Color.FromArgb(118, 129, 147);
    private static readonly Color AccentColor = Color.FromArgb(92, 154, 255);

    private readonly bool _mouseThrough;
    private readonly bool _excludeFromCapture;
    private readonly bool _analysisOnly;
    private readonly Rectangle _outputBounds;
    private readonly string _captureName;
    private readonly string _presentName;
    private string _route;
    private Font _titleFont = CreatePixelFont(11.33f, FontStyle.Bold);
    private Font _stateFont = CreatePixelFont(11.33f, FontStyle.Bold);
    private Font _bodyFont = CreatePixelFont(12f, FontStyle.Regular);
    private Font _sectionFont = CreatePixelFont(11.33f, FontStyle.Bold);
    private Font _captionFont = CreatePixelFont(10.67f, FontStyle.Regular);
    private Font _valueFont = CreatePixelFont(16f, FontStyle.Bold);
    private Font _footerFont = CreatePixelFont(10.67f, FontStyle.Regular);

    private bool _showPointerLuminance;
    private bool _falseColor;
    private bool _showAblEstimate;
    private string _ablProfileName = string.Empty;
    private bool _hasTelemetry;
    private string _state = string.Empty;
    private string _inputFormat = string.Empty;
    private string _inputKind = string.Empty;
    private uint _width;
    private uint _height;
    private double _framesPerSecond;
    private bool _cursorVisible;
    private LuminanceTelemetry? _luminance;
    private float _dpiScale = 1f;

    public StatusOverlayForm(
        Rectangle outputBounds,
        DisplayTarget capture,
        DisplayTarget present,
        bool mouseThrough,
        bool showPointerLuminance,
        bool falseColor,
        bool excludeFromCapture = true,
        bool analysisOnly = false)
    {
        _mouseThrough = mouseThrough;
        _excludeFromCapture = excludeFromCapture;
        _analysisOnly = analysisOnly;
        _outputBounds = outputBounds;
        _showPointerLuminance = showPointerLuminance;
        _falseColor = falseColor;
        _captureName = capture.DisplayName;
        _presentName = present.DisplayName;
        _route = CreateRouteText();

        Text = "HDRScreenMirror Status";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        int initialX = outputBounds.Left + 24;
        Bounds = new Rectangle(initialX, outputBounds.Top + 24, PanelWidth, CalculatePanelHeight());
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = SurfaceColor;
        Opacity = 0.97;
        DoubleBuffered = true;
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
        _state = telemetry.State;
        _inputFormat = telemetry.InputFormat;
        _inputKind = telemetry.InputKind;
        _width = telemetry.Width;
        _height = telemetry.Height;
        _framesPerSecond = telemetry.FramesPerSecond;
        _cursorVisible = telemetry.CursorVisible;
        Invalidate();
    }

    public void UpdateLuminance(LuminanceTelemetry telemetry)
    {
        _luminance = telemetry;
        Invalidate();
    }

    public void SetShowPointerLuminance(bool visible)
    {
        if (_showPointerLuminance == visible)
            return;

        _showPointerLuminance = visible;
        ResizeForContent();
    }

    public void SetFalseColor(bool enabled)
    {
        if (_falseColor == enabled)
            return;

        _falseColor = enabled;
        ResizeForContent();
    }

    public void SetAblEstimate(bool enabled, string profileName)
    {
        bool visibilityChanged = _showAblEstimate != enabled;
        _showAblEstimate = enabled;
        _ablProfileName = profileName;
        if (visibilityChanged)
            ResizeForContent();
        else
            Invalidate();
    }

    public void ApplyLanguage()
    {
        _route = CreateRouteText();
        Invalidate();
    }

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        ApplyDpi(DeviceDpi);
        SetCaptureExclusion(true);
        BringToFront();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs eventArgs)
    {
        base.OnDpiChanged(eventArgs);
        ApplyDpi(eventArgs.DeviceDpiNew);
    }

    internal bool SetCaptureExclusion(bool excluded)
    {
        if (!_excludeFromCapture || !IsHandleCreated)
            return !excluded;

        bool applied = NativeMethods.SetWindowDisplayAffinity(
            Handle,
            excluded ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);
        return applied;
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        UpdateRoundedRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(SurfaceColor);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        DrawPanel(eventArgs.Graphics);
    }

    internal Bitmap RenderScreenshotBitmap()
    {
        Bitmap bitmap = new(
            Math.Max(ClientSize.Width, 1),
            Math.Max(ClientSize.Height, 1),
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using GraphicsPath backgroundPath = CreateRoundedRectangle(
            new Rectangle(Point.Empty, bitmap.Size),
            ScaleToDevice(OuterRadius));
        using SolidBrush background = new(SurfaceColor);
        graphics.FillPath(background, backgroundPath);
        GraphicsState clipState = graphics.Save();
        graphics.SetClip(backgroundPath);
        DrawPanel(graphics);
        graphics.Restore(clipState);
        return bitmap;
    }

    private void DrawPanel(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        GraphicsState scaleState = graphics.Save();
        graphics.ScaleTransform(_dpiScale, _dpiScale);

        DrawHeader(graphics);
        DrawSignalSummary(graphics);
        DrawLuminanceSection(graphics);
        DrawFooter(graphics);

        Rectangle designBounds = new(0, 0, PanelWidth, CalculatePanelHeight());
        using GraphicsPath outlinePath = CreateRoundedRectangle(designBounds, OuterRadius);
        using Pen outline = new(Color.FromArgb(22, 255, 255, 255), 1);
        graphics.DrawPath(outline, outlinePath);
        graphics.Restore(scaleState);
    }

    private void DrawHeader(Graphics graphics)
    {
        using SolidBrush titleBrush = new(SecondaryTextColor);
        graphics.DrawString("HDR SCREEN MIRROR", _titleFont, titleBrush, OuterPadding, 17);

        string state = _hasTelemetry ? _state : Localization.T("OverlayStarting");
        bool paused = state.Contains("暂停", StringComparison.OrdinalIgnoreCase) ||
                      state.Contains("paused", StringComparison.OrdinalIgnoreCase) ||
                      state.Contains("waiting", StringComparison.OrdinalIgnoreCase);
        Color stateColor = paused ? Color.FromArgb(246, 186, 73) : Color.FromArgb(77, 214, 145);
        SizeF stateSize = graphics.MeasureString(state, _stateFont);
        int pillWidth = Math.Min(250, Math.Max(82, (int)Math.Ceiling(stateSize.Width) + 34));
        Rectangle pillBounds = new(PanelWidth - OuterPadding - pillWidth, 12, pillWidth, 28);
        DrawRoundedFill(graphics, pillBounds, 14, Color.FromArgb(34, stateColor));
        using SolidBrush dotBrush = new(stateColor);
        graphics.FillEllipse(dotBrush, pillBounds.Left + 11, pillBounds.Top + 10, 8, 8);
        using SolidBrush stateBrush = new(PrimaryTextColor);
        graphics.DrawString(state, _stateFont, stateBrush, pillBounds.Left + 25, pillBounds.Top + 6);
    }

    private void DrawSignalSummary(Graphics graphics)
    {
        using SolidBrush primaryBrush = new(PrimaryTextColor);
        using SolidBrush secondaryBrush = new(SecondaryTextColor);
        using SolidBrush mutedBrush = new(MutedTextColor);

        graphics.DrawString(_route, _bodyFont, primaryBrush, OuterPadding, 50);
        string signal = _hasTelemetry
            ? $"{_inputKind}  ·  {FriendlyFormat(_inputFormat)}  ·  {_width}×{_height}"
            : Localization.T("OverlayWaiting");
        graphics.DrawString(signal, _bodyFont, secondaryBrush, OuterPadding, 73);

        string performance = $"{_framesPerSecond:F1} fps   ·   {Localization.T(_cursorVisible ? "CursorOn" : "CursorOff")}";
        SizeF performanceSize = graphics.MeasureString(performance, _captionFont);
        graphics.DrawString(
            performance,
            _captionFont,
            mutedBrush,
            PanelWidth - OuterPadding - performanceSize.Width,
            75);

        using Pen divider = new(Color.FromArgb(16, 255, 255, 255), 1);
        graphics.DrawLine(divider, OuterPadding, 103, PanelWidth - OuterPadding, 103);
    }

    private void DrawLuminanceSection(Graphics graphics)
    {
        using SolidBrush sectionBrush = new(SecondaryTextColor);
        graphics.DrawString(Localization.T("OverlayFrameTitle"), _sectionFont, sectionBrush, OuterPadding, 115);

        double average = _luminance?.AverageNits ?? 0;
        double maximum = _luminance?.MaximumNits ?? 0;
        double minimum = _luminance?.MinimumNits ?? 0;
        const int cardY = 138;
        const int cardHeight = 54;
        const int gap = 8;
        int cardWidth = (PanelWidth - OuterPadding * 2 - gap * 2) / 3;
        DrawMetricCard(graphics, new Rectangle(OuterPadding, cardY, cardWidth, cardHeight), "OverlayAverage", average);
        DrawMetricCard(
            graphics,
            new Rectangle(OuterPadding + cardWidth + gap, cardY, cardWidth, cardHeight),
            "OverlayMaximum",
            maximum);
        DrawMetricCard(
            graphics,
            new Rectangle(OuterPadding + (cardWidth + gap) * 2, cardY, cardWidth, cardHeight),
            "OverlayMinimum",
            minimum);

        int nextY = 202;
        if (_showAblEstimate)
            nextY = DrawAblSection(graphics, nextY);

        if (_showPointerLuminance)
        {
            int pointerCardHeight = _showAblEstimate ? 62 : 40;
            DrawPointerCard(
                graphics,
                new Rectangle(OuterPadding, nextY, PanelWidth - OuterPadding * 2, pointerCardHeight));
            nextY += pointerCardHeight + 10;
        }

        if (_falseColor)
            DrawFalseColorLegend(graphics, nextY);
    }

    private int DrawAblSection(Graphics graphics, int y)
    {
        using SolidBrush sectionBrush = new(SecondaryTextColor);
        using SolidBrush profileBrush = new(MutedTextColor);
        graphics.DrawString(Localization.T("OverlayAblTitle"), _sectionFont, sectionBrush, OuterPadding, y);
        using StringFormat profileFormat = new()
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        Rectangle profileBounds = new(PanelWidth / 2, y, PanelWidth / 2 - OuterPadding, 20);
        graphics.DrawString(_ablProfileName, _captionFont, profileBrush, profileBounds, profileFormat);

        AblLuminanceEstimate? estimate = _luminance?.AblEstimate;
        double average = estimate?.AverageNits ?? 0;
        double maximum = estimate?.MaximumNits ?? 0;
        double minimum = estimate?.MinimumNits ?? 0;
        const int gap = 8;
        int cardWidth = (PanelWidth - OuterPadding * 2 - gap * 2) / 3;
        int cardY = y + 23;
        DrawMetricCard(
            graphics,
            new Rectangle(OuterPadding, cardY, cardWidth, 54),
            "OverlayAverage",
            average,
            Color.FromArgb(24, 34, 50));
        DrawMetricCard(
            graphics,
            new Rectangle(OuterPadding + cardWidth + gap, cardY, cardWidth, 54),
            "OverlayMaximum",
            maximum,
            Color.FromArgb(24, 34, 50));
        DrawMetricCard(
            graphics,
            new Rectangle(OuterPadding + (cardWidth + gap) * 2, cardY, cardWidth, 54),
            "OverlayMinimum",
            minimum,
            Color.FromArgb(24, 34, 50));

        Rectangle summaryBounds = new(OuterPadding, cardY + 64, PanelWidth - OuterPadding * 2, 36);
        DrawRoundedFill(graphics, summaryBounds, InnerRadius, Color.FromArgb(22, 36, 48, 70));
        double scalePercent = (estimate?.ScaleFactor ?? 0) * 100.0;
        string summary = Localization.F(
            "OverlayAblSummary",
            estimate?.EquivalentAplPercent ?? 0,
            scalePercent,
            100.0 - scalePercent);
        using SolidBrush summaryBrush = new(AccentColor);
        using StringFormat centered = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(summary, _captionFont, summaryBrush, summaryBounds, centered);
        return y + 133;
    }

    private void DrawMetricCard(
        Graphics graphics,
        Rectangle bounds,
        string captionKey,
        double value,
        Color? background = null)
    {
        DrawRoundedFill(graphics, bounds, InnerRadius, background ?? RaisedSurfaceColor);
        using Pen outline = new(Color.FromArgb(12, 255, 255, 255), 1);
        using GraphicsPath path = CreateRoundedRectangle(bounds, InnerRadius);
        graphics.DrawPath(outline, path);

        using SolidBrush captionBrush = new(MutedTextColor);
        using SolidBrush valueBrush = new(PrimaryTextColor);
        using StringFormat centered = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
        graphics.DrawString(Localization.T(captionKey), _captionFont, captionBrush, bounds, centered);

        Rectangle valueBounds = new(bounds.Left, bounds.Top + 18, bounds.Width, 29);
        string formattedValue = value is > 0 and < 1 ? $"{value:F3}" : $"{value:F1}";
        graphics.DrawString($"{formattedValue} nits", _valueFont, valueBrush, valueBounds, centered);
    }

    private void DrawPointerCard(Graphics graphics, Rectangle bounds)
    {
        DrawRoundedFill(graphics, bounds, InnerRadius, Color.FromArgb(22, 36, 48, 70));
        using SolidBrush titleBrush = new(SecondaryTextColor);
        using SolidBrush valueBrush = new(PrimaryTextColor);
        float titleY = bounds.Top + (bounds.Height - _bodyFont.Height) * 0.5f;
        graphics.DrawString(Localization.T("OverlayPointerTitle"), _bodyFont, titleBrush, bounds.Left + 12, titleY);

        using StringFormat rightAligned = new()
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center
        };
        Rectangle valueBounds = new(bounds.Left + bounds.Width / 2, bounds.Top, bounds.Width / 2 - 12, bounds.Height);
        if (_luminance is null || !_luminance.PointerInCaptureArea)
        {
            graphics.DrawString(
                Localization.T("OverlayPointerOutsideShort"),
                _bodyFont,
                valueBrush,
                valueBounds,
                rightAligned);
            return;
        }

        if (_luminance.PointerScaledNits is not double scaledNits)
        {
            graphics.DrawString(
                $"{_luminance.PointerRegionNits:F3} nits",
                _bodyFont,
                valueBrush,
                valueBounds,
                rightAligned);
            return;
        }

        using SolidBrush scaledBrush = new(AccentColor);
        using StringFormat lineFormat = new()
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Near,
            FormatFlags = StringFormatFlags.NoWrap
        };
        Rectangle originalBounds = new(valueBounds.Left, bounds.Top + 5, valueBounds.Width, 24);
        Rectangle scaledBounds = new(valueBounds.Left, bounds.Top + 33, valueBounds.Width, 24);
        graphics.DrawString(
            Localization.F("OverlayPointerOriginal", _luminance.PointerRegionNits),
            _bodyFont,
            valueBrush,
            originalBounds,
            lineFormat);
        graphics.DrawString(
            Localization.F("OverlayPointerScaled", scaledNits),
            _bodyFont,
            scaledBrush,
            scaledBounds,
            lineFormat);
    }

    private void DrawFalseColorLegend(Graphics graphics, int y)
    {
        using SolidBrush sectionBrush = new(SecondaryTextColor);
        graphics.DrawString(Localization.T("OverlayFalseColor"), _sectionFont, sectionBrush, OuterPadding, y);

        Color[] startColors =
        [
            Color.Black,
            Color.Cyan,
            Color.Lime,
            Color.Yellow,
            Color.Red,
            Color.Magenta,
            Color.White
        ];
        Color[] endColors =
        [
            Color.FromArgb(64, 64, 64),
            Color.Lime,
            Color.Yellow,
            Color.Red,
            Color.Magenta,
            Color.Blue,
            Color.White
        ];
        string[] labels = ["0–100", "100–203", "203–400", "400–1k", "1–2k", "2–4k", "4k+"];
        int legendY = y + 23;
        int legendWidth = PanelWidth - OuterPadding * 2;
        int segmentWidth = legendWidth / startColors.Length;
        Rectangle legendBounds = new(OuterPadding, legendY, legendWidth, 13);
        using GraphicsPath clipPath = CreateRoundedRectangle(legendBounds, 6);
        GraphicsState state = graphics.Save();
        graphics.SetClip(clipPath);
        for (int i = 0; i < startColors.Length; i++)
        {
            int width = i == startColors.Length - 1 ? legendWidth - segmentWidth * i : segmentWidth + 1;
            Rectangle segmentBounds = new(OuterPadding + i * segmentWidth, legendY, width, 13);
            if (startColors[i] == endColors[i])
            {
                using SolidBrush brush = new(startColors[i]);
                graphics.FillRectangle(brush, segmentBounds);
            }
            else
            {
                using LinearGradientBrush brush = new(
                    segmentBounds,
                    startColors[i],
                    endColors[i],
                    LinearGradientMode.Horizontal)
                {
                    GammaCorrection = true
                };
                graphics.FillRectangle(brush, segmentBounds);
            }
        }
        graphics.Restore(state);

        using SolidBrush labelBrush = new(MutedTextColor);
        using StringFormat centered = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
        for (int i = 0; i < labels.Length; i++)
        {
            Rectangle labelBounds = new(
                OuterPadding + i * segmentWidth,
                legendY + 16,
                i == labels.Length - 1 ? legendWidth - i * segmentWidth : segmentWidth,
                18);
            graphics.DrawString(labels[i], _footerFont, labelBrush, labelBounds, centered);
        }
    }

    private void DrawFooter(Graphics graphics)
    {
        int footerY = CalculatePanelHeight() - 29;
        using SolidBrush footerBrush = new(MutedTextColor);
        graphics.DrawString(
            Localization.T(_analysisOnly ? "OverlayHotkeysAnalysis" : "OverlayHotkeys"),
            _footerFont,
            footerBrush,
            OuterPadding,
            footerY);
    }

    private int CalculatePanelHeight()
    {
        int height = 245;
        if (_showAblEstimate)
            height += 133;
        if (_showPointerLuminance)
            height += _showAblEstimate ? 72 : 50;
        if (_falseColor)
            height += 68;
        return height;
    }

    private void ResizeForContent()
    {
        Height = ScaleToDevice(CalculatePanelHeight());
        UpdateRoundedRegion();
        Invalidate();
    }

    private void ApplyDpi(int dpi)
    {
        float newScale = Math.Max(1f, dpi / (float)HighDpiLayoutBaseline);
        _dpiScale = newScale;
        RecreateFonts(dpi);
        int panelWidth = ScaleToDevice(PanelWidth);
        int panelX = _outputBounds.Left + ScaleToDevice(24);
        Bounds = new Rectangle(
            Math.Max(_outputBounds.Left, panelX),
            _outputBounds.Top + ScaleToDevice(24),
            panelWidth,
            ScaleToDevice(CalculatePanelHeight()));
        UpdateRoundedRegion();
        Invalidate();
    }

    private void RecreateFonts(int dpi)
    {
        float pixelScale = dpi / 96f / _dpiScale;
        ReplaceFont(ref _titleFont, 11.33f * pixelScale, FontStyle.Bold);
        ReplaceFont(ref _stateFont, 11.33f * pixelScale, FontStyle.Bold);
        ReplaceFont(ref _bodyFont, 12f * pixelScale, FontStyle.Regular);
        ReplaceFont(ref _sectionFont, 11.33f * pixelScale, FontStyle.Bold);
        ReplaceFont(ref _captionFont, 10.67f * pixelScale, FontStyle.Regular);
        ReplaceFont(ref _valueFont, 16f * pixelScale, FontStyle.Bold);
        ReplaceFont(ref _footerFont, 10.67f * pixelScale, FontStyle.Regular);
    }

    private static void ReplaceFont(ref Font font, float size, FontStyle style)
    {
        Font oldFont = font;
        font = CreatePixelFont(size, style);
        oldFont.Dispose();
    }

    private int ScaleToDevice(int logicalPixels) =>
        Math.Max(1, (int)Math.Round(logicalPixels * _dpiScale));

    private void UpdateRoundedRegion()
    {
        if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0)
            return;

        using GraphicsPath path = CreateRoundedRectangle(ClientRectangle, ScaleToDevice(OuterRadius));
        Region?.Dispose();
        Region = new Region(path);
    }

    private static Font CreatePixelFont(float size, FontStyle style) =>
        new("Segoe UI", size, style, GraphicsUnit.Pixel);

    private static void DrawRoundedFill(Graphics graphics, Rectangle bounds, int radius, Color color)
    {
        using GraphicsPath path = CreateRoundedRectangle(bounds, radius);
        using SolidBrush brush = new(color);
        graphics.FillPath(brush, path);
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

    private static string FriendlyFormat(string format) => format switch
    {
        "R16G16B16A16_Float" => "FP16 scRGB",
        "R10G10B10A2_UNorm" => "RGB10 PQ",
        "B8G8R8A8_UNorm" => "BGRA8 SDR",
        _ => format
    };

    private string CreateRouteText() => _analysisOnly
        ? Localization.F("SingleDisplayRoute", _captureName)
        : $"{_captureName}  →  {_presentName}";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _stateFont.Dispose();
            _bodyFont.Dispose();
            _sectionFont.Dispose();
            _captionFont.Dispose();
            _valueFont.Dispose();
            _footerFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
