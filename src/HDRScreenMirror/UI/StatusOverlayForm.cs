using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using HDRScreenMirror.DirectX;
using HDRScreenMirror.Interop;

namespace HDRScreenMirror.UI;

internal sealed class StatusOverlayForm : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int LayoutBaselineDpi = 144;
    private const float DesignFontScale = LayoutBaselineDpi / 96f;

    private const int PanelWidth = 528;
    private const int OuterPadding = 24;
    private const int ContentWidth = PanelWidth - OuterPadding * 2;
    private const int OuterRadius = 12;
    private const int LegendBarRadius = 3;
    private const float HairlineWidth = 0.5f;

    // Restrained semantic micro-accent colors matching the analysis overlay.
    private static readonly Color PausedDotColor = Color.FromArgb(242, 180, 73);
    private static readonly Color MaxAccentColor = Color.FromArgb(255, 102, 92);
    private static readonly Color MinAccentColor = Color.FromArgb(92, 198, 245);
    private static readonly Color PointerAccentColor = Color.FromArgb(63, 209, 132);
    private static readonly Color HdrAccentColor = Color.FromArgb(255, 199, 56);

    private OverlayAppearance _appearance = OverlayAppearance.Default;
    private bool _glassActive;
    private Color SurfaceColor => _appearance.BackgroundColor;
    private Color BorderColor => _appearance.BorderColor;
    private Color DividerColor => _appearance.DividerColor;
    private Color PrimaryTextColor => _appearance.PrimaryTextColor;
    private Color SecondaryTextColor => _appearance.SecondaryTextColor;
    private Color MutedTextColor => _appearance.MutedTextColor;
    private Color AccentTextColor => _appearance.EffectiveAccentColor;

    private readonly bool _mouseThrough;
    private readonly bool _excludeFromCapture;
    private readonly bool _analysisOnly;
    private readonly Rectangle _outputBounds;
    private readonly string _captureName;
    private readonly string _presentName;
    private string _route;

    // Strictly 3 font sizes (12px, 13.5px, 18px at 100% DPI -> 1.5x for 144 DPI design space).
    private readonly Font _smallFont = CreatePixelFont(12f * DesignFontScale, FontStyle.Regular);
    private readonly Font _smallSemiboldFont = CreatePixelFont(12f * DesignFontScale, FontStyle.Bold);
    private readonly Font _bodyFont = CreatePixelFont(13.5f * DesignFontScale, FontStyle.Bold);
    private readonly Font _valueFont = CreatePixelFont(18f * DesignFontScale, FontStyle.Bold);
    private readonly float _unitBaselineOffset;

    private bool _showPointerLuminance;
    private FalseColorMode _falseColorMode;
    private bool _showAblEstimate;
    private string _ablProfileName = string.Empty;
    private bool _hasTelemetry;
    private string _state = string.Empty;
    private string _inputFormat = string.Empty;
    private string _inputKind = string.Empty;
    private uint _width;
    private uint _height;
    private double _framesPerSecond;
    private LuminanceTelemetry? _luminance;
    private float _dpiScale = 1f;
    private int _currentDpi = LayoutBaselineDpi;

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
        _falseColorMode = falseColor ? FalseColorMode.Luminance : FalseColorMode.None;
        _captureName = capture.DisplayName;
        _presentName = present.DisplayName;
        _route = CreateRouteText();
        _unitBaselineOffset = CalculateBaselineOffset(_valueFont, _smallFont);

        Text = "HDRScreenMirror Status";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        int initialX = outputBounds.Left + 24;
        Bounds = new Rectangle(initialX, outputBounds.Top + 24, PanelWidth, CalculatePanelHeight());
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

    public void SetFalseColor(bool enabled) =>
        SetFalseColorMode(enabled ? FalseColorMode.Luminance : FalseColorMode.None);

    public void SetFalseColorMode(FalseColorMode mode)
    {
        if (_falseColorMode == mode)
            return;

        _falseColorMode = mode;
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

    public void ApplyAppearance(OverlayAppearance appearance)
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

    internal bool SetCaptureExclusion(bool excluded)
    {
        if (!_excludeFromCapture || !IsHandleCreated)
            return !excluded;

        return NativeMethods.SetWindowDisplayAffinity(
            Handle,
            excluded ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        if (!_glassActive)
            UpdateRoundedRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        if (!_glassActive)
            eventArgs.Graphics.Clear(SurfaceColor);
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

    private void DrawPanel(Graphics graphics, bool drawOpaqueBackground, bool drawLiquidGlassSheen)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = drawLiquidGlassSheen
            ? TextRenderingHint.AntiAliasGridFit
            : TextRenderingHint.ClearTypeGridFit;
        GraphicsState scaleState = graphics.Save();
        graphics.ScaleTransform(_dpiScale, _dpiScale);

        int panelHeight = CalculatePanelHeight();
        if (drawOpaqueBackground)
        {
            using SolidBrush bg = new(SurfaceColor);
            graphics.FillRectangle(bg, 0, 0, PanelWidth, panelHeight);
        }

        if (drawLiquidGlassSheen)
            DrawLiquidGlassHighlights(graphics, panelHeight);

        int y = DrawHeader(graphics);
        y = DrawFrameLuminanceSection(graphics, y);
        if (_showAblEstimate)
            y = DrawAblSection(graphics, y);
        if (_showPointerLuminance)
            y = DrawPointerSection(graphics, y);
        if (_falseColorMode == FalseColorMode.Luminance)
            DrawLuminanceFalseColorLegend(graphics, y);
        else if (_falseColorMode == FalseColorMode.Gamut)
            DrawGamutFalseColorLegend(graphics, y);

        if (!_glassActive)
        {
            RectangleF designBounds = new(0.25f, 0.25f, PanelWidth - 0.5f, panelHeight - 0.5f);
            using GraphicsPath outlinePath = CreateRoundedRectangle(designBounds, OuterRadius);
            using Pen outline = new(BorderColor, HairlineWidth);
            graphics.DrawPath(outline, outlinePath);
        }

        graphics.Restore(scaleState);
    }

    private void DrawLiquidGlassHighlights(Graphics graphics, int panelHeight)
    {
        Color sheenTop = _appearance.IsLightSurface
            ? Color.FromArgb(64, 255, 255, 255)
            : Color.FromArgb(30, 255, 255, 255);
        RectangleF sheenRect = new(0, 0, PanelWidth, Math.Min(46, panelHeight));
        using LinearGradientBrush sheenBrush = new(sheenRect, sheenTop, Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical);
        graphics.FillRectangle(sheenBrush, sheenRect);

        // Match Windows 11 DWM rounded corner radius cleanly without sharp corner clipping seams.
        float dwmCornerRadius = Math.Max(4f, 8f / Math.Max(0.35f, _dpiScale));
        RectangleF rimBounds = new(0.4f, 0.4f, PanelWidth - 0.8f, panelHeight - 0.8f);
        using GraphicsPath rimPath = CreateRoundedRectangle(rimBounds, dwmCornerRadius);
        using Pen glassBorder = new(BorderColor, HairlineWidth);
        graphics.DrawPath(glassBorder, rimPath);
    }

    private int DrawHeader(Graphics graphics)
    {
        bool paused = _hasTelemetry &&
            (_state.Contains("暂停", StringComparison.OrdinalIgnoreCase) ||
             _state.Contains("paused", StringComparison.OrdinalIgnoreCase) ||
             _state.Contains("waiting", StringComparison.OrdinalIgnoreCase));

        using StringFormat typographic = CreateTypographicFormat();
        using SolidBrush primaryBrush = new(PrimaryTextColor);
        using SolidBrush secondaryBrush = new(SecondaryTextColor);
        using SolidBrush mutedBrush = new(MutedTextColor);

        string rightText = paused
            ? _state
            : _hasTelemetry
                ? $"{_framesPerSecond:F1} fps"
                : string.Empty;
        float rightWidth = string.IsNullOrEmpty(rightText)
            ? 0f
            : graphics.MeasureString(rightText, _smallSemiboldFont, 200, typographic).Width;
        float reservedRight = rightWidth + (paused ? 16f : 0f);
        float routeMaxWidth = Math.Max(140f, ContentWidth - reservedRight - 16f);

        using StringFormat routeFormat = CreateTypographicFormat(ellipsis: true);
        RectangleF routeBounds = new(OuterPadding, 20f, routeMaxWidth, 26f);
        graphics.DrawString(_route, _bodyFont, primaryBrush, routeBounds, routeFormat);

        if (!string.IsNullOrEmpty(rightText))
        {
            float rightX = PanelWidth - OuterPadding - rightWidth;
            float rightY = 20f + (_bodyFont.Height - _smallSemiboldFont.Height) * 0.5f + 0.5f;
            if (paused)
            {
                DrawMicroSquare(graphics, rightX - 12f, rightY + (_smallSemiboldFont.Height - 6f) * 0.5f, 6f, PausedDotColor);
            }
            graphics.DrawString(rightText, _smallSemiboldFont, secondaryBrush, rightX, rightY, typographic);
        }

        if (_hasTelemetry)
        {
            bool isHdr = _inputKind.Contains("HDR", StringComparison.OrdinalIgnoreCase);
            float sigX = OuterPadding;
            float sigY = 52f;
            DrawMicroSquare(graphics, sigX, sigY + (_smallFont.Height - 6f) * 0.5f, 6f, isHdr ? HdrAccentColor : MutedTextColor);
            sigX += 12f;
            graphics.DrawString(_inputKind, _smallSemiboldFont, isHdr ? primaryBrush : secondaryBrush, sigX, sigY, typographic);
            float kindW = graphics.MeasureString(_inputKind, _smallSemiboldFont, 120, typographic).Width;
            string rest = $"{FriendlyFormat(_inputFormat)}   {_width}×{_height}";
            graphics.DrawString(rest, _smallFont, mutedBrush, sigX + kindW + 12f, sigY, typographic);
        }
        else
        {
            graphics.DrawString(Localization.T("OverlayWaiting"), _smallFont, mutedBrush, OuterPadding, 52f, typographic);
        }

        return 86;
    }

    private int DrawFrameLuminanceSection(Graphics graphics, int y)
    {
        DrawDivider(graphics, y);
        y += 16;

        DrawSectionHeader(graphics, OuterPadding, y, Localization.T("OverlayFrameTitle"), PrimaryTextColor);

        DrawThreeColumnLuminance(
            graphics,
            y + 28,
            _luminance?.AverageNits ?? 0,
            _luminance?.MaximumNits ?? 0,
            _luminance?.MinimumNits ?? 0,
            showSemanticColumnAccents: true);

        return y + 88;
    }

    private int DrawAblSection(Graphics graphics, int y)
    {
        DrawDivider(graphics, y);
        y += 16;

        using StringFormat typographic = CreateTypographicFormat();
        using StringFormat rightEllipsis = CreateTypographicFormat(StringAlignment.Far, ellipsis: true);
        using SolidBrush mutedBrush = new(MutedTextColor);
        using SolidBrush secondaryBrush = new(SecondaryTextColor);
        using SolidBrush accentBrush = new(AccentTextColor);

        DrawSectionHeader(graphics, OuterPadding, y, Localization.T("OverlayAblTitle"), AccentTextColor);
        RectangleF profileBounds = new(OuterPadding + ContentWidth * 0.35f, y, ContentWidth * 0.65f, 20f);
        graphics.DrawString(_ablProfileName, _smallFont, mutedBrush, profileBounds, rightEllipsis);

        AblLuminanceEstimate? estimate = _luminance?.AblEstimate;
        DrawThreeColumnLuminance(
            graphics,
            y + 28,
            estimate?.AverageNits ?? 0,
            estimate?.MaximumNits ?? 0,
            estimate?.MinimumNits ?? 0,
            showSemanticColumnAccents: false);

        float subY = y + 88;
        float colWidth = ContentWidth / 3f;
        double aplPercent = estimate?.EquivalentAplPercent ?? 0;
        if (estimate?.UsesAplEotf == true)
        {
            double areaPercent = estimate.EffectiveAreaPercent ?? aplPercent;
            DrawInlineStat(graphics, OuterPadding, subY, Localization.T("OverlayAblAplLabel"), $"{aplPercent:F1}%", accentBrush, mutedBrush, typographic);
            DrawInlineStat(graphics, OuterPadding + colWidth, subY, Localization.T("OverlayAblAreaLabel"), $"{areaPercent:F1}%", secondaryBrush, mutedBrush, typographic);
            DrawInlineStat(graphics, OuterPadding + colWidth * 2f, subY, Localization.T("OverlayAblWindowPeakLabel"), $"{estimate.AplPeakNits:F0}", secondaryBrush, mutedBrush, typographic);
        }
        else
        {
            double scalePercent = (estimate?.ScaleFactor ?? 0) * 100.0;
            double reductionPercent = Math.Max(0, 100.0 - scalePercent);
            DrawInlineStat(graphics, OuterPadding, subY, Localization.T("OverlayAblAplLabel"), $"{aplPercent:F1}%", accentBrush, mutedBrush, typographic);
            DrawInlineStat(graphics, OuterPadding + colWidth, subY, Localization.T("OverlayAblScaleLabel"), $"{scalePercent:F1}%", secondaryBrush, mutedBrush, typographic);
            DrawInlineStat(graphics, OuterPadding + colWidth * 2f, subY, Localization.T("OverlayAblReductionLabel"), $"{reductionPercent:F1}%", secondaryBrush, mutedBrush, typographic);
        }

        return y + 120;
    }

    private void DrawSectionHeader(Graphics graphics, float x, float y, string title, Color accentSquareColor)
    {
        using StringFormat typographic = CreateTypographicFormat();
        using SolidBrush brush = new(SecondaryTextColor);
        const float sq = 5f;
        DrawMicroSquare(graphics, x, y + (_smallSemiboldFont.Height - sq) * 0.5f, sq, accentSquareColor);
        graphics.DrawString(title, _smallSemiboldFont, brush, x + sq + 7f, y, typographic);
    }

    private void DrawThreeColumnLuminance(
        Graphics graphics,
        float y,
        double average,
        double maximum,
        double minimum,
        bool showSemanticColumnAccents)
    {
        float colWidth = ContentWidth / 3f;
        DrawLuminanceColumn(graphics, OuterPadding, y, "OverlayAverage", average, showSemanticColumnAccents ? Color.FromArgb(180, SecondaryTextColor) : null);
        DrawLuminanceColumn(graphics, OuterPadding + colWidth, y, "OverlayMaximum", maximum, showSemanticColumnAccents ? MaxAccentColor : null);
        DrawLuminanceColumn(graphics, OuterPadding + colWidth * 2f, y, "OverlayMinimum", minimum, showSemanticColumnAccents ? MinAccentColor : null);
    }

    private void DrawLuminanceColumn(Graphics graphics, float x, float y, string labelKey, double value, Color? accentSquare)
    {
        using StringFormat typographic = CreateTypographicFormat();
        using SolidBrush mutedBrush = new(MutedTextColor);
        using SolidBrush primaryBrush = new(PrimaryTextColor);

        float labelX = x;
        if (accentSquare is Color color)
        {
            const float sq = 5f;
            DrawMicroSquare(graphics, x, y + (_smallFont.Height - sq) * 0.5f, sq, color);
            labelX += sq + 6f;
        }
        graphics.DrawString(Localization.T(labelKey), _smallFont, mutedBrush, labelX, y, typographic);

        string formattedValue = value is > 0 and < 1 ? $"{value:F3}" : $"{value:F1}";
        float valueY = y + 22f;
        graphics.DrawString(formattedValue, _valueFont, primaryBrush, x, valueY, typographic);
        float valueWidth = graphics.MeasureString(formattedValue, _valueFont, 150, typographic).Width;
        graphics.DrawString("nits", _smallFont, mutedBrush, x + valueWidth + 4f, valueY + _unitBaselineOffset, typographic);
    }

    private void DrawInlineStat(
        Graphics graphics,
        float x,
        float y,
        string label,
        string value,
        Brush valueBrush,
        Brush labelBrush,
        StringFormat typographic)
    {
        graphics.DrawString(label, _smallFont, labelBrush, x, y, typographic);
        float labelWidth = graphics.MeasureString(label, _smallFont, 120, typographic).Width;
        graphics.DrawString(value, _smallSemiboldFont, valueBrush, x + labelWidth + 6f, y, typographic);
    }

    private int DrawPointerSection(Graphics graphics, int y)
    {
        DrawDivider(graphics, y);
        y += 16;

        using StringFormat typographic = CreateTypographicFormat();
        using SolidBrush mutedBrush = new(MutedTextColor);
        using SolidBrush primaryBrush = new(PrimaryTextColor);
        using SolidBrush accentBrush = new(AccentTextColor);

        if (!_showAblEstimate)
        {
            float labelY = y + (_bodyFont.Height - _smallSemiboldFont.Height) * 0.5f;
            DrawSectionHeader(graphics, OuterPadding, labelY, Localization.T("OverlayPointerTitle"), PointerAccentColor);

            if (_luminance is null || !_luminance.PointerInCaptureArea)
            {
                string outside = Localization.T("OverlayPointerOutsideShort");
                SizeF outsideSize = graphics.MeasureString(outside, _smallFont, 200, typographic);
                graphics.DrawString(outside, _smallFont, mutedBrush, PanelWidth - OuterPadding - outsideSize.Width, labelY, typographic);
            }
            else
            {
                string value = $"{_luminance.PointerRegionNits:F3}";
                SizeF valueSize = graphics.MeasureString(value, _bodyFont, 160, typographic);
                SizeF unitSize = graphics.MeasureString("nits", _smallFont, 60, typographic);
                float unitX = PanelWidth - OuterPadding - unitSize.Width;
                float valueX = unitX - 4f - valueSize.Width;
                float unitOffset = CalculateBaselineOffset(_bodyFont, _smallFont);
                graphics.DrawString(value, _bodyFont, primaryBrush, valueX, y, typographic);
                graphics.DrawString("nits", _smallFont, mutedBrush, unitX, y + unitOffset, typographic);
            }

            return y + 38;
        }

        float colWidth = ContentWidth / 3f;
        DrawSectionHeader(graphics, OuterPadding, y, Localization.T("OverlayPointerTitle"), PointerAccentColor);

        if (_luminance is null || !_luminance.PointerInCaptureArea)
        {
            graphics.DrawString(
                Localization.T("OverlayPointerOutsideShort"),
                _smallFont,
                mutedBrush,
                OuterPadding + colWidth,
                y,
                typographic);
            return y + 54;
        }

        float col2X = OuterPadding + colWidth;
        float col3X = OuterPadding + colWidth * 2f;
        graphics.DrawString(Localization.T("OverlayPointerSourceLabel"), _smallFont, mutedBrush, col2X, y, typographic);
        graphics.DrawString($"{_luminance.PointerRegionNits:F3} nits", _smallSemiboldFont, primaryBrush, col2X, y + 22f, typographic);

        if (_luminance.PointerScaledNits is double scaledNits)
        {
            graphics.DrawString(Localization.T("OverlayPointerAblLabel"), _smallFont, mutedBrush, col3X, y, typographic);
            graphics.DrawString($"{scaledNits:F3} nits", _smallSemiboldFont, accentBrush, col3X, y + 22f, typographic);
        }

        return y + 54;
    }

    private void DrawLuminanceFalseColorLegend(Graphics graphics, int y)
    {
        DrawDivider(graphics, y);
        y += 16;

        using SolidBrush mutedBrush = new(MutedTextColor);
        DrawSectionHeader(graphics, OuterPadding, y, Localization.T("OverlayFalseColor"), HdrAccentColor);

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

        float barY = y + 28f;
        const float barHeight = 8f;
        float segmentWidth = ContentWidth / (float)startColors.Length;
        RectangleF barBounds = new(OuterPadding, barY, ContentWidth, barHeight);
        using GraphicsPath clipPath = CreateRoundedRectangle(barBounds, LegendBarRadius);
        GraphicsState state = graphics.Save();
        graphics.SetClip(clipPath);
        for (int i = 0; i < startColors.Length; i++)
        {
            float segX = OuterPadding + i * segmentWidth;
            float segW = i == startColors.Length - 1 ? ContentWidth - i * segmentWidth : segmentWidth + 0.75f;
            RectangleF segBounds = new(segX, barY, segW, barHeight);
            if (startColors[i] == endColors[i])
            {
                using SolidBrush brush = new(startColors[i]);
                graphics.FillRectangle(brush, segBounds);
            }
            else
            {
                using LinearGradientBrush brush = new(segBounds, startColors[i], endColors[i], LinearGradientMode.Horizontal)
                {
                    GammaCorrection = true
                };
                graphics.FillRectangle(brush, segBounds);
            }
        }
        graphics.Restore(state);

        using Pen barBorder = new(BorderColor, HairlineWidth);
        graphics.DrawPath(barBorder, clipPath);

        using StringFormat centered = CreateTypographicFormat(StringAlignment.Center);
        for (int i = 0; i < labels.Length; i++)
        {
            RectangleF labelBounds = new(
                OuterPadding + i * segmentWidth,
                barY + barHeight + 8f,
                segmentWidth,
                20f);
            graphics.DrawString(labels[i], _smallFont, mutedBrush, labelBounds, centered);
        }
    }

    private void DrawGamutFalseColorLegend(Graphics graphics, int y)
    {
        DrawDivider(graphics, y);
        y += 16;

        using SolidBrush mutedBrush = new(MutedTextColor);
        DrawSectionHeader(graphics, OuterPadding, y, Localization.T("OverlayGamutFalseColor"), Color.FromArgb(224, 92, 255));

        string[] labels =
        [
            Localization.T("OverlayGamutSrgb"),
            "DCI-P3",
            "BT.2020",
            Localization.T("OverlayGamutOutside")
        ];
        Color[] solidColors =
        [
            Color.Empty,
            Color.FromArgb(255, 199, 56),
            Color.FromArgb(224, 92, 255),
            Color.FromArgb(255, 66, 61)
        ];

        float barY = y + 28f;
        const float barHeight = 8f;
        const float segmentGap = 5f;
        float segmentWidth = (ContentWidth - segmentGap * (labels.Length - 1)) / labels.Length;

        using Pen barBorder = new(BorderColor, HairlineWidth);
        using StringFormat centered = CreateTypographicFormat(StringAlignment.Center);
        for (int i = 0; i < labels.Length; i++)
        {
            float segX = OuterPadding + i * (segmentWidth + segmentGap);
            RectangleF segBounds = new(segX, barY, segmentWidth, barHeight);
            using GraphicsPath segPath = CreateRoundedRectangle(segBounds, LegendBarRadius);
            if (i == 0)
            {
                using LinearGradientBrush monoBrush = new(
                    segBounds,
                    Color.FromArgb(30, 33, 40),
                    Color.FromArgb(224, 228, 236),
                    LinearGradientMode.Horizontal);
                graphics.FillPath(monoBrush, segPath);
                graphics.DrawPath(barBorder, segPath);
            }
            else
            {
                using SolidBrush brush = new(solidColors[i]);
                graphics.FillPath(brush, segPath);
            }

            RectangleF labelBounds = new(segX, barY + barHeight + 8f, segmentWidth, 20f);
            graphics.DrawString(labels[i], _smallFont, mutedBrush, labelBounds, centered);
        }
    }

    private static void DrawMicroSquare(Graphics graphics, float x, float y, float size, Color color)
    {
        using GraphicsPath path = CreateRoundedRectangle(new RectangleF(x, y, size, size), 1.5f);
        using SolidBrush brush = new(color);
        graphics.FillPath(brush, path);
    }

    private void DrawDivider(Graphics graphics, float y)
    {
        using Pen divider = new(DividerColor, HairlineWidth);
        graphics.DrawLine(divider, OuterPadding, y, PanelWidth - OuterPadding, y);
    }

    private int CalculatePanelHeight()
    {
        int height = 86 + 16 + 88;
        if (_showAblEstimate)
            height += 16 + 120;
        if (_showPointerLuminance)
            height += 16 + (_showAblEstimate ? 54 : 38);
        if (_falseColorMode != FalseColorMode.None)
            height += 16 + 70;
        return height + 18;
    }

    private void ResizeForContent()
    {
        int targetHeight = ScaleToDevice(CalculatePanelHeight());
        if (Height != targetHeight)
        {
            Height = targetHeight;
            if (!_glassActive)
                UpdateRoundedRegion();
        }
        Invalidate();
    }

    private void ApplyDpi(int dpi)
    {
        _currentDpi = Math.Max(72, dpi);
        _dpiScale = Math.Max(0.35f, (_currentDpi / (float)LayoutBaselineDpi) * (float)_appearance.Scale);
        int panelWidth = ScaleToDevice(PanelWidth);
        int panelX = _outputBounds.Left + ScaleToDevice(24);
        Bounds = new Rectangle(
            Math.Max(_outputBounds.Left, panelX),
            _outputBounds.Top + ScaleToDevice(24),
            panelWidth,
            ScaleToDevice(CalculatePanelHeight()));
        if (!_glassActive)
            UpdateRoundedRegion();
        Invalidate();
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

    private static float CalculateBaselineOffset(Font largeFont, Font smallFont)
    {
        float largeAscent = largeFont.Size *
            largeFont.FontFamily.GetCellAscent(largeFont.Style) /
            largeFont.FontFamily.GetEmHeight(largeFont.Style);
        float smallAscent = smallFont.Size *
            smallFont.FontFamily.GetCellAscent(smallFont.Style) /
            smallFont.FontFamily.GetEmHeight(smallFont.Style);
        return largeAscent - smallAscent;
    }

    private static StringFormat CreateTypographicFormat(
        StringAlignment alignment = StringAlignment.Near,
        bool ellipsis = false)
    {
        StringFormat format = new(StringFormat.GenericTypographic)
        {
            Alignment = alignment,
            LineAlignment = StringAlignment.Near,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces
        };
        if (ellipsis)
            format.Trimming = StringTrimming.EllipsisCharacter;
        return format;
    }

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

    private static string FriendlyFormat(string format) => format switch
    {
        "R16G16B16A16_Float" => "FP16 scRGB",
        "R10G10B10A2_UNorm" => "RGB10 PQ",
        "B8G8R8A8_UNorm" => "BGRA8 SDR",
        _ => format
    };

    private string CreateRouteText() => _analysisOnly
        ? Localization.F("SingleDisplayRoute", _captureName)
        : $"{_captureName} → {_presentName}";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _smallFont.Dispose();
            _smallSemiboldFont.Dispose();
            _bodyFont.Dispose();
            _valueFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
