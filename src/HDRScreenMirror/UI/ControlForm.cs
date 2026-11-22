using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using HDRScreenMirror.DirectX;
using HDRScreenMirror.Interop;
using Microsoft.Win32;

namespace HDRScreenMirror.UI;

internal sealed class ControlForm : Form
{
    private const int DefaultPaperWhiteNits = 203;
    private const int RecallWindowHotKeyId = 0x4848;
    private const int ToggleMirrorHotKeyId = 0x484D;
    private const int ToggleStatusOverlayHotKeyId = 0x4851;
    private const int ToggleFalseColorHotKeyId = 0x4846;
    private const int ScreenshotHotKeyId = 0x4853;
    private const int RegionScreenshotHotKeyId = 0x4854;
    private const string AutomaticScreenshotMode = "automatic";
    private const string PromptScreenshotMode = "prompt";
    private const string ClipboardScreenshotMode = "clipboard";
    private const string FollowOutputFrameRateMode = "output";
    private const string FixedFrameRateMode = "fixed";
    private const string UnlimitedFrameRateMode = "unlimited";
    private const string MirrorOperationMode = "mirror";
    private const string AnalysisOperationMode = "analysis";
    private const float LayoutBaselineDpi = HextaTheme.BaselineDpi;

    // Header controls
    private readonly HextaSegmentedControl _modeSegmented = new();
    private readonly HextaSelect _themeSelect = new();
    private readonly HextaSelect _languageSelect = new();
    private readonly HextaButton _hotkeysButton = CreateButton("HotkeysButton", HextaButtonVariant.Ghost);
    private readonly HextaButton _aboutButton = CreateButton("About", HextaButtonVariant.Ghost);

    // Card 1: Display & Output
    private readonly HextaSelect _captureSelect = new();
    private readonly HextaButton _refreshButton = CreateButton("ScanShort", HextaButtonVariant.Secondary);
    private readonly HextaSelect _presentSelect = new();
    private readonly HextaSwitch _allOutputs = CreateSwitch("AllOutputs", false);
    private readonly HextaSegmentedControl _frameRateSegmented = new();
    private readonly HextaNumberInput _frameRateLimit = new() { Minimum = 24, Maximum = 500, Step = 10, Value = 60 };
    private readonly Label _frameRateUnitLabel = CreateMutedLabel("FrameRateUnit");
    private readonly HextaNumberInput _paperWhite = new() { Minimum = 80, Maximum = 500, Step = 5, Value = DefaultPaperWhiteNits };
    private readonly Label _paperWhiteUnitLabel = new() { Text = "nits", Font = HextaTheme.CreateSmallFont(), ForeColor = HextaTheme.TextMuted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly HextaButton _resetPaperWhiteButton = CreateButton("ResetShort", HextaButtonVariant.Secondary);
    private readonly HextaSwitch _renderCursor = CreateSwitch("RenderCursor", true);
    private readonly HextaSwitch _mouseThrough = CreateSwitch("MouseThrough", false);
    private readonly HextaSwitch _moveOutputWindows = CreateSwitch("MoveOutputWindowsShort", false);

    // Card 2: Capture & Controls
    private readonly HextaSegmentedControl _screenshotModeSegmented = new();
    private readonly HextaSwitch _copyToClipboardSwitch;
    private readonly HextaReadOnlyField _screenshotDirectoryField = new();
    private readonly HextaButton _chooseScreenshotDirectoryButton = CreateButton("ChooseFolder", HextaButtonVariant.Secondary);
    private readonly HextaButton _fullScreenshotButton = CreateButton("CaptureFullButton", HextaButtonVariant.Secondary);
    private readonly HextaButton _regionScreenshotButton = CreateButton("CaptureRegionButton", HextaButtonVariant.Secondary);
    private readonly HextaSwitch _enableHotKeys = CreateSwitch("EnableHotkeys", true);
    private readonly HextaSwitch _minimizeToTray;

    // Card 3: Analysis & False Color
    private readonly HextaSegmentedControl _falseColorSegmented = new();
    private readonly HextaSwitch _showPointerLuminance = CreateSwitch("ShowPointerLuminance", true);
    private readonly HextaSwitch _showLuminanceMarkers = CreateSwitch("LuminanceMarkers", false);
    private readonly HextaSwitch _showCieAnalysis = CreateSwitch("CieAnalysis", false);
    private readonly HextaSwitch _showAblEstimate;
    private readonly HextaSelect _ablProfileSelect = new();
    private readonly HextaButton _manageAblProfilesButton = CreateButton("ManageAblProfiles", HextaButtonVariant.Secondary);

    // Card 4: Overlay Appearance
    private static readonly int[] SupportedOverlayUpdateHz = [1, 2, 4, 10, 20];
    private readonly HextaButton _resetOverlayStyleButton = CreateButton("OverlayResetStyle", HextaButtonVariant.Ghost);
    private readonly HextaSwitch _showStatusOverlay = CreateSwitch("StatusOverlay", true);
    private readonly HextaSwitch _overlayGlassSwitch;
    private readonly HextaSegmentedControl _overlayUpdateHzSegmented = new();
    private readonly HextaSlider _overlayOpacitySlider = new() { Minimum = 40, Maximum = 100, Value = 82 };
    private readonly SingleLineRightValue _overlayOpacityValue = new() { Text = "82%" };
    private readonly HextaSlider _overlayScaleSlider = new() { Minimum = 50, Maximum = 150, Value = 100 };
    private readonly SingleLineRightValue _overlayScaleValue = new() { Text = "100%" };
    private readonly List<HextaColorSwatch> _bgSwatches = [];
    private readonly List<HextaColorSwatch> _accentSwatches = [];
    private readonly HextaColorSwatch _customBgSwatch = new() { IsCustomPicker = true };
    private readonly HextaColorSwatch _customAccentSwatch = new() { IsCustomPicker = true };
    private readonly OverlayMiniPreview _overlayPreview = new();

    // Footer controls
    private readonly FooterTelemetryBar _footerTelemetry = new();
    private readonly HextaButton _stopButton = CreateButton("Stop", HextaButtonVariant.Secondary);
    private readonly HextaButton _startButton = CreateButton("StartMirror", HextaButtonVariant.Primary);

    // Layout hosts
    private Panel _headerBar = null!;
    private HextaSeparator _headerDivider = null!;
    private Panel _scrollHost = null!;
    private Panel _scrollContent = null!;
    private HextaSeparator _footerDivider = null!;
    private Panel _footerBar = null!;
    private PictureBox _brandPicture = null!;
    private Label _brandLabel = null!;

    private HextaCard _displayCard = null!;
    private HextaCard _captureCard = null!;
    private HextaCard _analysisCard = null!;
    private HextaCard _overlayCard = null!;

    // Labels stored for deterministic layout & localization
    private readonly Label _displayHeading = CreateCardHeading("SectionDisplay");
    private readonly Label _captureDisplayLabel = CreateFieldLabel("CaptureDisplay");
    private readonly Label _outputDisplayLabel = CreateFieldLabel("OutputDisplay");
    private readonly Label _frameRateLabel = CreateFieldLabel("FrameRateMode");
    private readonly Label _paperWhiteLabel = CreateFieldLabel("PaperWhite");
    private readonly HextaSeparator _displayDivider = new() { Dock = DockStyle.None };

    private readonly Label _captureHeading = CreateCardHeading("SectionScreenshot");
    private readonly Label _screenshotModeLabel = CreateFieldLabel("ScreenshotSettings");
    private readonly HextaSeparator _captureDivider = new() { Dock = DockStyle.None };

    private readonly Label _analysisHeading = CreateCardHeading("SectionAnalysis");
    private readonly Label _falseColorLabel = CreateFieldLabel("FalseColorModeLabel");
    private readonly HextaSeparator _analysisDivider = new() { Dock = DockStyle.None };

    private readonly Label _overlayHeading = CreateCardHeading("SectionOverlayStyle");
    private readonly Label _refreshRateLabel = CreateFieldLabel("OverlayRefreshRateLabel");
    private readonly Label _opacityLabel = CreateFieldLabel("OverlayOpacityLabel");
    private readonly Label _scaleLabel = CreateFieldLabel("OverlayScaleLabel");
    private readonly Label _bgColorLabel = CreateFieldLabel("OverlayBackgroundLabel");
    private readonly Label _accentColorLabel = CreateFieldLabel("OverlayAccentLabel");

    private IReadOnlyList<DisplayTarget> _displays = [];
    private MirrorSession? _session;
    private readonly List<MirrorForm> _mirrorWindows = [];
    private readonly List<StatusOverlayForm> _statusOverlays = [];
    private readonly List<CieOverlayForm> _cieOverlays = [];
    private readonly List<AnalysisOverlayForm> _analysisOverlays = [];
    private readonly System.Windows.Forms.Timer _displayChangeTimer = new() { Interval = 750 };
    private readonly System.Windows.Forms.Timer _noticeTimer = new() { Interval = 4000 };
    private readonly AppSettings _settings;
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _trayIconImage;
    private readonly Image _brandLogo;
    private readonly ContextMenuStrip _trayMenu;
    private readonly ToolStripMenuItem _trayShowItem;
    private readonly ToolStripMenuItem _trayToggleMirrorItem;
    private readonly ToolStripMenuItem _trayExitItem;

    private bool _exitRequested;
    private bool _updatingThemeSelection;
    private bool _updatingLanguageSelection;
    private bool _updatingScreenshotSelection;
    private bool _updatingFrameRateSelection;
    private bool _updatingModeSelection;
    private bool _updatingAblProfileSelection;
    private bool _updatingDisplaySelection;
    private bool _updatingFalseColorSelection;
    private bool _updatingOverlayControls;
    private bool _takingScreenshot;
    private bool _singleDisplayAnalysis;
    private bool _controlWindowCaptureExcluded;
    private bool _restartMirrorAfterDisplayChange;
    private bool? _displayChangeAnalysisOnly;
    private string? _displayChangeCaptureId;
    private string? _displayChangePresentId;

    public ControlForm(AppSettings settings)
    {
        SuspendLayout();
        Text = "HDRScreenMirror";
        ClientSize = new Size(1200, 980);
        MinimumSize = new Size(940, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = HextaTheme.Canvas;
        Font = HextaTheme.CreateSmallFont();
        DoubleBuffered = true;

        _settings = settings;
        _settings.NormalizeAblProfiles();
        _minimizeToTray = CreateSwitch("MinimizeToTray", _settings.MinimizeToTrayOnClose);
        _showAblEstimate = CreateSwitch("ShowAblEstimate", _settings.AblEstimationEnabled);
        _copyToClipboardSwitch = CreateSwitch("CopyToClipboard", _settings.CopyScreenshotToClipboard);
        _overlayGlassSwitch = CreateSwitch("OverlayGlassLabel", _settings.OverlayGlassEnabled);

        _trayShowItem = new ToolStripMenuItem(Localization.T("TrayShow"));
        _trayToggleMirrorItem = new ToolStripMenuItem(Localization.T("TrayStart"));
        _trayExitItem = new ToolStripMenuItem(Localization.T("TrayExit"));
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add(_trayShowItem);
        _trayMenu.Items.Add(_trayToggleMirrorItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(_trayExitItem);
        _trayIconImage = BrandAssets.LoadApplicationIcon();
        _brandLogo = BrandAssets.LoadLogoImage();
        Icon = _trayIconImage;
        _trayIcon = new NotifyIcon
        {
            Icon = _trayIconImage,
            Text = "HDRScreenMirror",
            ContextMenuStrip = _trayMenu,
            Visible = false
        };

        _stopButton.Enabled = false;

        InitializeThemeSelector();
        InitializeLanguageSelector();
        InitializeScreenshotSelector();
        InitializeFrameRateSelector();
        InitializeOperationModeSelector();
        InitializeFalseColorSelector();
        InitializeAblProfiles();
        InitializeOverlayAppearanceControls();

        BuildControlHierarchy();

        AutoScaleDimensions = new SizeF(LayoutBaselineDpi, LayoutBaselineDpi);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
        ApplyLanguage();
        PerformFullLayout();

        WireEvents();
    }

    private void BuildControlHierarchy()
    {
        _headerBar = new Panel { BackColor = HextaTheme.Surface };
        _brandPicture = new PictureBox
        {
            Image = _brandLogo,
            SizeMode = PictureBoxSizeMode.Zoom,
            TabStop = false
        };
        _brandLabel = new Label
        {
            Text = "HDRScreenMirror",
            AutoSize = false,
            Font = HextaTheme.CreateTitleFont(FontStyle.Bold),
            ForeColor = HextaTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _headerBar.Controls.AddRange([_brandPicture, _brandLabel, _modeSegmented, _themeSelect, _languageSelect, _hotkeysButton, _aboutButton]);

        _headerDivider = new HextaSeparator { Dock = DockStyle.None };

        _scrollHost = new Panel { AutoScroll = true, BackColor = HextaTheme.Canvas };
        _scrollContent = new Panel { BackColor = HextaTheme.Canvas };
        _scrollHost.Controls.Add(_scrollContent);

        _displayCard = new HextaCard();
        _displayCard.Controls.AddRange(
        [
            _displayHeading,
            _captureDisplayLabel, _captureSelect, _refreshButton,
            _outputDisplayLabel, _presentSelect,
            _allOutputs,
            _frameRateLabel, _frameRateSegmented, _frameRateLimit, _frameRateUnitLabel,
            _paperWhiteLabel, _paperWhite, _paperWhiteUnitLabel, _resetPaperWhiteButton,
            _displayDivider,
            _renderCursor, _mouseThrough, _moveOutputWindows
        ]);

        _captureCard = new HextaCard();
        _captureCard.Controls.AddRange(
        [
            _captureHeading,
            _screenshotModeLabel, _screenshotModeSegmented,
            _copyToClipboardSwitch,
            _screenshotDirectoryField, _chooseScreenshotDirectoryButton,
            _fullScreenshotButton, _regionScreenshotButton,
            _captureDivider,
            _enableHotKeys, _minimizeToTray
        ]);

        _analysisCard = new HextaCard();
        _analysisCard.Controls.AddRange(
        [
            _analysisHeading,
            _falseColorLabel, _falseColorSegmented,
            _showPointerLuminance, _showLuminanceMarkers, _showCieAnalysis,
            _analysisDivider,
            _showAblEstimate, _ablProfileSelect, _manageAblProfilesButton
        ]);

        _overlayCard = new HextaCard();
        _overlayCard.Controls.AddRange(
        [
            _overlayHeading, _resetOverlayStyleButton,
            _showStatusOverlay, _overlayGlassSwitch,
            _refreshRateLabel, _overlayUpdateHzSegmented,
            _opacityLabel, _overlayOpacitySlider, _overlayOpacityValue,
            _scaleLabel, _overlayScaleSlider, _overlayScaleValue,
            _bgColorLabel, _customBgSwatch,
            _accentColorLabel, _customAccentSwatch,
            _overlayPreview
        ]);
        foreach (HextaColorSwatch swatch in _bgSwatches)
            _overlayCard.Controls.Add(swatch);
        foreach (HextaColorSwatch swatch in _accentSwatches)
            _overlayCard.Controls.Add(swatch);

        _scrollContent.Controls.AddRange([_displayCard, _captureCard, _analysisCard, _overlayCard]);

        _footerDivider = new HextaSeparator { Dock = DockStyle.None };
        _footerBar = new Panel { BackColor = HextaTheme.Surface };
        _footerBar.Controls.AddRange([_footerTelemetry, _stopButton, _startButton]);

        Controls.AddRange([_headerBar, _headerDivider, _scrollHost, _footerDivider, _footerBar]);
    }

    private void WireEvents()
    {
        Resize += (_, _) => PerformFullLayout();
        DpiChanged += (_, _) =>
        {
            PerformFullLayout();
            _paperWhite.PerformLayout();
            _frameRateLimit.PerformLayout();
        };
        _scrollHost.ClientSizeChanged += (_, _) => LayoutCardsOnly();

        _refreshButton.Click += (_, _) => RefreshDisplays();
        _captureSelect.SelectedIndexChanged += (_, _) => SaveDisplaySelection(capture: true);
        _presentSelect.SelectedIndexChanged += (_, _) => SaveDisplaySelection(capture: false);
        _resetPaperWhiteButton.Click += (_, _) => _paperWhite.Value = DefaultPaperWhiteNits;
        _startButton.Click += (_, _) => StartMirror();
        _stopButton.Click += (_, _) => StopMirror();
        _hotkeysButton.Click += (_, _) => ShowHotkeysDialog();
        _aboutButton.Click += (_, _) => ShowAbout();
        _allOutputs.CheckedChanged += (_, _) => UpdatePresentSelectorState();
        _showStatusOverlay.CheckedChanged += (_, _) => UpdateStatusOverlayVisibility();
        _showPointerLuminance.CheckedChanged += (_, _) => UpdatePointerLuminanceVisibility();
        _falseColorSegmented.SelectedIndexChanged += (_, _) => UpdateFalseColorMode();
        _showLuminanceMarkers.CheckedChanged += (_, _) => UpdateLuminanceMarkerVisibility();
        _showAblEstimate.CheckedChanged += (_, _) => ChangeAblEstimation();
        _ablProfileSelect.SelectedIndexChanged += (_, _) => ChangeAblProfile();
        _manageAblProfilesButton.Click += (_, _) => ManageAblProfiles();
        _showCieAnalysis.CheckedChanged += (_, _) => UpdateCieAnalysisVisibility();
        _enableHotKeys.CheckedChanged += (_, _) => RegisterConfiguredHotKeys();
        _minimizeToTray.CheckedChanged += (_, _) => SaveCloseBehavior();
        _screenshotModeSegmented.SelectedIndexChanged += (_, _) => ChangeScreenshotMode();
        _copyToClipboardSwitch.CheckedChanged += (_, _) => ChangeCopyToClipboard();
        _fullScreenshotButton.Click += (_, _) => TakeOutputScreenshots();
        _regionScreenshotButton.Click += (_, _) => TakeRegionScreenshot();
        _frameRateSegmented.SelectedIndexChanged += (_, _) => ChangeFrameRateMode();
        _modeSegmented.SelectedIndexChanged += (_, _) => ChangeOperationMode();
        _frameRateLimit.ValueChanged += (_, _) => ChangeFrameRateLimit();
        _chooseScreenshotDirectoryButton.Click += (_, _) => ChooseScreenshotDirectory();
        _themeSelect.SelectedIndexChanged += (_, _) => ChangeTheme();
        _languageSelect.SelectedIndexChanged += (_, _) => ChangeLanguage();
        SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged;

        _overlayGlassSwitch.CheckedChanged += (_, _) => ChangeOverlayGlass();
        _overlayUpdateHzSegmented.SelectedIndexChanged += (_, _) => ChangeOverlayUpdateHz();
        _overlayOpacitySlider.ValueChanged += (_, _) => ChangeOverlayOpacity(save: false);
        _overlayOpacitySlider.ValueCommitted += (_, _) => ChangeOverlayOpacity(save: true);
        _overlayScaleSlider.ValueChanged += (_, _) => ChangeOverlayScale(save: false);
        _overlayScaleSlider.ValueCommitted += (_, _) => ChangeOverlayScale(save: true);
        _resetOverlayStyleButton.Click += (_, _) => ResetOverlayAppearance();
        _customBgSwatch.Click += (_, _) => PickCustomOverlayColor(isBackground: true);
        _customAccentSwatch.Click += (_, _) => PickCustomOverlayColor(isBackground: false);

        _displayChangeTimer.Tick += (_, _) => ApplyDisplayChange();
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            _footerTelemetry.ClearNotice();
        };
        _trayShowItem.Click += (_, _) => RecallControlWindow();
        _trayToggleMirrorItem.Click += (_, _) => ToggleMirror();
        _trayExitItem.Click += (_, _) => ExitApplication();
        _trayIcon.DoubleClick += (_, _) => RecallControlWindow();
        FormClosing += OnFormClosing;
        Shown += (_, _) =>
        {
            PerformFullLayout();
            FitInitialWindowHeight();
            RefreshDisplays();
        };
    }

    private int S(float px) => Math.Max(1, (int)Math.Round(px * HextaTheme.ScaleFactor(this)));

    private void FitInitialWindowHeight()
    {
        if (_scrollContent is null)
            return;

        int desiredClientHeight = S(60) + S(64) + 2 + _scrollContent.Height;
        Rectangle workArea = Screen.FromControl(this).WorkingArea;
        int chromeHeight = Height - ClientSize.Height;
        int maxClientHeight = Math.Max(S(600), workArea.Height - chromeHeight - S(32));
        int targetClientHeight = Math.Min(desiredClientHeight, maxClientHeight);

        if (ClientSize.Height < targetClientHeight)
        {
            ClientSize = new Size(ClientSize.Width, targetClientHeight);
            Location = new Point(
                workArea.Left + Math.Max(16, (workArea.Width - Width) / 2),
                workArea.Top + Math.Max(16, (workArea.Height - Height) / 2));
            PerformFullLayout();
        }
    }

    private void PerformFullLayout()
    {
        if (_headerBar is null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        int headerH = S(60);
        int footerH = S(64);
        int midH = Math.Max(100, h - headerH - footerH - 2);

        _headerBar.SetBounds(0, 0, w, headerH);
        _headerDivider.SetBounds(0, headerH, w, 1);
        _scrollHost.SetBounds(0, headerH + 1, w, midH);
        _footerDivider.SetBounds(0, headerH + 1 + midH, w, 1);
        _footerBar.SetBounds(0, headerH + 2 + midH, w, footerH);

        LayoutHeaderBar(w, headerH);
        LayoutCardsOnly();
        LayoutFooterBar(w, footerH);
    }

    private void LayoutHeaderBar(int w, int h)
    {
        int padX = S(20);
        int ctrlH = S(34);
        int cy = (h - ctrlH) / 2;

        int logoSize = S(26);
        _brandPicture.SetBounds(padX, (h - logoSize) / 2, logoSize, logoSize);
        _brandLabel.SetBounds(padX + logoSize + S(10), 0, S(240), h);

        int aboutW = S(60);
        int hotkeysW = S(72);
        int langW = S(124);
        int themeW = S(124);
        int modeW = S(204);
        int gap = S(8);

        int x = w - padX;
        x -= aboutW;
        _aboutButton.SetBounds(x, cy, aboutW, ctrlH);
        x -= gap + hotkeysW;
        _hotkeysButton.SetBounds(x, cy, hotkeysW, ctrlH);
        x -= gap + langW;
        _languageSelect.SetBounds(x, cy, langW, ctrlH);
        x -= gap + themeW;
        _themeSelect.SetBounds(x, cy, themeW, ctrlH);
        x -= gap + modeW;
        _modeSegmented.SetBounds(x, cy, modeW, ctrlH);
    }

    private void LayoutCardsOnly()
    {
        if (_scrollHost is null || _scrollContent is null)
            return;

        int availableW = Math.Max(S(680), _scrollHost.ClientSize.Width);
        int padX = S(22);
        int padY = S(20);
        int colGap = S(16);
        int cardGap = S(16);
        int innerW = availableW - padX * 2;
        int leftW = (int)Math.Round((innerW - colGap) * 0.52);
        int rightW = innerW - colGap - leftW;
        int rightX = padX + leftW + colGap;

        int displayH = LayoutDisplayCard(leftW);
        _displayCard.SetBounds(padX, padY, leftW, displayH);

        int captureH = LayoutCaptureCard(leftW);
        _captureCard.SetBounds(padX, padY + displayH + cardGap, leftW, captureH);

        int analysisH = LayoutAnalysisCard(rightW);
        _analysisCard.SetBounds(rightX, padY, rightW, analysisH);

        int overlayH = LayoutOverlayCard(rightW);
        _overlayCard.SetBounds(rightX, padY + analysisH + cardGap, rightW, overlayH);

        int totalH = Math.Max(padY * 2 + displayH + cardGap + captureH, padY * 2 + analysisH + cardGap + overlayH);
        if (_scrollContent.Width != availableW || _scrollContent.Height != totalH)
            _scrollContent.SetBounds(0, 0, availableW, totalH);
    }

    private int LayoutDisplayCard(int cardW)
    {
        int padX = S(22);
        int y = S(20);
        int cw = cardW - padX * 2;
        int labelW = S(132);
        int rowH = S(36);
        int rowGap = S(12);
        int switchH = S(34);
        int switchGap = S(6);
        int editorX = padX + labelW;
        int editorW = Math.Max(S(140), cw - labelW);

        _displayHeading.SetBounds(padX, y, cw, S(24));
        y += S(36);

        int scanW = S(72);
        int btnGap = S(8);
        _captureDisplayLabel.SetBounds(padX, y, labelW, rowH);
        _captureSelect.SetBounds(editorX, y, Math.Max(S(80), editorW - scanW - btnGap), rowH);
        _refreshButton.SetBounds(editorX + editorW - scanW, y, scanW, rowH);
        y += rowH + rowGap;

        _outputDisplayLabel.SetBounds(padX, y, labelW, rowH);
        _presentSelect.SetBounds(editorX, y, editorW, rowH);
        y += rowH + S(10);

        _allOutputs.SetBounds(padX, y, cw, switchH);
        y += switchH + rowGap;

        _frameRateLabel.SetBounds(padX, y, labelW, rowH);
        bool showLimit = _frameRateLimit.Visible;
        if (showLimit)
        {
            int unitW = S(34);
            int numW = S(98);
            int segW = Math.Max(S(140), editorW - numW - unitW - S(12));
            _frameRateSegmented.SetBounds(editorX, y, segW, rowH);
            _frameRateLimit.SetBounds(editorX + segW + S(8), y, numW, rowH);
            _frameRateUnitLabel.SetBounds(editorX + segW + S(8) + numW + S(4), y, unitW, rowH);
        }
        else
        {
            _frameRateSegmented.SetBounds(editorX, y, editorW, rowH);
        }
        y += rowH + rowGap;

        int pwW = S(112);
        int pwUnitW = S(44);
        int resetW = S(68);
        _paperWhiteLabel.SetBounds(padX, y, labelW, rowH);
        _paperWhite.SetBounds(editorX, y, pwW, rowH);
        _paperWhiteUnitLabel.SetBounds(editorX + pwW + S(8), y, pwUnitW, rowH);
        _resetPaperWhiteButton.SetBounds(editorX + pwW + pwUnitW + S(10), y, resetW, rowH);
        y += rowH + S(14);

        _displayDivider.SetBounds(padX, y, cw, 1);
        y += S(12);

        _renderCursor.SetBounds(padX, y, cw, switchH);
        y += switchH + switchGap;
        _mouseThrough.SetBounds(padX, y, cw, switchH);
        y += switchH + switchGap;
        _moveOutputWindows.SetBounds(padX, y, cw, switchH);
        y += switchH + S(20);

        return y;
    }

    private int LayoutCaptureCard(int cardW)
    {
        int padX = S(22);
        int y = S(20);
        int cw = cardW - padX * 2;
        int labelW = S(118);
        int rowH = S(36);
        int rowGap = S(12);
        int switchH = S(34);
        int switchGap = S(6);
        int editorX = padX + labelW;
        int editorW = Math.Max(S(140), cw - labelW);

        _captureHeading.SetBounds(padX, y, cw, S(24));
        y += S(36);

        _screenshotModeLabel.SetBounds(padX, y, labelW, rowH);
        _screenshotModeSegmented.SetBounds(editorX, y, editorW, rowH);
        y += rowH + S(10);

        _copyToClipboardSwitch.SetBounds(padX, y, cw, switchH);
        y += switchH + S(10);

        int chooseW = S(92);
        int btnGap = S(8);
        _screenshotDirectoryField.SetBounds(padX, y, Math.Max(S(100), cw - chooseW - btnGap), rowH);
        _chooseScreenshotDirectoryButton.SetBounds(padX + cw - chooseW, y, chooseW, rowH);
        y += rowH + rowGap;

        int actionW = S(116);
        _fullScreenshotButton.SetBounds(padX, y, actionW, rowH);
        _regionScreenshotButton.SetBounds(padX + actionW + btnGap, y, actionW, rowH);
        y += rowH + S(14);

        _captureDivider.SetBounds(padX, y, cw, 1);
        y += S(12);

        _enableHotKeys.SetBounds(padX, y, cw, switchH);
        y += switchH + switchGap;
        _minimizeToTray.SetBounds(padX, y, cw, switchH);
        y += switchH + S(20);

        return y;
    }

    private int LayoutAnalysisCard(int cardW)
    {
        int padX = S(22);
        int y = S(20);
        int cw = cardW - padX * 2;
        int labelW = S(104);
        int rowH = S(36);
        int switchH = S(34);
        int switchGap = S(6);
        int editorX = padX + labelW;
        int editorW = Math.Max(S(140), cw - labelW);

        _analysisHeading.SetBounds(padX, y, cw, S(24));
        y += S(36);

        _falseColorLabel.SetBounds(padX, y, labelW, rowH);
        _falseColorSegmented.SetBounds(editorX, y, editorW, rowH);
        y += rowH + S(12);

        _showPointerLuminance.SetBounds(padX, y, cw, switchH);
        y += switchH + switchGap;
        _showLuminanceMarkers.SetBounds(padX, y, cw, switchH);
        y += switchH + switchGap;
        _showCieAnalysis.SetBounds(padX, y, cw, switchH);
        y += switchH + S(12);

        _analysisDivider.SetBounds(padX, y, cw, 1);
        y += S(12);

        _showAblEstimate.SetBounds(padX, y, cw, switchH);
        y += switchH + S(10);

        int manageW = S(112);
        int btnGap = S(8);
        _ablProfileSelect.SetBounds(padX, y, Math.Max(S(100), cw - manageW - btnGap), rowH);
        _manageAblProfilesButton.SetBounds(padX + cw - manageW, y, manageW, rowH);
        y += rowH + S(20);

        return y;
    }

    private int LayoutOverlayCard(int cardW)
    {
        int padX = S(22);
        int y = S(20);
        int cw = cardW - padX * 2;
        int labelW = S(104);
        int valueW = S(60);
        int switchH = S(34);
        int sliderRowH = S(32);
        int swatchRowH = S(36);
        int swatchSize = S(30);
        int swatchGap = S(8);

        int resetW = S(92);
        _overlayHeading.SetBounds(padX, y + S(2), cw - resetW - S(8), S(24));
        _resetOverlayStyleButton.SetBounds(padX + cw - resetW, y - S(2), resetW, S(30));
        y += S(36);

        _showStatusOverlay.SetBounds(padX, y, cw, switchH);
        y += switchH + S(6);

        _overlayGlassSwitch.SetBounds(padX, y, cw, switchH);
        y += switchH + S(10);

        int freqRowH = S(34);
        int sliderX = padX + labelW;
        _refreshRateLabel.SetBounds(padX, y, labelW, freqRowH);
        _overlayUpdateHzSegmented.SetBounds(sliderX, y, Math.Max(S(140), cw - labelW), freqRowH);
        y += freqRowH + S(10);

        int sliderW = Math.Max(S(80), cw - labelW - valueW - S(8));
        _opacityLabel.SetBounds(padX, y, labelW, sliderRowH);
        _overlayOpacitySlider.SetBounds(sliderX, y + S(3), sliderW, S(26));
        _overlayOpacityValue.SetBounds(padX + cw - valueW, y, valueW, sliderRowH);
        y += sliderRowH + S(8);

        _scaleLabel.SetBounds(padX, y, labelW, sliderRowH);
        _overlayScaleSlider.SetBounds(sliderX, y + S(3), sliderW, S(26));
        _overlayScaleValue.SetBounds(padX + cw - valueW, y, valueW, sliderRowH);
        y += sliderRowH + S(10);

        _bgColorLabel.SetBounds(padX, y, labelW, swatchRowH);
        int sx = sliderX;
        int sy = y + (swatchRowH - swatchSize) / 2;
        foreach (HextaColorSwatch swatch in _bgSwatches)
        {
            swatch.SetBounds(sx, sy, swatchSize, swatchSize);
            sx += swatchSize + swatchGap;
        }
        _customBgSwatch.SetBounds(sx, sy, swatchSize, swatchSize);
        y += swatchRowH + S(8);

        _accentColorLabel.SetBounds(padX, y, labelW, swatchRowH);
        sx = sliderX;
        sy = y + (swatchRowH - swatchSize) / 2;
        foreach (HextaColorSwatch swatch in _accentSwatches)
        {
            swatch.SetBounds(sx, sy, swatchSize, swatchSize);
            sx += swatchSize + swatchGap;
        }
        _customAccentSwatch.SetBounds(sx, sy, swatchSize, swatchSize);
        y += swatchRowH + S(14);

        int previewH = S(58);
        _overlayPreview.SetBounds(padX, y, cw, previewH);
        y += previewH + S(20);

        return y;
    }

    private void LayoutFooterBar(int w, int h)
    {
        int padX = S(20);
        int btnH = S(38);
        int by = (h - btnH) / 2;
        int startW = S(148);
        int stopW = S(82);
        int gap = S(8);

        int startX = w - padX - startW;
        int stopX = startX - gap - stopW;
        _startButton.SetBounds(startX, by, startW, btnH);
        _stopButton.SetBounds(stopX, by, stopW, btnH);
        _footerTelemetry.SetBounds(padX, 0, Math.Max(S(100), stopX - padX - S(16)), h);
    }

    private static Label CreateCardHeading(string key) => new()
    {
        Text = Localization.T(key),
        Tag = key,
        AutoSize = false,
        Font = HextaTheme.CreateBodyFont(FontStyle.Bold),
        ForeColor = HextaTheme.TextPrimary,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label CreateFieldLabel(string key) => new()
    {
        Text = Localization.T(key),
        Tag = key,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = HextaTheme.CreateSmallFont(),
        ForeColor = HextaTheme.TextSecondary
    };

    private static Label CreateMutedLabel(string key) => new()
    {
        Text = Localization.T(key),
        Tag = key,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = HextaTheme.CreateSmallFont(),
        ForeColor = HextaTheme.TextMuted
    };

    private static HextaSwitch CreateSwitch(string key, bool isChecked) => new()
    {
        Text = Localization.T(key),
        Tag = key,
        Checked = isChecked
    };

    private static HextaButton CreateButton(string key, HextaButtonVariant variant) => new()
    {
        Text = Localization.T(key),
        Tag = key,
        Variant = variant
    };

    private void ShowFooterNotice(string text, bool isError = false, bool autoClear = true)
    {
        _noticeTimer.Stop();
        _footerTelemetry.SetNotice(text, isError);
        if (autoClear)
            _noticeTimer.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.ApplyImmersiveDarkMode(Handle, HextaTheme.IsDark);
    }

    private void InitializeThemeSelector()
    {
        RefreshThemeChoices();
    }

    private void RefreshThemeChoices()
    {
        string pref = HextaTheme.NormalizePreference(_settings.UiTheme);
        ThemeChoice[] items =
        [
            new ThemeChoice(HextaTheme.AutoPreference, Localization.T("ThemeAuto")),
            new ThemeChoice(HextaTheme.LightPreference, Localization.T("ThemeLight")),
            new ThemeChoice(HextaTheme.DarkPreference, Localization.T("ThemeDark"))
        ];
        int idx = Array.FindIndex(items, x => x.Code == pref);
        _updatingThemeSelection = true;
        _themeSelect.SetItems(items, Math.Max(0, idx));
        _updatingThemeSelection = false;
    }

    private void ChangeTheme()
    {
        if (_updatingThemeSelection || _themeSelect.SelectedItem is not ThemeChoice choice)
            return;

        _settings.UiTheme = choice.Code;
        _settings.Save();
        HextaTheme.SetPreference(choice.Code);
        ApplyTheme();
    }

    private void OnSystemUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
            return;

        PostToUi(() =>
        {
            if (HextaTheme.RefreshSystemTheme())
                ApplyTheme();
        });
    }

    private void ApplyTheme()
    {
        if (IsHandleCreated)
            NativeMethods.ApplyImmersiveDarkMode(Handle, HextaTheme.IsDark);

        BackColor = HextaTheme.Canvas;
        _headerBar.BackColor = HextaTheme.Surface;
        _scrollHost.BackColor = HextaTheme.Canvas;
        _scrollContent.BackColor = HextaTheme.Canvas;
        _footerBar.BackColor = HextaTheme.Surface;

        _displayCard.BackColor = HextaTheme.Surface;
        _captureCard.BackColor = HextaTheme.Surface;
        _analysisCard.BackColor = HextaTheme.Surface;
        _overlayCard.BackColor = HextaTheme.Surface;

        _brandLabel.ForeColor = HextaTheme.TextPrimary;
        _displayHeading.ForeColor = HextaTheme.TextPrimary;
        _captureHeading.ForeColor = HextaTheme.TextPrimary;
        _analysisHeading.ForeColor = HextaTheme.TextPrimary;
        _overlayHeading.ForeColor = HextaTheme.TextPrimary;

        foreach (Label fieldLabel in new[]
        {
            _captureDisplayLabel, _outputDisplayLabel, _frameRateLabel, _paperWhiteLabel,
            _screenshotModeLabel, _falseColorLabel, _refreshRateLabel, _opacityLabel, _scaleLabel,
            _bgColorLabel, _accentColorLabel
        })
        {
            fieldLabel.ForeColor = HextaTheme.TextSecondary;
        }

        _frameRateUnitLabel.ForeColor = HextaTheme.TextMuted;
        _paperWhiteUnitLabel.ForeColor = HextaTheme.TextMuted;

        _paperWhite.RefreshTheme();
        _frameRateLimit.RefreshTheme();
        InvalidateRecursive(this);
    }

    private static void InvalidateRecursive(Control parent)
    {
        parent.Invalidate();
        foreach (Control child in parent.Controls)
            InvalidateRecursive(child);
    }

    private void InitializeLanguageSelector()
    {
        LanguageChoice[] items =
        [
            new LanguageChoice(Localization.Automatic, "Auto"),
            new LanguageChoice(Localization.SimplifiedChinese, "简体中文"),
            new LanguageChoice(Localization.English, "English")
        ];
        string pref = NormalizeLanguagePreference(_settings.Language);
        int idx = Array.FindIndex(items, x => x.Code == pref);
        _updatingLanguageSelection = true;
        _languageSelect.SetItems(items, Math.Max(0, idx));
        _updatingLanguageSelection = false;
    }

    private void InitializeScreenshotSelector()
    {
        _settings.ScreenshotSaveMode = NormalizeScreenshotMode(_settings.ScreenshotSaveMode);
        RefreshScreenshotModeChoices();
        UpdateScreenshotSettingsState();
    }

    private void InitializeFrameRateSelector()
    {
        _settings.FrameRateMode = NormalizeFrameRateMode(_settings.FrameRateMode);
        _settings.FrameRateLimit = Math.Clamp(_settings.FrameRateLimit, 24, 500);
        _frameRateLimit.Value = _settings.FrameRateLimit;
        RefreshFrameRateModeChoices();
        UpdateFrameRateSettingsState();
    }

    private void InitializeOperationModeSelector()
    {
        _settings.OperationMode = NormalizeOperationMode(_settings.OperationMode);
        RefreshOperationModeChoices();
    }

    private void InitializeFalseColorSelector() => RefreshFalseColorChoices();

    private void InitializeOverlayAppearanceControls()
    {
        _bgSwatches.Clear();
        foreach ((_, string hex) in OverlayAppearance.BackgroundPresets)
        {
            HextaColorSwatch swatch = new() { SwatchColor = OverlayAppearance.ParseHex(hex, Color.Black) };
            string capturedHex = hex;
            swatch.Click += (_, _) => SetOverlayBackgroundColor(capturedHex);
            _bgSwatches.Add(swatch);
        }

        _accentSwatches.Clear();
        foreach ((_, string hex) in OverlayAppearance.AccentPresets)
        {
            HextaColorSwatch swatch = new() { SwatchColor = OverlayAppearance.ParseHex(hex, Color.White) };
            string capturedHex = hex;
            swatch.Click += (_, _) => SetOverlayAccentColor(capturedHex);
            _accentSwatches.Add(swatch);
        }

        SyncOverlayAppearanceControls();
    }

    private void SyncOverlayAppearanceControls()
    {
        OverlayAppearance appearance = _settings.GetOverlayAppearance();
        _updatingOverlayControls = true;
        try
        {
            _overlayGlassSwitch.Checked = appearance.GlassEnabled;

            int hzIndex = FindClosestOverlayUpdateHzIndex(_settings.OverlayUpdateHz);
            _overlayUpdateHzSegmented.SetItems(
                SupportedOverlayUpdateHz.Select(hz => $"{hz}Hz"),
                hzIndex);

            int opacityPct = (int)Math.Round(appearance.Opacity * 100.0);
            _overlayOpacitySlider.Value = Math.Clamp(opacityPct, 40, 100);
            _overlayOpacityValue.Text = $"{opacityPct}%";

            int scalePct = (int)Math.Round(appearance.Scale * 100.0);
            _overlayScaleSlider.Value = Math.Clamp(scalePct, 50, 150);
            _overlayScaleValue.Text = $"{scalePct}%";

            string bgHex = OverlayAppearance.ToHex(appearance.BackgroundColor);
            bool bgMatched = false;
            for (int i = 0; i < _bgSwatches.Count; i++)
            {
                bool selected = string.Equals(OverlayAppearance.BackgroundPresets[i].Hex, bgHex, StringComparison.OrdinalIgnoreCase);
                _bgSwatches[i].Selected = selected;
                bgMatched |= selected;
            }
            _customBgSwatch.SwatchColor = appearance.BackgroundColor;
            _customBgSwatch.Selected = !bgMatched;

            string accentHex = OverlayAppearance.ToHex(appearance.AccentColor);
            bool accentMatched = false;
            for (int i = 0; i < _accentSwatches.Count; i++)
            {
                bool selected = string.Equals(OverlayAppearance.AccentPresets[i].Hex, accentHex, StringComparison.OrdinalIgnoreCase);
                _accentSwatches[i].Selected = selected;
                accentMatched |= selected;
            }
            _customAccentSwatch.SwatchColor = appearance.AccentColor;
            _customAccentSwatch.Selected = !accentMatched;

            _overlayPreview.SetAppearance(appearance);
        }
        finally
        {
            _updatingOverlayControls = false;
        }
    }

    private static int FindClosestOverlayUpdateHzIndex(int hz)
    {
        int bestIndex = 2; // 4Hz default
        int bestDistance = int.MaxValue;
        for (int i = 0; i < SupportedOverlayUpdateHz.Length; i++)
        {
            int distance = Math.Abs(SupportedOverlayUpdateHz[i] - hz);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

    private int GetSelectedOverlayUpdateHz()
    {
        int index = Math.Clamp(_overlayUpdateHzSegmented.SelectedIndex, 0, SupportedOverlayUpdateHz.Length - 1);
        return SupportedOverlayUpdateHz[index];
    }

    private void ChangeOverlayUpdateHz()
    {
        if (_updatingOverlayControls)
            return;

        int hz = GetSelectedOverlayUpdateHz();
        _settings.OverlayUpdateHz = hz;
        _settings.Save();
        _session?.SetAnalysisUpdateHz(hz);
    }

    private void ChangeOverlayGlass()
    {
        if (_updatingOverlayControls)
            return;

        _settings.OverlayGlassEnabled = _overlayGlassSwitch.Checked;
        _settings.Save();
        ApplyOverlayAppearanceToLiveOverlays();
    }

    private void ChangeOverlayOpacity(bool save)
    {
        if (_updatingOverlayControls)
            return;

        int percent = (int)Math.Round(_overlayOpacitySlider.Value);
        _overlayOpacityValue.Text = $"{percent}%";
        _settings.OverlayOpacity = Math.Clamp(percent / 100.0, OverlayAppearance.MinOpacity, OverlayAppearance.MaxOpacity);
        ApplyOverlayAppearanceToLiveOverlays();
        if (save)
            _settings.Save();
    }

    private void ChangeOverlayScale(bool save)
    {
        if (_updatingOverlayControls)
            return;

        int percent = (int)Math.Round(_overlayScaleSlider.Value);
        _overlayScaleValue.Text = $"{percent}%";
        _settings.OverlayScale = Math.Clamp(percent / 100.0, OverlayAppearance.MinScale, OverlayAppearance.MaxScale);
        ApplyOverlayAppearanceToLiveOverlays();
        if (save)
            _settings.Save();
    }

    private void SetOverlayBackgroundColor(string hex)
    {
        _settings.OverlayBackgroundColor = hex;
        _settings.Save();
        SyncOverlayAppearanceControls();
        ApplyOverlayAppearanceToLiveOverlays();
    }

    private void SetOverlayAccentColor(string hex)
    {
        _settings.OverlayAccentColor = hex;
        _settings.Save();
        SyncOverlayAppearanceControls();
        ApplyOverlayAppearanceToLiveOverlays();
    }

    private void PickCustomOverlayColor(bool isBackground)
    {
        OverlayAppearance current = _settings.GetOverlayAppearance();
        using ColorDialog dialog = new()
        {
            FullOpen = true,
            Color = isBackground ? current.BackgroundColor : current.AccentColor
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string hex = OverlayAppearance.ToHex(dialog.Color);
        if (isBackground)
            SetOverlayBackgroundColor(hex);
        else
            SetOverlayAccentColor(hex);
    }

    private void ResetOverlayAppearance()
    {
        _settings.OverlayOpacity = OverlayAppearance.DefaultOpacity;
        _settings.OverlayScale = OverlayAppearance.DefaultScale;
        _settings.OverlayGlassEnabled = OverlayAppearance.DefaultGlassEnabled;
        _settings.OverlayUpdateHz = 4;
        _settings.OverlayBackgroundColor = OverlayAppearance.DefaultBackgroundHex;
        _settings.OverlayAccentColor = OverlayAppearance.DefaultAccentHex;
        _settings.Save();
        SyncOverlayAppearanceControls();
        ApplyOverlayAppearanceToLiveOverlays();
        _session?.SetAnalysisUpdateHz(4);
    }

    private void ApplyOverlayAppearanceToLiveOverlays()
    {
        OverlayAppearance appearance = _settings.GetOverlayAppearance();
        _overlayPreview.SetAppearance(appearance);
        foreach (StatusOverlayForm overlay in _statusOverlays)
            overlay.ApplyAppearance(appearance);
        foreach (CieOverlayForm overlay in _cieOverlays)
            overlay.ApplyAppearance(appearance);
        foreach (AnalysisOverlayForm overlay in _analysisOverlays)
            overlay.ApplyAppearance(appearance);
    }

    private void RefreshOperationModeChoices()
    {
        string selectedMode = NormalizeOperationMode(_settings.OperationMode);
        _updatingModeSelection = true;
        _modeSegmented.SetItems(
            [Localization.T("OperationModeMirrorShort"), Localization.T("OperationModeAnalysisShort")],
            selectedMode == AnalysisOperationMode ? 1 : 0);
        _updatingModeSelection = false;
    }

    private void ChangeOperationMode()
    {
        if (_updatingModeSelection)
            return;

        _settings.OperationMode = _modeSegmented.SelectedIndex == 1
            ? AnalysisOperationMode
            : MirrorOperationMode;
        _settings.Save();
        SetRunningState(_session is not null);
        RefreshFrameRateModeChoices();
    }

    private void RefreshFalseColorChoices()
    {
        int current = Math.Clamp(_falseColorSegmented.SelectedIndex, 0, 2);
        _updatingFalseColorSelection = true;
        _falseColorSegmented.SetItems(
            [
                Localization.T("FalseColorOff"),
                Localization.T("FalseColorLuminanceShort"),
                Localization.T("FalseColorGamutShort")
            ],
            current);
        _updatingFalseColorSelection = false;
    }

    private void InitializeAblProfiles()
    {
        _settings.NormalizeAblProfiles();
        _showAblEstimate.Checked =
            _settings.AblEstimationEnabled && GetActiveAblProfile() is not null;
        RefreshAblProfileChoices();
        UpdateAblControlsState();
    }

    private void RefreshAblProfileChoices()
    {
        string activeProfileId = _settings.ActiveAblProfileId;
        AblProfileChoice[] validChoices = _settings.AblProfiles
            .Where(profile => profile.IsValid)
            .Select(profile => new AblProfileChoice(profile.Id, profile.Name))
            .ToArray();

        int idx = Array.FindIndex(validChoices, c => string.Equals(c.Id, activeProfileId, StringComparison.Ordinal));
        if (idx < 0 && validChoices.Length > 0)
        {
            idx = 0;
            _settings.ActiveAblProfileId = validChoices[0].Id;
        }

        _updatingAblProfileSelection = true;
        _ablProfileSelect.SetItems(validChoices, idx);
        _updatingAblProfileSelection = false;
    }

    private void ChangeAblProfile()
    {
        if (_updatingAblProfileSelection ||
            _ablProfileSelect.SelectedItem is not AblProfileChoice choice)
        {
            return;
        }

        _settings.ActiveAblProfileId = choice.Id;
        _settings.Save();
        UpdateAblControlsState();
        ApplyAblProfileToSession();
    }

    private void ChangeAblEstimation()
    {
        if (_showAblEstimate.Checked && GetActiveAblProfile() is null)
        {
            _showAblEstimate.Checked = false;
            return;
        }

        _settings.AblEstimationEnabled = _showAblEstimate.Checked;
        _settings.Save();
        ApplyAblProfileToSession();
    }

    private void ManageAblProfiles()
    {
        using AblProfileManagerForm dialog = new(
            _settings.AblProfiles,
            _settings.ActiveAblProfileId);
        if (_singleDisplayAnalysis)
        {
            dialog.Shown += (_, _) => NativeMethods.SetWindowDisplayAffinity(
                dialog.Handle,
                NativeMethods.WdaExcludeFromCapture);
        }
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _settings.AblProfiles = dialog.Profiles.Select(profile => profile.Clone()).ToList();
        _settings.ActiveAblProfileId = dialog.ActiveProfileId;
        _settings.NormalizeAblProfiles();
        if (GetActiveAblProfile() is null)
        {
            _settings.AblEstimationEnabled = false;
            _showAblEstimate.Checked = false;
        }
        _settings.Save(allowClearingAblProfiles: true);
        RefreshAblProfileChoices();
        UpdateAblControlsState();
        ApplyAblProfileToSession();
    }

    private AblProfile? GetActiveAblProfile() => _settings.AblProfiles.FirstOrDefault(profile =>
        profile.IsValid &&
        string.Equals(profile.Id, _settings.ActiveAblProfileId, StringComparison.Ordinal));

    private void UpdateAblControlsState()
    {
        bool hasActiveProfile = GetActiveAblProfile() is not null;
        _showAblEstimate.Enabled = hasActiveProfile;
        _ablProfileSelect.Enabled = _ablProfileSelect.ItemCount > 0;
        _manageAblProfilesButton.Enabled = true;
    }

    private void ApplyAblProfileToSession()
    {
        AblProfile? profile = _showAblEstimate.Checked ? GetActiveAblProfile() : null;
        _session?.SetAblProfile(profile);
        foreach (StatusOverlayForm overlay in _statusOverlays)
            overlay.SetAblEstimate(profile is not null, profile?.Name ?? string.Empty);
    }

    private void RefreshFrameRateModeChoices()
    {
        string selectedMode = NormalizeFrameRateMode(_settings.FrameRateMode);
        int selectedIndex = selectedMode switch
        {
            FixedFrameRateMode => 1,
            UnlimitedFrameRateMode => 2,
            _ => 0
        };
        _updatingFrameRateSelection = true;
        _frameRateSegmented.SetItems(
            [
                Localization.T("FrameRateFollowShort"),
                Localization.T("FrameRateFixedShort"),
                Localization.T("FrameRateUnlimitedShort")
            ],
            selectedIndex);
        _updatingFrameRateSelection = false;
    }

    private void ChangeFrameRateMode()
    {
        if (_updatingFrameRateSelection)
            return;

        _settings.FrameRateMode = _frameRateSegmented.SelectedIndex switch
        {
            1 => FixedFrameRateMode,
            2 => UnlimitedFrameRateMode,
            _ => FollowOutputFrameRateMode
        };
        _settings.Save();
        UpdateFrameRateSettingsState();
        LayoutCardsOnly();
    }

    private void ChangeFrameRateLimit()
    {
        if (_updatingFrameRateSelection)
            return;

        _settings.FrameRateLimit = _frameRateLimit.Value;
        _settings.Save();
    }

    private void UpdateFrameRateSettingsState()
    {
        bool fixedFrameRate = NormalizeFrameRateMode(_settings.FrameRateMode) == FixedFrameRateMode;
        _frameRateLimit.Visible = fixedFrameRate;
        _frameRateUnitLabel.Visible = fixedFrameRate;
        _frameRateLimit.Enabled = _session is null && fixedFrameRate;
    }

    private void RefreshScreenshotModeChoices()
    {
        string selectedMode = NormalizeScreenshotMode(_settings.ScreenshotSaveMode);
        int index = selectedMode switch
        {
            PromptScreenshotMode => 1,
            ClipboardScreenshotMode => 2,
            _ => 0
        };
        _updatingScreenshotSelection = true;
        _screenshotModeSegmented.SetItems(
            [
                Localization.T("ScreenshotAutoShort"),
                Localization.T("ScreenshotPromptShort"),
                Localization.T("ScreenshotClipboardOnly")
            ],
            index);
        _updatingScreenshotSelection = false;
    }

    private void ChangeScreenshotMode()
    {
        if (_updatingScreenshotSelection)
            return;

        _settings.ScreenshotSaveMode = _screenshotModeSegmented.SelectedIndex switch
        {
            1 => PromptScreenshotMode,
            2 => ClipboardScreenshotMode,
            _ => AutomaticScreenshotMode
        };
        _settings.Save();
        UpdateScreenshotSettingsState();
    }

    private void ChangeCopyToClipboard()
    {
        _settings.CopyScreenshotToClipboard = _copyToClipboardSwitch.Checked;
        _settings.Save();
    }

    private void UpdateScreenshotSettingsState()
    {
        string mode = NormalizeScreenshotMode(_settings.ScreenshotSaveMode);
        bool automatic = mode == AutomaticScreenshotMode;
        bool clipboardOnly = mode == ClipboardScreenshotMode;
        bool mirroring = _session is not null && !_singleDisplayAnalysis;
        bool configurable = _session is null || !_singleDisplayAnalysis;

        _screenshotDirectoryField.Text = GetScreenshotDirectory();
        _screenshotModeSegmented.Enabled = configurable;
        _copyToClipboardSwitch.Enabled = configurable && !clipboardOnly;
        _screenshotDirectoryField.Enabled = configurable && automatic;
        _chooseScreenshotDirectoryButton.Enabled = configurable && automatic;
        _fullScreenshotButton.Enabled = mirroring;
        _regionScreenshotButton.Enabled = mirroring;
    }

    private void ChooseScreenshotDirectory()
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = Localization.T("ChooseFolder"),
            UseDescriptionForTitle = true,
            SelectedPath = GetScreenshotDirectory(),
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _settings.ScreenshotDirectory = dialog.SelectedPath;
        _settings.Save();
        UpdateScreenshotSettingsState();
    }

    private void ChangeLanguage()
    {
        if (_updatingLanguageSelection || _languageSelect.SelectedItem is not LanguageChoice choice)
            return;

        _settings.Language = choice.Code;
        _settings.Save();
        Localization.SetPreference(choice.Code);
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        ApplyLocalizedControlText(this);
        RefreshThemeChoices();
        RefreshScreenshotModeChoices();
        RefreshFrameRateModeChoices();
        RefreshOperationModeChoices();
        RefreshFalseColorChoices();
        UpdateScreenshotSettingsState();
        UpdateFrameRateSettingsState();
        UpdateAblControlsState();
        RefreshDisplayLabels();
        UpdateModeText();
        _overlayPreview.Invalidate();
        _footerTelemetry.Invalidate();
        _trayShowItem.Text = Localization.T("TrayShow");
        _trayExitItem.Text = Localization.T("TrayExit");
        UpdateTrayState();
        foreach (StatusOverlayForm overlay in _statusOverlays)
            overlay.ApplyLanguage();
        foreach (CieOverlayForm overlay in _cieOverlays)
            overlay.ApplyLanguage();
        foreach (AnalysisOverlayForm overlay in _analysisOverlays)
            overlay.ApplyLanguage();
        PerformFullLayout();
    }

    private void RefreshDisplayLabels()
    {
        if (_displays.Count == 0)
            return;

        string? captureId = (_captureSelect.SelectedItem as DisplayTarget)?.StableId;
        string? presentId = (_presentSelect.SelectedItem as DisplayTarget)?.StableId;
        _updatingDisplaySelection = true;
        try
        {
            _captureSelect.SetItems(_displays.Cast<object>());
            _presentSelect.SetItems(_displays.Cast<object>());
            SelectDisplay(_captureSelect, captureId);
            SelectDisplay(_presentSelect, presentId);
        }
        finally
        {
            _updatingDisplaySelection = false;
        }
    }

    private void SaveDisplaySelection(bool capture)
    {
        if (_updatingDisplaySelection)
            return;

        HextaSelect selector = capture ? _captureSelect : _presentSelect;
        if (selector.SelectedItem is not DisplayTarget display)
            return;

        if (capture)
            _settings.CaptureDisplayId = display.StableId;
        else
            _settings.OutputDisplayId = display.StableId;
        _settings.Save();
    }

    private static void ApplyLocalizedControlText(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control.Tag is string key)
                control.Text = Localization.T(key);
            ApplyLocalizedControlText(control);
        }
    }

    private static string NormalizeLanguagePreference(string? preference) => preference switch
    {
        Localization.SimplifiedChinese => Localization.SimplifiedChinese,
        Localization.English => Localization.English,
        _ => Localization.Automatic
    };

    private static string NormalizeScreenshotMode(string? mode) => mode switch
    {
        PromptScreenshotMode => PromptScreenshotMode,
        ClipboardScreenshotMode => ClipboardScreenshotMode,
        _ => AutomaticScreenshotMode
    };

    private static string NormalizeFrameRateMode(string? mode) => mode switch
    {
        FixedFrameRateMode => FixedFrameRateMode,
        UnlimitedFrameRateMode => UnlimitedFrameRateMode,
        _ => FollowOutputFrameRateMode
    };

    private static string NormalizeOperationMode(string? mode) => mode switch
    {
        AnalysisOperationMode => AnalysisOperationMode,
        _ => MirrorOperationMode
    };

    private bool IsAnalysisModeSelected() => _modeSegmented.SelectedIndex == 1;

    private FrameRateMode GetFrameRateMode() => NormalizeFrameRateMode(_settings.FrameRateMode) switch
    {
        FixedFrameRateMode => FrameRateMode.Fixed,
        UnlimitedFrameRateMode => FrameRateMode.Unlimited,
        _ => FrameRateMode.FollowOutput
    };

    private string GetScreenshotDirectory() => string.IsNullOrWhiteSpace(_settings.ScreenshotDirectory)
        ? Path.Combine(AppContext.BaseDirectory, "Screenshots")
        : _settings.ScreenshotDirectory;

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        RegisterConfiguredHotKeys();
    }

    private bool RefreshDisplays(
        string? captureDisplayId = null,
        string? presentDisplayId = null)
    {
        if (_session is not null)
            return false;

        captureDisplayId ??= _settings.CaptureDisplayId;
        presentDisplayId ??= _settings.OutputDisplayId;

        try
        {
            _displays = DxgiDisplayEnumerator.GetDisplays().Where(x => x.AttachedToDesktop).ToArray();
            _updatingDisplaySelection = true;
            try
            {
                _captureSelect.SetItems(_displays.Cast<object>());
                _presentSelect.SetItems(_displays.Cast<object>());

                if (_displays.Count == 0)
                {
                    ShowFooterNotice(Localization.T("StatusNoDisplays"), isError: true, autoClear: false);
                    _startButton.Enabled = false;
                    UpdateModeText();
                    return false;
                }

                if (!SelectDisplay(_captureSelect, captureDisplayId))
                    _captureSelect.SetSelectedIndexSilently(0);
                if (!SelectDisplay(_presentSelect, presentDisplayId))
                    _presentSelect.SetSelectedIndexSilently(FindDefaultPresentIndex());
            }
            finally
            {
                _updatingDisplaySelection = false;
            }

            SaveInitialDisplaySelection();
            RefreshFrameRateModeChoices();
            SetRunningState(false);
            _footerTelemetry.ClearNotice();
            return true;
        }
        catch (Exception exception)
        {
            ShowFooterNotice(exception.Message, isError: true, autoClear: false);
            _startButton.Enabled = false;
            MessageBox.Show(
                exception.ToString(),
                Localization.T("EnumerateFailed"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    private static bool SelectDisplay(HextaSelect selector, string? displayId)
    {
        if (string.IsNullOrWhiteSpace(displayId))
            return false;

        for (int i = 0; i < selector.Items.Count; i++)
        {
            if (selector.Items[i] is DisplayTarget display &&
                (string.Equals(display.StableId, displayId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(display.DeviceName, displayId, StringComparison.OrdinalIgnoreCase)))
            {
                selector.SetSelectedIndexSilently(i);
                return true;
            }
        }

        return false;
    }

    private void SaveInitialDisplaySelection()
    {
        bool changed = false;
        if (string.IsNullOrWhiteSpace(_settings.CaptureDisplayId) &&
            _captureSelect.SelectedItem is DisplayTarget capture)
        {
            _settings.CaptureDisplayId = capture.StableId;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.OutputDisplayId) &&
            _displays.Count > 1 &&
            _presentSelect.SelectedItem is DisplayTarget present &&
            _captureSelect.SelectedItem is DisplayTarget selectedCapture &&
            !string.Equals(present.StableId, selectedCapture.StableId, StringComparison.OrdinalIgnoreCase))
        {
            _settings.OutputDisplayId = present.StableId;
            changed = true;
        }

        if (changed)
            _settings.Save();
    }

    private int FindDefaultPresentIndex()
    {
        if (_displays.Count < 2)
            return 0;

        DisplayTarget capture = _captureSelect.SelectedItem as DisplayTarget ?? _displays[0];
        for (int i = 0; i < _displays.Count; i++)
        {
            if (_displays[i].GlobalIndex != capture.GlobalIndex &&
                _displays[i].AdapterIndex == capture.AdapterIndex)
                return i;
        }

        for (int i = 0; i < _displays.Count; i++)
        {
            if (_displays[i].GlobalIndex != capture.GlobalIndex)
                return i;
        }

        return 0;
    }

    private void StartMirror(
        bool refreshDisplayState = true,
        bool moveOutputWindows = true,
        bool? analysisOnlyOverride = null)
    {
        if (_session is not null)
            return;

        if (refreshDisplayState)
        {
            string? captureDisplayId = (_captureSelect.SelectedItem as DisplayTarget)?.StableId;
            string? presentDisplayId = (_presentSelect.SelectedItem as DisplayTarget)?.StableId;
            if (!RefreshDisplays(captureDisplayId, presentDisplayId))
                return;
        }

        if (_captureSelect.SelectedItem is not DisplayTarget capture)
            return;

        bool analysisOnly = analysisOnlyOverride ?? IsAnalysisModeSelected();
        if (analysisOnly &&
            !_showStatusOverlay.Checked &&
            !_showLuminanceMarkers.Checked &&
            !_showCieAnalysis.Checked)
        {
            MessageBox.Show(
                this,
                Localization.T("NoVisibleAnalysisContent"),
                Localization.T("NoVisibleAnalysisTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        IReadOnlyList<DisplayTarget> targets = analysisOnly ? [] : ResolveOutputTargets(capture);
        if (!analysisOnly && targets.Count == 0)
        {
            MessageBox.Show(
                Localization.T("NoGpuOutputs"),
                Localization.T("NoOutput"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        DisplayTarget[] nonHdrTargets = targets.Where(x => !x.IsHdrActive).ToArray();
        if (nonHdrTargets.Length > 0)
        {
            string separator = Localization.Current == UiLanguage.Chinese ? "、" : ", ";
            string names = string.Join(separator, nonHdrTargets.Select(x => x.DeviceName));
            DialogResult result = MessageBox.Show(
                Localization.F("NonHdrWarning", names),
                Localization.T("NonHdrTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
                return;
        }

        try
        {
            _singleDisplayAnalysis = analysisOnly;
            if (analysisOnly && !SetControlWindowCaptureExclusion(true))
                throw new InvalidOperationException(Localization.T("CaptureExclusionFailed"));

            AblProfile? activeAblProfile =
                _showAblEstimate.Checked ? GetActiveAblProfile() : null;
            FalseColorMode falseColorMode = analysisOnly ? FalseColorMode.None : GetFalseColorMode();
            OverlayAppearance appearance = _settings.GetOverlayAppearance();

            PositionOnCaptureDisplay(capture);
            if (!analysisOnly && moveOutputWindows && _moveOutputWindows.Checked)
            {
                Rectangle captureWorkArea = FindScreenForDisplay(capture).WorkingArea;
                WindowRelocator.MoveWindowsToCapture(targets, captureWorkArea);
            }

            List<MirrorOutputBinding> bindings = [];
            IReadOnlyList<DisplayTarget> overlayTargets = analysisOnly ? [capture] : targets;
            foreach (DisplayTarget target in overlayTargets)
            {
                MirrorForm? mirrorWindow = null;
                if (!analysisOnly)
                {
                    mirrorWindow = new MirrorForm(target.Bounds, _mouseThrough.Checked);
                    mirrorWindow.Show();
                    _mirrorWindows.Add(mirrorWindow);
                    bindings.Add(new MirrorOutputBinding(target, mirrorWindow.Handle));
                }

                StatusOverlayForm overlay = new(
                    target.Bounds,
                    capture,
                    target,
                    analysisOnly || _mouseThrough.Checked,
                    _showPointerLuminance.Checked,
                    falseColorMode == FalseColorMode.Luminance,
                    analysisOnly: analysisOnly);
                overlay.SetFalseColorMode(falseColorMode);
                overlay.SetAblEstimate(
                    activeAblProfile is not null,
                    activeAblProfile?.Name ?? string.Empty);
                overlay.ApplyAppearance(appearance);
                if (mirrorWindow is null)
                    overlay.Show();
                else
                    overlay.Show(mirrorWindow);
                _statusOverlays.Add(overlay);
                if (analysisOnly && !overlay.SetCaptureExclusion(true))
                    throw new InvalidOperationException(Localization.T("CaptureExclusionFailed"));
                if (!_showStatusOverlay.Checked)
                    overlay.Hide();

                CieOverlayForm cieOverlay = new(target.Bounds);
                cieOverlay.ApplyAppearance(appearance);
                if (mirrorWindow is null)
                    cieOverlay.Show();
                else
                    cieOverlay.Show(mirrorWindow);
                _cieOverlays.Add(cieOverlay);
                if (analysisOnly && !cieOverlay.SetCaptureExclusion(true))
                    throw new InvalidOperationException(Localization.T("CaptureExclusionFailed"));
                if (!_showCieAnalysis.Checked)
                    cieOverlay.Hide();

                AnalysisOverlayForm analysisOverlay = new(
                    target.Bounds,
                    capture,
                    _showLuminanceMarkers.Checked);
                analysisOverlay.ApplyAppearance(appearance);
                if (mirrorWindow is null)
                    analysisOverlay.Show();
                else
                    analysisOverlay.Show(mirrorWindow);
                _analysisOverlays.Add(analysisOverlay);
                if (analysisOnly && !analysisOverlay.SetCaptureExclusion(true))
                    throw new InvalidOperationException(Localization.T("CaptureExclusionFailed"));
                if (!analysisOverlay.HasVisibleContent)
                    analysisOverlay.Hide();
            }

            _session = new MirrorSession(
                capture,
                bindings,
                _paperWhite.Value,
                GetFrameRateMode(),
                _frameRateLimit.Value,
                analysisOnly || _renderCursor.Checked,
                falseColorMode == FalseColorMode.Luminance,
                true);
            _session.SetFalseColorMode(falseColorMode);
            _session.TelemetryChanged += OnSessionTelemetryChanged;
            _session.LuminanceChanged += OnSessionLuminanceChanged;
            _session.GamutChanged += OnSessionGamutChanged;
            _session.Failed += OnSessionFailed;
            _session.Stopped += OnSessionStopped;
            _session.SetGamutAnalysis(_showCieAnalysis.Checked);
            _session.SetAnalysisUpdateHz(GetSelectedOverlayUpdateHz());
            _session.SetAblProfile(activeAblProfile);
            _session.Start();

            SetRunningState(true);
            _footerTelemetry.ClearNotice();
            if (analysisOnly)
            {
                TopMost = false;
                HideToTray();
            }
            else
            {
                TopMost = true;
                BringToFront();
                Activate();
            }
        }
        catch (Exception exception)
        {
            StopMirror();
            MessageBox.Show(
                this,
                exception.ToString(),
                Localization.T(analysisOnly ? "StartAnalysisFailed" : "StartFailed"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private IReadOnlyList<DisplayTarget> ResolveOutputTargets(DisplayTarget capture)
    {
        if (_allOutputs.Checked)
        {
            return _displays
                .Where(x => x.GlobalIndex != capture.GlobalIndex && x.AdapterIndex == capture.AdapterIndex)
                .ToArray();
        }

        if (_presentSelect.SelectedItem is not DisplayTarget present)
            return [];
        if (present.GlobalIndex == capture.GlobalIndex)
        {
            MessageBox.Show(
                Localization.T("SameDisplay"),
                "HDRScreenMirror",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return [];
        }
        if (present.AdapterIndex != capture.AdapterIndex)
        {
            MessageBox.Show(
                Localization.T("CrossGpu"),
                "HDRScreenMirror",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return [];
        }

        return [present];
    }

    private void StopMirror(bool restoreControlWindow = true)
    {
        bool wasSingleDisplayAnalysis = _singleDisplayAnalysis;
        MirrorSession? session = _session;
        _session = null;

        if (session is not null)
        {
            session.TelemetryChanged -= OnSessionTelemetryChanged;
            session.LuminanceChanged -= OnSessionLuminanceChanged;
            session.GamutChanged -= OnSessionGamutChanged;
            session.Failed -= OnSessionFailed;
            session.Stopped -= OnSessionStopped;
            session.Stop();
            session.Dispose();
        }

        foreach (StatusOverlayForm overlay in _statusOverlays)
        {
            overlay.Close();
            overlay.Dispose();
        }
        _statusOverlays.Clear();

        foreach (CieOverlayForm overlay in _cieOverlays)
        {
            overlay.Close();
            overlay.Dispose();
        }
        _cieOverlays.Clear();

        foreach (AnalysisOverlayForm overlay in _analysisOverlays)
        {
            overlay.Close();
            overlay.Dispose();
        }
        _analysisOverlays.Clear();

        foreach (MirrorForm mirrorWindow in _mirrorWindows)
        {
            mirrorWindow.Close();
            mirrorWindow.Dispose();
        }
        _mirrorWindows.Clear();

        SetControlWindowCaptureExclusion(false);
        _singleDisplayAnalysis = false;
        TopMost = false;
        _footerTelemetry.ClearTelemetry();
        SetRunningState(false);
        if (wasSingleDisplayAnalysis && restoreControlWindow && !_exitRequested)
            RecallControlWindow();
    }

    private void SetRunningState(bool running)
    {
        bool analysisMode = running ? _singleDisplayAnalysis : IsAnalysisModeSelected();
        _captureSelect.Enabled = !running;
        _presentSelect.Enabled = !running && !analysisMode && !_allOutputs.Checked;
        _allOutputs.Enabled = !running && !analysisMode && _displays.Count > 1;
        _paperWhite.Enabled = !running;
        _resetPaperWhiteButton.Enabled = !running;
        _frameRateSegmented.Enabled = !running;
        _modeSegmented.Enabled = !running;
        UpdateFrameRateSettingsState();
        _showStatusOverlay.Enabled = true;
        _renderCursor.Enabled = !running && !analysisMode;
        _showPointerLuminance.Enabled = true;
        _falseColorSegmented.Enabled = !analysisMode;
        _showLuminanceMarkers.Enabled = true;
        UpdateAblControlsState();
        _showCieAnalysis.Enabled = true;
        _mouseThrough.Enabled = !running && !analysisMode;
        _moveOutputWindows.Enabled = !running;
        _refreshButton.Enabled = !running;
        _startButton.Enabled = !running &&
            _displays.Count > 0 &&
            (analysisMode || _displays.Count > 1);
        _stopButton.Enabled = running;
        _stopButton.Variant = running ? HextaButtonVariant.Danger : HextaButtonVariant.Secondary;
        UpdateScreenshotSettingsState();
        UpdateModeText();
        UpdateTrayState();
    }

    private void UpdatePresentSelectorState()
    {
        bool analysisMode = _session is not null
            ? _singleDisplayAnalysis
            : IsAnalysisModeSelected();
        _presentSelect.Enabled = _session is null && !analysisMode && !_allOutputs.Checked;
    }

    private void UpdateModeText()
    {
        bool analysisMode = _session is not null
            ? _singleDisplayAnalysis
            : IsAnalysisModeSelected();
        _startButton.Text = Localization.T(analysisMode ? "StartAnalysis" : "StartMirror");
        _showStatusOverlay.Text = Localization.T("StatusOverlay");
        _showCieAnalysis.Text = Localization.T("CieAnalysis");
    }

    private bool SetControlWindowCaptureExclusion(bool excluded)
    {
        if (!IsHandleCreated)
            return !excluded;
        if (!excluded && !_controlWindowCaptureExcluded)
            return true;

        bool applied = NativeMethods.SetWindowDisplayAffinity(
            Handle,
            excluded ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);
        if (applied)
            _controlWindowCaptureExcluded = excluded;
        return applied;
    }

    private void UpdateStatusOverlayVisibility()
    {
        foreach (StatusOverlayForm overlay in _statusOverlays)
        {
            if (_showStatusOverlay.Checked)
                overlay.Show();
            else
                overlay.Hide();
        }
        EnsureSingleDisplayAnalysisOverlaysOnTop();
    }

    private void UpdatePointerLuminanceVisibility()
    {
        foreach (StatusOverlayForm overlay in _statusOverlays)
            overlay.SetShowPointerLuminance(_showPointerLuminance.Checked);
    }

    private FalseColorMode GetFalseColorMode() => _falseColorSegmented.SelectedIndex switch
    {
        1 => FalseColorMode.Luminance,
        2 => FalseColorMode.Gamut,
        _ => FalseColorMode.None
    };

    private void UpdateFalseColorMode()
    {
        if (_updatingFalseColorSelection)
            return;

        FalseColorMode mode = _singleDisplayAnalysis ? FalseColorMode.None : GetFalseColorMode();
        _session?.SetFalseColorMode(mode);
        foreach (StatusOverlayForm overlay in _statusOverlays)
            overlay.SetFalseColorMode(mode);
    }

    private void UpdateLuminanceMarkerVisibility()
    {
        foreach (AnalysisOverlayForm overlay in _analysisOverlays)
        {
            overlay.SetShowMarkers(_showLuminanceMarkers.Checked);
            UpdateAnalysisOverlayVisibility(overlay);
        }
        EnsureSingleDisplayAnalysisOverlaysOnTop();
    }

    private void UpdateCieAnalysisVisibility()
    {
        _session?.SetGamutAnalysis(_showCieAnalysis.Checked);
        foreach (CieOverlayForm overlay in _cieOverlays)
        {
            if (_showCieAnalysis.Checked)
                overlay.Show();
            else
                overlay.Hide();
        }
        EnsureSingleDisplayAnalysisOverlaysOnTop();
    }

    private static void UpdateAnalysisOverlayVisibility(AnalysisOverlayForm overlay)
    {
        if (overlay.HasVisibleContent)
            overlay.Show();
        else
            overlay.Hide();
    }

    private async Task<Bitmap[]?> CaptureCompositedOutputBitmapsAsync(MirrorSession session)
    {
        foreach (AnalysisOverlayForm overlay in _analysisOverlays)
            overlay.HideScreenshotNotification();

        IReadOnlyList<HdrScreenshotFrame> frames = await session.CaptureOutputFramesAsync();
        if (!ReferenceEquals(_session, session) || frames.Count != _mirrorWindows.Count)
            return null;

        float paperWhiteNits = _paperWhite.Value;
        FalseColorMode falseColorMode = GetFalseColorMode();
        Bitmap[] bitmaps = await Task.Run(() => ConvertScreenshotFrames(
            frames,
            paperWhiteNits,
            falseColorMode));
        if (!ReferenceEquals(_session, session) || bitmaps.Length != _mirrorWindows.Count)
        {
            DisposeBitmaps(bitmaps);
            return null;
        }

        for (int i = 0; i < bitmaps.Length; i++)
            CompositeScreenshotOverlays(bitmaps[i], i);

        return bitmaps;
    }

    private async void TakeOutputScreenshots()
    {
        if (_takingScreenshot)
            return;

        if (_session is null || _singleDisplayAnalysis || _mirrorWindows.Count == 0)
        {
            ShowFooterNotice(Localization.T("ScreenshotNeedsMirror"));
            return;
        }

        _takingScreenshot = true;
        string? toastKey = null;
        try
        {
            string mode = NormalizeScreenshotMode(_settings.ScreenshotSaveMode);
            bool clipboardOnly = mode == ClipboardScreenshotMode;
            string[]? paths = clipboardOnly ? null : ResolveScreenshotPaths();
            if (!clipboardOnly && paths is null)
                return;

            MirrorSession session = _session;
            Bitmap[]? bitmaps = await CaptureCompositedOutputBitmapsAsync(session);
            if (bitmaps is null)
                return;

            try
            {
                if (clipboardOnly)
                {
                    CopyBitmapToClipboard(bitmaps[0]);
                    ShowFooterNotice(Localization.T("ScreenshotCopiedStatus"));
                    toastKey = "ScreenshotCopiedToast";
                }
                else
                {
                    for (int i = 0; i < bitmaps.Length; i++)
                        bitmaps[i].Save(paths![i], ImageFormat.Png);

                    if (_copyToClipboardSwitch.Checked && bitmaps.Length > 0)
                        CopyBitmapToClipboard(bitmaps[0]);

                    ShowFooterNotice(paths!.Length == 1
                        ? Localization.F("ScreenshotSaved", Path.GetFileName(paths[0]))
                        : Localization.F("ScreenshotsSaved", paths.Length, Path.GetDirectoryName(paths[0]) ?? string.Empty));
                    toastKey = "ScreenshotSuccessToast";
                }
            }
            finally
            {
                DisposeBitmaps(bitmaps);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                Localization.T("ScreenshotFailed"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _takingScreenshot = false;
        }

        if (toastKey is not null)
        {
            foreach (AnalysisOverlayForm overlay in _analysisOverlays)
                overlay.ShowScreenshotNotification(toastKey);
        }
    }

    private async void TakeRegionScreenshot()
    {
        if (_takingScreenshot)
            return;

        if (_session is null || _singleDisplayAnalysis || _mirrorWindows.Count == 0)
        {
            ShowFooterNotice(Localization.T("ScreenshotNeedsMirror"));
            return;
        }

        _takingScreenshot = true;
        string? toastKey = null;
        Bitmap[]? bitmaps = null;
        try
        {
            MirrorSession session = _session;
            bitmaps = await CaptureCompositedOutputBitmapsAsync(session);
            if (bitmaps is null)
                return;

            RegionScreenshotSelection? selection = await ShowRegionSelectorsAsync(bitmaps);
            if (selection is null)
                return;

            if (selection.OutputIndex < 0 || selection.OutputIndex >= bitmaps.Length)
                return;

            using Bitmap cropped = CropBitmap(bitmaps[selection.OutputIndex], selection.PixelBounds);
            if (selection.Action == RegionScreenshotAction.Copy)
            {
                CopyBitmapToClipboard(cropped);
                ShowFooterNotice(Localization.T("ScreenshotCopiedStatus"));
                toastKey = "ScreenshotCopiedToast";
            }
            else
            {
                string? path = ResolveSingleRegionScreenshotPath(selection.OutputIndex);
                if (path is null)
                    return;

                cropped.Save(path, ImageFormat.Png);
                if (_copyToClipboardSwitch.Checked)
                    CopyBitmapToClipboard(cropped);
                ShowFooterNotice(Localization.F("ScreenshotSaved", Path.GetFileName(path)));
                toastKey = "ScreenshotSuccessToast";
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                Localization.T("ScreenshotFailed"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (bitmaps is not null)
                DisposeBitmaps(bitmaps);
            _takingScreenshot = false;
        }

        if (toastKey is not null)
        {
            foreach (AnalysisOverlayForm overlay in _analysisOverlays)
                overlay.ShowScreenshotNotification(toastKey);
        }
    }

    private async Task<RegionScreenshotSelection?> ShowRegionSelectorsAsync(Bitmap[] bitmaps)
    {
        TaskCompletionSource<RegionScreenshotSelection?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<RegionScreenshotOverlayForm> selectors = [];

        void CompleteSelection(RegionScreenshotSelection? result)
        {
            if (!tcs.TrySetResult(result))
                return;

            foreach (RegionScreenshotOverlayForm form in selectors)
            {
                if (!form.IsDisposed)
                    form.Hide();
            }
        }

        try
        {
            for (int i = 0; i < bitmaps.Length && i < _mirrorWindows.Count; i++)
            {
                RegionScreenshotOverlayForm selector = new(i, _mirrorWindows[i].Bounds, bitmaps[i]);
                selector.SelectionConfirmed += result => CompleteSelection(result);
                selector.SelectionCanceled += () => CompleteSelection(null);
                selector.FormClosed += (_, _) => CompleteSelection(null);
                selectors.Add(selector);
                selector.Show();
            }

            if (selectors.Count == 0)
                return null;

            return await tcs.Task;
        }
        finally
        {
            foreach (RegionScreenshotOverlayForm form in selectors)
            {
                if (!form.IsDisposed)
                {
                    form.Close();
                    form.Dispose();
                }
            }
        }
    }

    private static Bitmap CropBitmap(Bitmap source, Rectangle bounds)
    {
        Rectangle safe = Rectangle.Intersect(bounds, new Rectangle(0, 0, source.Width, source.Height));
        if (safe.Width <= 0 || safe.Height <= 0)
            safe = new Rectangle(0, 0, Math.Max(1, source.Width), Math.Max(1, source.Height));

        Bitmap cropped = new(safe.Width, safe.Height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(cropped);
        graphics.DrawImage(source, new Rectangle(0, 0, safe.Width, safe.Height), safe, GraphicsUnit.Pixel);
        return cropped;
    }

    private static void CopyBitmapToClipboard(Bitmap bitmap)
    {
        using Bitmap copy = new(bitmap);
        Clipboard.SetDataObject(copy, copy: true, retryTimes: 5, retryDelay: 50);
    }

    private static void DisposeBitmaps(IEnumerable<Bitmap?> bitmaps)
    {
        foreach (Bitmap? bitmap in bitmaps)
            bitmap?.Dispose();
    }

    private static Bitmap[] ConvertScreenshotFrames(
        IReadOnlyList<HdrScreenshotFrame> frames,
        float paperWhiteNits,
        FalseColorMode falseColorMode)
    {
        Bitmap[] bitmaps = new Bitmap[frames.Count];
        try
        {
            for (int i = 0; i < frames.Count; i++)
            {
                bitmaps[i] = HdrScreenshotCapture.ConvertToSdrBitmap(
                    frames[i],
                    paperWhiteNits,
                    falseColorMode);
            }
            return bitmaps;
        }
        catch
        {
            DisposeBitmaps(bitmaps);
            throw;
        }
    }

    private void CompositeScreenshotOverlays(Bitmap bitmap, int outputIndex)
    {
        if (outputIndex >= _mirrorWindows.Count)
            return;

        Rectangle mirrorBounds = _mirrorWindows[outputIndex].Bounds;
        if (mirrorBounds.Width <= 0 || mirrorBounds.Height <= 0)
            return;

        float scaleX = bitmap.Width / (float)mirrorBounds.Width;
        float scaleY = bitmap.Height / (float)mirrorBounds.Height;
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        if (outputIndex < _statusOverlays.Count && _statusOverlays[outputIndex].Visible)
        {
            StatusOverlayForm overlay = _statusOverlays[outputIndex];
            using Bitmap panel = overlay.RenderScreenshotBitmap();
            Rectangle destination = new(
                (int)Math.Round((overlay.Bounds.Left - mirrorBounds.Left) * scaleX),
                (int)Math.Round((overlay.Bounds.Top - mirrorBounds.Top) * scaleY),
                Math.Max(1, (int)Math.Round(overlay.Bounds.Width * scaleX)),
                Math.Max(1, (int)Math.Round(overlay.Bounds.Height * scaleY)));
            using ImageAttributes opacity = new();
            ColorMatrix matrix = new() { Matrix33 = (float)overlay.Opacity };
            opacity.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            graphics.DrawImage(
                panel,
                destination,
                0,
                0,
                panel.Width,
                panel.Height,
                GraphicsUnit.Pixel,
                opacity);
        }

        if (outputIndex < _cieOverlays.Count && _cieOverlays[outputIndex].Visible)
        {
            CieOverlayForm overlay = _cieOverlays[outputIndex];
            using Bitmap panel = overlay.RenderScreenshotBitmap();
            Rectangle destination = new(
                (int)Math.Round((overlay.Bounds.Left - mirrorBounds.Left) * scaleX),
                (int)Math.Round((overlay.Bounds.Top - mirrorBounds.Top) * scaleY),
                Math.Max(1, (int)Math.Round(overlay.Bounds.Width * scaleX)),
                Math.Max(1, (int)Math.Round(overlay.Bounds.Height * scaleY)));
            using ImageAttributes opacity = new();
            ColorMatrix matrix = new() { Matrix33 = (float)overlay.Opacity };
            opacity.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            graphics.DrawImage(
                panel,
                destination,
                0,
                0,
                panel.Width,
                panel.Height,
                GraphicsUnit.Pixel,
                opacity);
        }

        if (outputIndex < _analysisOverlays.Count && _analysisOverlays[outputIndex].Visible)
        {
            AnalysisOverlayForm overlay = _analysisOverlays[outputIndex];
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(
                (overlay.Bounds.Left - mirrorBounds.Left) * scaleX,
                (overlay.Bounds.Top - mirrorBounds.Top) * scaleY);
            graphics.ScaleTransform(
                bitmap.Width / (float)Math.Max(overlay.ClientSize.Width, 1),
                bitmap.Height / (float)Math.Max(overlay.ClientSize.Height, 1));
            overlay.DrawScreenshot(graphics);
            graphics.Restore(state);
        }
    }

    private string? ResolveSingleRegionScreenshotPath(int outputIndex)
    {
        DateTime now = DateTime.Now;
        if (NormalizeScreenshotMode(_settings.ScreenshotSaveMode) == AutomaticScreenshotMode)
        {
            string directory = GetScreenshotDirectory();
            Directory.CreateDirectory(directory);
            string fileName = $"HDRScreenMirror_{now:yyyyMMdd_HHmmss_fff}_{GetOutputDisplayToken(outputIndex)}_region.png";
            return GetUniqueFilePath(Path.Combine(directory, fileName));
        }

        string initialDirectory = GetScreenshotDirectory();
        if (!Directory.Exists(initialDirectory))
            initialDirectory = AppContext.BaseDirectory;

        using SaveFileDialog dialog = new()
        {
            AddExtension = true,
            DefaultExt = "png",
            Filter = "PNG image (*.png)|*.png",
            InitialDirectory = initialDirectory,
            FileName = $"HDRScreenMirror_{now:yyyyMMdd_HHmmss_fff}_region.png",
            OverwritePrompt = true
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private string[]? ResolveScreenshotPaths()
    {
        DateTime now = DateTime.Now;
        if (NormalizeScreenshotMode(_settings.ScreenshotSaveMode) == AutomaticScreenshotMode)
        {
            string directory = GetScreenshotDirectory();
            Directory.CreateDirectory(directory);
            return Enumerable.Range(0, _mirrorWindows.Count)
                .Select(index => GetUniqueFilePath(Path.Combine(directory, CreateScreenshotFileName(index, now))))
                .ToArray();
        }

        string initialDirectory = GetScreenshotDirectory();
        if (!Directory.Exists(initialDirectory))
            initialDirectory = AppContext.BaseDirectory;

        using SaveFileDialog dialog = new()
        {
            AddExtension = true,
            DefaultExt = "png",
            Filter = "PNG image (*.png)|*.png",
            InitialDirectory = initialDirectory,
            FileName = $"HDRScreenMirror_{now:yyyyMMdd_HHmmss_fff}.png",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return null;

        if (_mirrorWindows.Count == 1)
            return [dialog.FileName];

        string directoryName = Path.GetDirectoryName(dialog.FileName) ?? initialDirectory;
        string baseName = Path.GetFileNameWithoutExtension(dialog.FileName);
        return Enumerable.Range(0, _mirrorWindows.Count)
            .Select(index => GetUniqueFilePath(Path.Combine(
                directoryName,
                $"{baseName}_{GetOutputDisplayToken(index)}.png")))
            .ToArray();
    }

    private string CreateScreenshotFileName(int outputIndex, DateTime now) =>
        $"HDRScreenMirror_{now:yyyyMMdd_HHmmss_fff}_{GetOutputDisplayToken(outputIndex)}.png";

    private string GetOutputDisplayToken(int outputIndex)
    {
        Screen screen = Screen.FromRectangle(_mirrorWindows[outputIndex].Bounds);
        string token = new(screen.DeviceName.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(token) ? $"OUTPUT{outputIndex + 1}" : token;
    }

    private static string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path))
            return path;

        string? directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int suffix = 2; ; suffix++)
        {
            string candidate = Path.Combine(directory ?? string.Empty, $"{fileName} ({suffix}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private void OnSessionTelemetryChanged(MirrorTelemetry telemetry) => PostToUi(() =>
    {
        _footerTelemetry.SetTelemetry(telemetry);
        foreach (StatusOverlayForm overlay in _statusOverlays)
            overlay.UpdateTelemetry(telemetry);
        foreach (AnalysisOverlayForm overlay in _analysisOverlays)
            overlay.UpdateMirrorTelemetry(telemetry);
        EnsureSingleDisplayAnalysisOverlaysOnTop();
    });

    private void EnsureSingleDisplayAnalysisOverlaysOnTop()
    {
        if (!_singleDisplayAnalysis)
            return;

        foreach (StatusOverlayForm overlay in _statusOverlays)
        {
            if (overlay.Visible && overlay.IsHandleCreated)
                NativeMethods.EnsureOverlayAboveOccludingWindows(overlay.Handle);
        }

        foreach (CieOverlayForm overlay in _cieOverlays)
        {
            if (overlay.Visible && overlay.IsHandleCreated)
                NativeMethods.EnsureOverlayAboveOccludingWindows(overlay.Handle);
        }

        foreach (AnalysisOverlayForm overlay in _analysisOverlays)
        {
            if (overlay.Visible && overlay.IsHandleCreated)
                NativeMethods.EnsureOverlayAboveOccludingWindows(overlay.Handle);
        }
    }

    private void OnSessionLuminanceChanged(LuminanceTelemetry telemetry) => PostToUi(() =>
    {
        foreach (StatusOverlayForm overlay in _statusOverlays)
            overlay.UpdateLuminance(telemetry);
        foreach (AnalysisOverlayForm overlay in _analysisOverlays)
            overlay.UpdateLuminance(telemetry);
    });

    private void OnSessionGamutChanged(GamutTelemetry telemetry) => PostToUi(() =>
    {
        foreach (CieOverlayForm overlay in _cieOverlays)
            overlay.UpdateGamut(telemetry);
    });

    private void OnSessionFailed(Exception exception) => PostToUi(() =>
    {
        if (_restartMirrorAfterDisplayChange || _displayChangeTimer.Enabled)
        {
            StopMirror();
            return;
        }

        bool analysisOnly = _singleDisplayAnalysis;
        StopMirror();
        MessageBox.Show(
            this,
            exception.ToString(),
            Localization.T(analysisOnly ? "RuntimeAnalysisFailed" : "RuntimeFailed"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    });

    private void OnSessionStopped() => PostToUi(() =>
    {
        if (_session is not null)
            StopMirror();
    });

    private void PostToUi(Action action)
    {
        if (IsDisposed || Disposing)
            return;
        if (InvokeRequired)
            BeginInvoke(action);
        else
            action();
    }

    private void PositionOnCaptureDisplay(DisplayTarget capture)
    {
        Screen screen = FindScreenForDisplay(capture);
        Rectangle workArea = screen.WorkingArea;
        WindowState = FormWindowState.Normal;
        Location = new Point(
            workArea.Left + Math.Max(16, (workArea.Width - Width) / 2),
            workArea.Top + Math.Max(16, (workArea.Height - Height) / 2));
    }

    private static Screen FindScreenForDisplay(DisplayTarget display) => Screen.AllScreens
        .OrderByDescending(x => IntersectionArea(x.Bounds, display.Bounds))
        .First();

    private void RecallControlWindow()
    {
        DisplayTarget? capture = _captureSelect.SelectedItem as DisplayTarget ?? _displays.FirstOrDefault();
        if (capture is not null)
            PositionOnCaptureDisplay(capture);

        Show();
        _trayIcon.Visible = false;
        TopMost = _session is not null;
        BringToFront();
        Activate();
    }

    internal void RecallFromSecondInstance()
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
            return;

        try
        {
            BeginInvoke(RecallControlWindow);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static int IntersectionArea(Rectangle left, Rectangle right)
    {
        Rectangle intersection = Rectangle.Intersect(left, right);
        return Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
    }

    private void ShowHotkeysDialog()
    {
        using HotkeysForm dialog = new();
        dialog.TopMost = TopMost;
        if (_singleDisplayAnalysis)
        {
            dialog.Shown += (_, _) => NativeMethods.SetWindowDisplayAffinity(
                dialog.Handle,
                NativeMethods.WdaExcludeFromCapture);
        }
        dialog.ShowDialog(this);
    }

    private void ShowAbout()
    {
        using AboutForm about = new();
        about.TopMost = TopMost;
        if (_singleDisplayAnalysis)
        {
            about.Shown += (_, _) => NativeMethods.SetWindowDisplayAffinity(
                about.Handle,
                NativeMethods.WdaExcludeFromCapture);
        }
        about.ShowDialog(this);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!_exitRequested &&
            eventArgs.CloseReason == CloseReason.UserClosing &&
            _minimizeToTray.Checked)
        {
            eventArgs.Cancel = true;
            HideToTray();
            return;
        }

        _exitRequested = true;
        SystemEvents.UserPreferenceChanged -= OnSystemUserPreferenceChanged;
        HextaHoverTip.Hide();
        UnregisterConfiguredHotKeys();
        _displayChangeTimer.Stop();
        _displayChangeTimer.Dispose();
        _noticeTimer.Stop();
        _noticeTimer.Dispose();
        StopMirror();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIconImage.Dispose();
        _brandLogo.Dispose();
        _trayMenu.Dispose();
    }

    private void SaveCloseBehavior()
    {
        _settings.MinimizeToTrayOnClose = _minimizeToTray.Checked;
        _settings.Save();
    }

    private void HideToTray()
    {
        Hide();
        UpdateTrayState();
        _trayIcon.Visible = true;
    }

    private void UpdateTrayState()
    {
        bool running = _session is not null;
        bool analysisMode = running ? _singleDisplayAnalysis : IsAnalysisModeSelected();
        _trayToggleMirrorItem.Text = Localization.T(running
            ? analysisMode ? "TrayStopAnalysis" : "TrayStop"
            : analysisMode ? "TrayStartAnalysis" : "TrayStart");
        _trayIcon.Text = running
            ? Localization.T(analysisMode ? "TrayAnalyzing" : "TrayRunning")
            : "HDRScreenMirror";
    }

    private void ToggleMirror()
    {
        if (_session is null)
            StartMirror();
        else
            StopMirror();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmDisplayChange)
        {
            base.WndProc(ref message);
            ScheduleDisplayChange();
            return;
        }

        if (message.Msg == NativeMethods.WmHotKey)
        {
            int hotKeyId = message.WParam.ToInt32();
            if (hotKeyId == ToggleStatusOverlayHotKeyId)
            {
                _showStatusOverlay.Checked = !_showStatusOverlay.Checked;
                return;
            }
            if (hotKeyId == ToggleMirrorHotKeyId)
            {
                ToggleMirror();
                return;
            }
            if (hotKeyId == RecallWindowHotKeyId)
            {
                RecallControlWindow();
                return;
            }
            if (hotKeyId == ScreenshotHotKeyId)
            {
                TakeOutputScreenshots();
                return;
            }
            if (hotKeyId == RegionScreenshotHotKeyId)
            {
                TakeRegionScreenshot();
                return;
            }
            if (hotKeyId == ToggleFalseColorHotKeyId)
            {
                if (_session is not null ? _singleDisplayAnalysis : IsAnalysisModeSelected())
                {
                    ShowFooterNotice(Localization.T("FalseColorNeedsOutput"));
                    return;
                }
                _falseColorSegmented.SelectedIndex = (_falseColorSegmented.SelectedIndex + 1) % 3;
                return;
            }
        }

        base.WndProc(ref message);
    }

    private void ScheduleDisplayChange()
    {
        if (!_displayChangeTimer.Enabled)
        {
            _restartMirrorAfterDisplayChange = _session is not null;
            _displayChangeAnalysisOnly = _restartMirrorAfterDisplayChange && _singleDisplayAnalysis
                ? true
                : null;
            _displayChangeCaptureId = _restartMirrorAfterDisplayChange
                ? (_captureSelect.SelectedItem as DisplayTarget)?.StableId
                : null;
            _displayChangePresentId = _restartMirrorAfterDisplayChange
                ? _singleDisplayAnalysis
                    ? _settings.OutputDisplayId
                    : (_presentSelect.SelectedItem as DisplayTarget)?.StableId
                : null;
        }

        _displayChangeTimer.Stop();
        _displayChangeTimer.Start();
    }

    private void ApplyDisplayChange()
    {
        _displayChangeTimer.Stop();

        bool restartMirror = _restartMirrorAfterDisplayChange;
        bool? analysisOnly = _displayChangeAnalysisOnly;
        string? captureDisplayId = _displayChangeCaptureId;
        string? presentDisplayId = _displayChangePresentId;
        _restartMirrorAfterDisplayChange = false;
        _displayChangeAnalysisOnly = null;
        _displayChangeCaptureId = null;
        _displayChangePresentId = null;

        if (restartMirror)
            StopMirror(false);

        if (!RefreshDisplays(captureDisplayId, presentDisplayId))
        {
            if (restartMirror && analysisOnly == true && !_exitRequested)
                RecallControlWindow();
            return;
        }

        if (restartMirror)
            StartMirror(false, false, analysisOnly);
    }

    private void RegisterConfiguredHotKeys()
    {
        if (!IsHandleCreated)
            return;

        UnregisterConfiguredHotKeys();
        if (!_enableHotKeys.Checked)
            return;

        uint ctrl = NativeMethods.ModControl | NativeMethods.ModNoRepeat;
        uint ctrlShift = NativeMethods.ModControl | NativeMethods.ModShift | NativeMethods.ModNoRepeat;
        List<string> failedHotKeys = [];
        RegisterHotKeyOrRecord(ToggleMirrorHotKeyId, ctrl, NativeMethods.VirtualKeyF8, "Ctrl+F8", failedHotKeys);
        RegisterHotKeyOrRecord(ToggleFalseColorHotKeyId, ctrl, NativeMethods.VirtualKeyF9, "Ctrl+F9", failedHotKeys);
        RegisterHotKeyOrRecord(ScreenshotHotKeyId, ctrl, NativeMethods.VirtualKeyF10, "Ctrl+F10", failedHotKeys);
        RegisterHotKeyOrRecord(RegionScreenshotHotKeyId, ctrlShift, NativeMethods.VirtualKeyF10, "Ctrl+Shift+F10", failedHotKeys);
        RegisterHotKeyOrRecord(ToggleStatusOverlayHotKeyId, ctrl, NativeMethods.VirtualKeyF11, "Ctrl+F11", failedHotKeys);
        RegisterHotKeyOrRecord(RecallWindowHotKeyId, ctrl, NativeMethods.VirtualKeyF12, "Ctrl+F12", failedHotKeys);

        if (failedHotKeys.Count > 0)
        {
            string separator = Localization.Current == UiLanguage.Chinese ? "、" : ", ";
            ShowFooterNotice(Localization.F("HotkeyFailed", string.Join(separator, failedHotKeys)), isError: true, autoClear: false);
        }
    }

    private void RegisterHotKeyOrRecord(
        int id,
        uint modifiers,
        uint virtualKey,
        string displayName,
        ICollection<string> failedHotKeys)
    {
        if (!NativeMethods.RegisterHotKey(Handle, id, modifiers, virtualKey))
            failedHotKeys.Add(displayName);
    }

    private void UnregisterConfiguredHotKeys()
    {
        if (!IsHandleCreated)
            return;

        NativeMethods.UnregisterHotKey(Handle, ToggleMirrorHotKeyId);
        NativeMethods.UnregisterHotKey(Handle, ToggleStatusOverlayHotKeyId);
        NativeMethods.UnregisterHotKey(Handle, RecallWindowHotKeyId);
        NativeMethods.UnregisterHotKey(Handle, ScreenshotHotKeyId);
        NativeMethods.UnregisterHotKey(Handle, RegionScreenshotHotKeyId);
        NativeMethods.UnregisterHotKey(Handle, ToggleFalseColorHotKeyId);
    }

    private sealed record ThemeChoice(string Code, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record LanguageChoice(string Code, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record AblProfileChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed class SingleLineRightValue : Control
    {
        public SingleLineRightValue()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Font = HextaTheme.CreateSmallFont(FontStyle.Bold);
            TabStop = false;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                HextaTheme.TextPrimary,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    private sealed class FooterTelemetryBar : Control
    {
        private MirrorTelemetry? _telemetry;
        private string? _notice;
        private bool _noticeIsError;

        public FooterTelemetryBar()
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
            TabStop = false;
        }

        public void SetTelemetry(MirrorTelemetry telemetry)
        {
            _telemetry = telemetry;
            Invalidate();
        }

        public void ClearTelemetry()
        {
            _telemetry = null;
            Invalidate();
        }

        public void SetNotice(string text, bool isError)
        {
            _notice = text;
            _noticeIsError = isError;
            Invalidate();
        }

        public void ClearNotice()
        {
            if (_notice is null)
                return;
            _notice = null;
            _noticeIsError = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            if (!string.IsNullOrWhiteSpace(_notice))
            {
                TextRenderer.DrawText(
                    graphics,
                    _notice,
                    Font,
                    ClientRectangle,
                    _noticeIsError ? HextaTheme.Danger : HextaTheme.TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                return;
            }

            if (_telemetry is null)
                return;

            float scale = HextaTheme.ScaleFactor(this);
            float x = 0f;
            float chipH = 26f * scale;
            float chipY = (Height - chipH) * 0.5f;

            bool paused = _telemetry.State.Contains("暂停", StringComparison.OrdinalIgnoreCase) ||
                          _telemetry.State.Contains("paused", StringComparison.OrdinalIgnoreCase);
            float dotSize = 7f * scale;
            using (SolidBrush dotBrush = new(paused ? HextaTheme.Warning : HextaTheme.Success))
                graphics.FillEllipse(dotBrush, x, (Height - dotSize) * 0.5f, dotSize, dotSize);
            x += dotSize + 10f * scale;

            string[] chips =
            [
                _telemetry.InputKind,
                FriendlyFormat(_telemetry.InputFormat),
                $"{_telemetry.Width}×{_telemetry.Height}",
                $"{_telemetry.FramesPerSecond:F1} fps"
            ];

            using SolidBrush chipBg = new(HextaTheme.MutedSurface);
            using Pen chipBorder = new(HextaTheme.Border, HextaTheme.HairlineWidth);
            for (int i = 0; i < chips.Length; i++)
            {
                string text = chips[i];
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                Size sz = TextRenderer.MeasureText(graphics, text, Font, Size.Empty, TextFormatFlags.NoPadding);
                float chipW = sz.Width + 16f * scale;
                if (x + chipW > Width)
                    break;

                RectangleF rect = new(x, chipY, chipW, chipH);
                using GraphicsPath path = HextaTheme.CreateRoundedPath(rect, 6f * scale);
                graphics.FillPath(chipBg, path);
                graphics.DrawPath(chipBorder, path);

                TextRenderer.DrawText(
                    graphics,
                    text,
                    Font,
                    Rectangle.Round(rect),
                    i == 0 || i == chips.Length - 1 ? HextaTheme.TextPrimary : HextaTheme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

                x += chipW + 6f * scale;
            }
        }

        private static string FriendlyFormat(string format) => format switch
        {
            "R16G16B16A16_Float" => "FP16 scRGB",
            "R10G10B10A2_UNorm" => "RGB10 PQ",
            "B8G8R8A8_UNorm" => "BGRA8 SDR",
            _ => format
        };
    }

    private sealed class OverlayMiniPreview : Control
    {
        private OverlayAppearance _appearance = OverlayAppearance.Default;

        public OverlayMiniPreview()
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
            TabStop = false;
        }

        public void SetAppearance(OverlayAppearance appearance)
        {
            _appearance = appearance;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float scale = HextaTheme.ScaleFactor(this);
            RectangleF bounds = new(0.5f, 0.5f, Width - 1f, Height - 1f);
            using GraphicsPath path = HextaTheme.CreateRoundedPath(bounds, 8f * scale);

            // Subtle neutral gradient backing so translucent/glass opacity is clearly visible.
            Color backA = HextaTheme.IsDark ? Color.FromArgb(36, 38, 46) : Color.FromArgb(228, 232, 240);
            Color backB = HextaTheme.IsDark ? Color.FromArgb(52, 56, 66) : Color.FromArgb(244, 244, 246);
            using LinearGradientBrush backing = new(bounds, backA, backB, LinearGradientMode.Horizontal);
            graphics.FillPath(backing, path);

            int alpha = _appearance.GlassEnabled
                ? (int)Math.Clamp(Math.Round(_appearance.Opacity * 220.0), 60, 245)
                : (int)Math.Round(_appearance.Opacity * 255.0);
            using SolidBrush fill = new(Color.FromArgb(alpha, _appearance.BackgroundColor));
            graphics.FillPath(fill, path);

            if (_appearance.GlassEnabled)
            {
                RectangleF sheen = new(bounds.X, bounds.Y, bounds.Width, bounds.Height * 0.48f);
                Color sheenTop = _appearance.IsLightSurface
                    ? Color.FromArgb(70, 255, 255, 255)
                    : Color.FromArgb(32, 255, 255, 255);
                using GraphicsPath clipPath = HextaTheme.CreateRoundedPath(bounds, 8f * scale);
                GraphicsState state = graphics.Save();
                graphics.SetClip(clipPath);
                using LinearGradientBrush sheenBrush = new(sheen, sheenTop, Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical);
                graphics.FillRectangle(sheenBrush, sheen);
                graphics.Restore(state);
            }

            using Pen border = new(_appearance.BorderColor, HextaTheme.HairlineWidth);
            graphics.DrawPath(border, path);

            int padX = (int)Math.Round(14f * scale);
            int halfW = Math.Max(1, (Width - padX * 2) / 2);
            Rectangle leftRect = new(padX, 0, halfW, Height);
            Rectangle rightRect = new(padX + halfW, 0, Width - padX * 2 - halfW, Height);

            TextRenderer.DrawText(
                graphics,
                $"{Localization.T("OverlayPreviewSample")}   203.0 nits",
                Font,
                leftRect,
                _appearance.PrimaryTextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(
                graphics,
                $"{(int)Math.Round(_appearance.Scale * 100)}%   {(int)Math.Round(_appearance.Opacity * 100)}%",
                Font,
                rightRect,
                _appearance.EffectiveAccentColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}
