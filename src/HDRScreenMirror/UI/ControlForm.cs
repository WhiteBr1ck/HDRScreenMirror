using System.ComponentModel;
using System.Drawing.Imaging;
using HDRScreenMirror.DirectX;
using HDRScreenMirror.Interop;

namespace HDRScreenMirror.UI;

internal sealed class ControlForm : Form
{
    private const decimal DefaultPaperWhiteNits = 203;
    private const int RecallWindowHotKeyId = 0x4848;
    private const int ToggleMirrorHotKeyId = 0x484D;
    private const int ToggleStatusOverlayHotKeyId = 0x4851;
    private const int ToggleFalseColorHotKeyId = 0x4846;
    private const int ScreenshotHotKeyId = 0x4853;
    private const string AutomaticScreenshotMode = "automatic";
    private const string PromptScreenshotMode = "prompt";
    private const string FollowOutputFrameRateMode = "output";
    private const string FixedFrameRateMode = "fixed";
    private const string UnlimitedFrameRateMode = "unlimited";
    private const string MirrorOperationMode = "mirror";
    private const string AnalysisOperationMode = "analysis";

    private readonly ComboBox _captureCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _presentCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _paperWhite = new()
    {
        Minimum = 80,
        Maximum = 500,
        Value = DefaultPaperWhiteNits,
        DecimalPlaces = 0,
        Width = 110,
        Margin = new Padding(3, 8, 3, 0)
    };
    private readonly Button _resetPaperWhiteButton = CreateButton("ResetDefault", 132);
    private readonly CheckBox _allOutputs = CreateOptionCheckBox("AllOutputs", false);
    private readonly ComboBox _frameRateModeCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 190,
        Margin = new Padding(3, 7, 6, 3)
    };
    private readonly NumericUpDown _frameRateLimit = new()
    {
        Minimum = 24,
        Maximum = 500,
        Value = 60,
        Width = 72,
        Margin = new Padding(3, 8, 2, 3)
    };
    private readonly Label _frameRateUnitLabel = new()
    {
        Text = Localization.T("FrameRateUnit"),
        Tag = "FrameRateUnit",
        AutoSize = true,
        Padding = new Padding(0, 14, 8, 0),
        ForeColor = Color.FromArgb(84, 91, 104)
    };
    private readonly CheckBox _showStatusOverlay = CreateOptionCheckBox("StatusOverlay", true);
    private readonly CheckBox _renderCursor = CreateOptionCheckBox("RenderCursor", true);
    private readonly CheckBox _showPointerLuminance = CreateOptionCheckBox("ShowPointerLuminance", true);
    private readonly CheckBox _falseColor = CreateOptionCheckBox("FalseColor", false);
    private readonly CheckBox _showLuminanceMarkers = CreateOptionCheckBox("LuminanceMarkers", false);
    private readonly CheckBox _showAblEstimate;
    private readonly ComboBox _ablProfileCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 230,
        Margin = new Padding(3, 6, 6, 3)
    };
    private readonly Button _manageAblProfilesButton = CreateButton("ManageAblProfiles", 154);
    private readonly CheckBox _showCieAnalysis = CreateOptionCheckBox("CieAnalysis", false);
    private readonly CheckBox _mouseThrough = CreateOptionCheckBox("MouseThrough", false);
    private readonly CheckBox _enableHotKeys = CreateOptionCheckBox("EnableHotkeys", true);
    private readonly CheckBox _moveOutputWindows = CreateOptionCheckBox("MoveOutputWindows", false);
    private readonly CheckBox _minimizeToTray;
    private readonly ComboBox _screenshotModeCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 210,
        Margin = new Padding(3, 7, 6, 3)
    };
    private readonly TextBox _screenshotDirectoryTextBox = new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        Margin = new Padding(3, 8, 6, 5)
    };
    private readonly Button _chooseScreenshotDirectoryButton = CreateButton("ChooseFolder", 136);
    private readonly Button _refreshButton = CreateButton("RefreshDisplays", 176);
    private readonly Button _startButton = CreateButton("StartMirror", 196);
    private readonly Button _stopButton = CreateButton("Stop", 92);
    private readonly Button _aboutButton = CreateButton("About", 88);
    private readonly ComboBox _modeCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 228,
        Margin = new Padding(4, 8, 4, 3)
    };
    private readonly ComboBox _languageCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 146,
        Margin = new Padding(4, 8, 4, 3)
    };
    private readonly Label _shortcutLabel = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        Font = new Font("Consolas", 9.5f, FontStyle.Regular),
        ForeColor = Color.FromArgb(52, 58, 70),
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label _statusLabel = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Text = Localization.T("StatusEnumerating"),
        TextAlign = ContentAlignment.TopLeft,
        Padding = new Padding(2, 5, 2, 2)
    };

    private IReadOnlyList<DisplayTarget> _displays = [];
    private MirrorSession? _session;
    private readonly List<MirrorForm> _mirrorWindows = [];
    private readonly List<StatusOverlayForm> _statusOverlays = [];
    private readonly List<AnalysisOverlayForm> _analysisOverlays = [];
    private readonly System.Windows.Forms.Timer _displayChangeTimer = new() { Interval = 750 };
    private readonly AppSettings _settings;
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _trayIconImage;
    private readonly ContextMenuStrip _trayMenu;
    private readonly ToolStripMenuItem _trayShowItem;
    private readonly ToolStripMenuItem _trayToggleMirrorItem;
    private readonly ToolStripMenuItem _trayExitItem;
    private bool _exitRequested;
    private bool _hasEnumeratedDisplays;
    private bool _updatingLanguageSelection;
    private bool _updatingScreenshotSelection;
    private bool _updatingFrameRateSelection;
    private bool _updatingModeSelection;
    private bool _updatingAblProfileSelection;
    private bool _updatingDisplaySelection;
    private bool _takingScreenshot;
    private bool _singleDisplayAnalysis;
    private bool _controlWindowCaptureExcluded;
    private bool _restartMirrorAfterDisplayChange;
    private bool? _displayChangeAnalysisOnly;
    private string? _displayChangeCaptureId;
    private string? _displayChangePresentId;

    public ControlForm(AppSettings settings)
    {
        Text = "HDRScreenMirror";
        Width = 1280;
        Height = 920;
        MinimumSize = new Size(1120, 728);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(248, 249, 251);

        _settings = settings;
        _settings.NormalizeAblProfiles();
        _minimizeToTray = CreateOptionCheckBox("MinimizeToTray", _settings.MinimizeToTrayOnClose);
        _showAblEstimate = CreateOptionCheckBox("ShowAblEstimate", _settings.AblEstimationEnabled);
        _trayShowItem = new ToolStripMenuItem(Localization.T("TrayShow"));
        _trayToggleMirrorItem = new ToolStripMenuItem(Localization.T("TrayStart"));
        _trayExitItem = new ToolStripMenuItem(Localization.T("TrayExit"));
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add(_trayShowItem);
        _trayMenu.Items.Add(_trayToggleMirrorItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(_trayExitItem);
        _trayIconImage = BrandAssets.LoadApplicationIcon();
        Icon = _trayIconImage;
        _trayIcon = new NotifyIcon
        {
            Icon = _trayIconImage,
            Text = "HDRScreenMirror",
            ContextMenuStrip = _trayMenu,
            Visible = false
        };

        _stopButton.Enabled = false;
        InitializeLanguageSelector();
        InitializeScreenshotSelector();
        InitializeFrameRateSelector();
        InitializeOperationModeSelector();
        InitializeAblProfiles();
        Controls.Add(BuildLayout());
        ApplyLanguage();

        _refreshButton.Click += (_, _) => RefreshDisplays();
        _captureCombo.SelectedIndexChanged += (_, _) => SaveDisplaySelection(capture: true);
        _presentCombo.SelectedIndexChanged += (_, _) => SaveDisplaySelection(capture: false);
        _resetPaperWhiteButton.Click += (_, _) => _paperWhite.Value = DefaultPaperWhiteNits;
        _startButton.Click += (_, _) => StartMirror();
        _stopButton.Click += (_, _) => StopMirror();
        _aboutButton.Click += (_, _) => ShowAbout();
        _allOutputs.CheckedChanged += (_, _) => UpdatePresentSelectorState();
        _showStatusOverlay.CheckedChanged += (_, _) => UpdateStatusOverlayVisibility();
        _showPointerLuminance.CheckedChanged += (_, _) => UpdatePointerLuminanceVisibility();
        _falseColor.CheckedChanged += (_, _) => UpdateFalseColorMode();
        _showLuminanceMarkers.CheckedChanged += (_, _) => UpdateLuminanceMarkerVisibility();
        _showAblEstimate.CheckedChanged += (_, _) => ChangeAblEstimation();
        _ablProfileCombo.SelectedIndexChanged += (_, _) => ChangeAblProfile();
        _manageAblProfilesButton.Click += (_, _) => ManageAblProfiles();
        _showCieAnalysis.CheckedChanged += (_, _) => UpdateCieAnalysisVisibility();
        _enableHotKeys.CheckedChanged += (_, _) => RegisterConfiguredHotKeys();
        _minimizeToTray.CheckedChanged += (_, _) => SaveCloseBehavior();
        _screenshotModeCombo.SelectedIndexChanged += (_, _) => ChangeScreenshotMode();
        _frameRateModeCombo.SelectedIndexChanged += (_, _) => ChangeFrameRateMode();
        _modeCombo.SelectedIndexChanged += (_, _) => ChangeOperationMode();
        _frameRateLimit.ValueChanged += (_, _) => ChangeFrameRateLimit();
        _chooseScreenshotDirectoryButton.Click += (_, _) => ChooseScreenshotDirectory();
        _languageCombo.SelectedIndexChanged += (_, _) => ChangeLanguage();
        _displayChangeTimer.Tick += (_, _) => ApplyDisplayChange();
        _trayShowItem.Click += (_, _) => RecallControlWindow();
        _trayToggleMirrorItem.Click += (_, _) => ToggleMirror();
        _trayExitItem.Click += (_, _) => ExitApplication();
        _trayIcon.DoubleClick += (_, _) => RecallControlWindow();
        FormClosing += OnFormClosing;
        Shown += (_, _) => RefreshDisplays();
    }

    private Control BuildLayout()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 18, 20, 18),
            ColumnCount = 2,
            RowCount = 12,
            BackColor = Color.FromArgb(248, 249, 251)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateLabel("CaptureDisplay"), 0, 0);
        layout.Controls.Add(BuildCaptureSelector(), 1, 0);
        layout.Controls.Add(CreateLabel("OutputDisplay"), 0, 1);
        layout.Controls.Add(BuildOutputSelector(), 1, 1);
        layout.Controls.Add(CreateLabel("PaperWhite"), 0, 2);
        layout.Controls.Add(BuildPaperWhiteRow(), 1, 2);
        layout.Controls.Add(CreateLabel("Present"), 0, 3);
        layout.Controls.Add(BuildPresentOptions(), 1, 3);
        layout.Controls.Add(CreateLabel("LuminanceAnalysis"), 0, 4);
        layout.Controls.Add(BuildLuminanceOptions(), 1, 4);
        layout.Controls.Add(CreateLabel("ColorAnalysis"), 0, 5);
        layout.Controls.Add(BuildColorAnalysisOptions(), 1, 5);
        layout.Controls.Add(CreateLabel("InteractionSafety"), 0, 6);
        layout.Controls.Add(BuildInteractionOptions(), 1, 6);
        layout.Controls.Add(CreateLabel("WindowManagement"), 0, 7);
        layout.Controls.Add(BuildWindowManagementOptions(), 1, 7);
        layout.Controls.Add(CreateLabel("ScreenshotSettings"), 0, 8);
        layout.Controls.Add(BuildScreenshotSettings(), 1, 8);

        GroupBox shortcutGroup = new()
        {
            Text = Localization.T("GlobalHotkeys"),
            Tag = "GlobalHotkeys",
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 8),
            BackColor = Color.White
        };
        shortcutGroup.Controls.Add(_shortcutLabel);
        layout.Controls.Add(shortcutGroup, 0, 9);
        layout.SetColumnSpan(shortcutGroup, 2);

        Control actionRow = BuildActionRow();
        layout.Controls.Add(actionRow, 0, 10);
        layout.SetColumnSpan(actionRow, 2);

        GroupBox statusGroup = new()
        {
            Text = Localization.T("CurrentStatus"),
            Tag = "CurrentStatus",
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 10, 12, 10),
            BackColor = Color.White
        };
        statusGroup.Controls.Add(_statusLabel);
        layout.Controls.Add(statusGroup, 0, 11);
        layout.SetColumnSpan(statusGroup, 2);
        return layout;
    }

    private Control BuildOutputSelector()
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 0, 8, 0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        row.Controls.Add(_presentCombo, 0, 0);
        row.Controls.Add(_allOutputs, 1, 0);
        return row;
    }

    private Control BuildCaptureSelector()
    {
        TableLayoutPanel row = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184));
        row.Controls.Add(_captureCombo, 0, 0);
        row.Controls.Add(_refreshButton, 1, 0);
        return row;
    }

    private Control BuildPaperWhiteRow()
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(_paperWhite, 0, 0);
        row.Controls.Add(_resetPaperWhiteButton, 1, 0);
        row.Controls.Add(new Label
        {
            Text = Localization.T("PaperWhiteHelp"),
            Tag = "PaperWhiteHelp",
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 4, 0),
            ForeColor = Color.FromArgb(84, 91, 104)
        }, 2, 0);
        return row;
    }

    private Control BuildPresentOptions()
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        FlowLayoutPanel frameRateGroup = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        frameRateGroup.Controls.Add(new Label
        {
            Text = Localization.T("FrameRateMode"),
            Tag = "FrameRateMode",
            AutoSize = true,
            Padding = new Padding(0, 14, 2, 0),
            ForeColor = Color.FromArgb(84, 91, 104)
        });
        frameRateGroup.Controls.Add(_frameRateModeCombo);
        frameRateGroup.Controls.Add(_frameRateLimit);
        frameRateGroup.Controls.Add(_frameRateUnitLabel);

        row.Controls.Add(frameRateGroup, 0, 0);
        row.Controls.Add(_showStatusOverlay, 1, 0);
        row.Controls.Add(_renderCursor, 2, 0);
        return row;
    }

    private Control BuildInteractionOptions()
    {
        FlowLayoutPanel row = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        row.Controls.Add(_mouseThrough);
        row.Controls.Add(_enableHotKeys);
        row.Controls.Add(_minimizeToTray);
        return row;
    }

    private Control BuildLuminanceOptions()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        FlowLayoutPanel analysisRow = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        analysisRow.Controls.Add(_showPointerLuminance);
        analysisRow.Controls.Add(_falseColor);
        analysisRow.Controls.Add(_showLuminanceMarkers);

        FlowLayoutPanel ablRow = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        ablRow.Controls.Add(_showAblEstimate);
        ablRow.Controls.Add(new Label
        {
            Text = Localization.T("AblProfile"),
            Tag = "AblProfile",
            AutoSize = true,
            Padding = new Padding(8, 12, 2, 0),
            ForeColor = Color.FromArgb(84, 91, 104)
        });
        ablRow.Controls.Add(_ablProfileCombo);
        ablRow.Controls.Add(_manageAblProfilesButton);

        layout.Controls.Add(analysisRow, 0, 0);
        layout.Controls.Add(ablRow, 0, 1);
        return layout;
    }

    private Control BuildColorAnalysisOptions()
    {
        FlowLayoutPanel row = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        row.Controls.Add(_showCieAnalysis);
        return row;
    }

    private Control BuildWindowManagementOptions()
    {
        FlowLayoutPanel row = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        row.Controls.Add(_moveOutputWindows);
        return row;
    }

    private Control BuildScreenshotSettings()
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(0, 0, 8, 0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        row.Controls.Add(_screenshotModeCombo, 0, 0);
        row.Controls.Add(_screenshotDirectoryTextBox, 1, 0);
        row.Controls.Add(_chooseScreenshotDirectoryButton, 2, 0);
        return row;
    }

    private Control BuildActionRow()
    {
        TableLayoutPanel row = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 404));

        FlowLayoutPanel primaryActions = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        primaryActions.Controls.Add(_startButton);
        primaryActions.Controls.Add(_stopButton);

        FlowLayoutPanel modeActions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(2, 0, 4, 0)
        };
        modeActions.Controls.Add(new Label
        {
            Text = Localization.T("OperationMode"),
            Tag = "OperationMode",
            AutoSize = false,
            Size = new Size(70, 42),
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(0, 3, 2, 3)
        });
        modeActions.Controls.Add(_modeCombo);

        FlowLayoutPanel secondaryActions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(2, 0, 4, 0)
        };
        secondaryActions.Controls.Add(new Label
        {
            Text = Localization.T("Language"),
            Tag = "Language",
            AutoSize = false,
            Size = new Size(100, 42),
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(0, 3, 2, 3)
        });
        secondaryActions.Controls.Add(_languageCombo);
        secondaryActions.Controls.Add(_aboutButton);
        row.Controls.Add(primaryActions, 0, 0);
        row.Controls.Add(modeActions, 1, 0);
        row.Controls.Add(secondaryActions, 2, 0);
        return row;
    }

    private static Label CreateLabel(string key) => new()
    {
        Text = Localization.T(key),
        Tag = key,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoSize = false,
        UseMnemonic = false,
        Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Regular),
        ForeColor = Color.FromArgb(42, 47, 57)
    };

    private static CheckBox CreateOptionCheckBox(string key, bool isChecked) => new()
    {
        Text = Localization.T(key),
        Tag = key,
        Checked = isChecked,
        AutoSize = true,
        MinimumSize = new Size(0, 40),
        Padding = new Padding(0, 9, 12, 9),
        Margin = new Padding(3, 0, 3, 0)
    };

    private static Button CreateButton(string key, int width) => new()
    {
        Text = Localization.T(key),
        Tag = key,
        AutoSize = false,
        Size = new Size(width, 42),
        MinimumSize = new Size(width, 42),
        Margin = new Padding(4, 3, 4, 3)
    };

    private void InitializeLanguageSelector()
    {
        _languageCombo.Items.AddRange(
        [
            new LanguageChoice(Localization.Automatic, "自动 / Auto"),
            new LanguageChoice(Localization.SimplifiedChinese, "简体中文"),
            new LanguageChoice(Localization.English, "English")
        ]);

        _updatingLanguageSelection = true;
        _languageCombo.SelectedItem = _languageCombo.Items
            .Cast<LanguageChoice>()
            .First(x => x.Code == NormalizeLanguagePreference(_settings.Language));
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

    private void RefreshOperationModeChoices()
    {
        string selectedMode = NormalizeOperationMode(_settings.OperationMode);
        _updatingModeSelection = true;
        _modeCombo.Items.Clear();
        _modeCombo.Items.AddRange(
        [
            new OperationModeChoice(MirrorOperationMode, Localization.T("OperationModeMirror")),
            new OperationModeChoice(AnalysisOperationMode, Localization.T("OperationModeAnalysis"))
        ]);
        _modeCombo.SelectedItem = _modeCombo.Items
            .Cast<OperationModeChoice>()
            .First(choice => choice.Code == selectedMode);
        _updatingModeSelection = false;
    }

    private void ChangeOperationMode()
    {
        if (_updatingModeSelection ||
            _modeCombo.SelectedItem is not OperationModeChoice choice)
        {
            return;
        }

        _settings.OperationMode = choice.Code;
        _settings.Save();
        SetRunningState(_session is not null);
        RefreshFrameRateModeChoices();
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
        AblProfile[] validProfiles = _settings.AblProfiles
            .Where(profile => profile.IsValid)
            .ToArray();

        _updatingAblProfileSelection = true;
        _ablProfileCombo.Items.Clear();
        foreach (AblProfile profile in validProfiles)
            _ablProfileCombo.Items.Add(new AblProfileChoice(profile.Id, profile.Name));

        _ablProfileCombo.SelectedItem = _ablProfileCombo.Items
            .Cast<AblProfileChoice>()
            .FirstOrDefault(choice =>
                string.Equals(choice.Id, activeProfileId, StringComparison.Ordinal));
        if (_ablProfileCombo.SelectedIndex < 0 && _ablProfileCombo.Items.Count > 0)
        {
            _ablProfileCombo.SelectedIndex = 0;
            _settings.ActiveAblProfileId = ((AblProfileChoice)_ablProfileCombo.SelectedItem!).Id;
        }
        _updatingAblProfileSelection = false;
    }

    private void ChangeAblProfile()
    {
        if (_updatingAblProfileSelection ||
            _ablProfileCombo.SelectedItem is not AblProfileChoice choice)
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
        _settings.Save();
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
        _ablProfileCombo.Enabled = _ablProfileCombo.Items.Count > 0;
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
        _updatingFrameRateSelection = true;
        _frameRateModeCombo.Items.Clear();
        _frameRateModeCombo.Items.AddRange(
        [
            new FrameRateModeChoice(
                FollowOutputFrameRateMode,
                Localization.T("FrameRateFollowOutput")),
            new FrameRateModeChoice(FixedFrameRateMode, Localization.T("FrameRateFixed")),
            new FrameRateModeChoice(UnlimitedFrameRateMode, Localization.T("FrameRateUnlimited"))
        ]);
        _frameRateModeCombo.SelectedItem = _frameRateModeCombo.Items
            .Cast<FrameRateModeChoice>()
            .First(x => x.Code == selectedMode);
        _updatingFrameRateSelection = false;
    }

    private void ChangeFrameRateMode()
    {
        if (_updatingFrameRateSelection ||
            _frameRateModeCombo.SelectedItem is not FrameRateModeChoice choice)
        {
            return;
        }

        _settings.FrameRateMode = choice.Code;
        _settings.Save();
        UpdateFrameRateSettingsState();
    }

    private void ChangeFrameRateLimit()
    {
        if (_updatingFrameRateSelection)
            return;

        _settings.FrameRateLimit = decimal.ToInt32(_frameRateLimit.Value);
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
        _updatingScreenshotSelection = true;
        _screenshotModeCombo.Items.Clear();
        _screenshotModeCombo.Items.AddRange(
        [
            new ScreenshotModeChoice(AutomaticScreenshotMode, Localization.T("ScreenshotAutomatic")),
            new ScreenshotModeChoice(PromptScreenshotMode, Localization.T("ScreenshotPrompt"))
        ]);
        _screenshotModeCombo.SelectedItem = _screenshotModeCombo.Items
            .Cast<ScreenshotModeChoice>()
            .First(x => x.Code == selectedMode);
        _updatingScreenshotSelection = false;
    }

    private void ChangeScreenshotMode()
    {
        if (_updatingScreenshotSelection ||
            _screenshotModeCombo.SelectedItem is not ScreenshotModeChoice choice)
        {
            return;
        }

        _settings.ScreenshotSaveMode = choice.Code;
        _settings.Save();
        UpdateScreenshotSettingsState();
    }

    private void UpdateScreenshotSettingsState()
    {
        bool automatic = NormalizeScreenshotMode(_settings.ScreenshotSaveMode) == AutomaticScreenshotMode;
        bool enabled = _session is null || !_singleDisplayAnalysis;
        _screenshotDirectoryTextBox.Text = GetScreenshotDirectory();
        _screenshotModeCombo.Enabled = enabled;
        _screenshotDirectoryTextBox.Enabled = enabled && automatic;
        _chooseScreenshotDirectoryButton.Enabled = enabled && automatic;
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
        if (_updatingLanguageSelection || _languageCombo.SelectedItem is not LanguageChoice choice)
            return;

        _settings.Language = choice.Code;
        _settings.Save();
        Localization.SetPreference(choice.Code);
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        SuspendLayout();
        ApplyLocalizedControlText(this);
        RefreshScreenshotModeChoices();
        RefreshFrameRateModeChoices();
        RefreshOperationModeChoices();
        RefreshAblProfileChoices();
        UpdateScreenshotSettingsState();
        UpdateFrameRateSettingsState();
        UpdateAblControlsState();
        _shortcutLabel.Text = Localization.T("ShortcutText");
        RefreshDisplayLabels();
        UpdateModeText();

        if (_session is not null)
            _statusLabel.Text = _singleDisplayAnalysis
                ? Localization.T("StatusAnalyzing")
                : Localization.F("StatusMirroring", _mirrorWindows.Count);
        else if (!_hasEnumeratedDisplays)
            _statusLabel.Text = Localization.T("StatusEnumerating");
        else if (_displays.Count == 0)
            _statusLabel.Text = Localization.T("StatusNoDisplays");
        else if (_displays.Count == 1)
            _statusLabel.Text = Localization.T("StatusFoundSingleDisplay");
        else
            _statusLabel.Text = Localization.F("StatusFoundDisplays", _displays.Count);

        _trayShowItem.Text = Localization.T("TrayShow");
        _trayExitItem.Text = Localization.T("TrayExit");
        UpdateTrayState();
        foreach (StatusOverlayForm overlay in _statusOverlays)
            overlay.ApplyLanguage();
        foreach (AnalysisOverlayForm overlay in _analysisOverlays)
            overlay.ApplyLanguage();
        ResumeLayout(true);
    }

    private void RefreshDisplayLabels()
    {
        string? captureId = (_captureCombo.SelectedItem as DisplayTarget)?.StableId;
        string? presentId = (_presentCombo.SelectedItem as DisplayTarget)?.StableId;
        _updatingDisplaySelection = true;
        try
        {
            _captureCombo.DataSource = _displays.ToArray();
            _presentCombo.DataSource = _displays.ToArray();
            SelectDisplay(_captureCombo, captureId);
            SelectDisplay(_presentCombo, presentId);
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

        ComboBox comboBox = capture ? _captureCombo : _presentCombo;
        if (comboBox.SelectedItem is not DisplayTarget display)
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

    private bool IsAnalysisModeSelected() =>
        _modeCombo.SelectedItem is OperationModeChoice choice
            ? choice.Code == AnalysisOperationMode
            : NormalizeOperationMode(_settings.OperationMode) == AnalysisOperationMode;

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
            _hasEnumeratedDisplays = true;
            _displays = DxgiDisplayEnumerator.GetDisplays().Where(x => x.AttachedToDesktop).ToArray();
            _updatingDisplaySelection = true;
            try
            {
                _captureCombo.DataSource = _displays.ToArray();
                _presentCombo.DataSource = _displays.ToArray();

                if (_displays.Count == 0)
                {
                    _statusLabel.Text = Localization.T("StatusNoDisplays");
                    _startButton.Enabled = false;
                    UpdateModeText();
                    return false;
                }

                if (!SelectDisplay(_captureCombo, captureDisplayId))
                    _captureCombo.SelectedIndex = 0;
                if (!SelectDisplay(_presentCombo, presentDisplayId))
                    _presentCombo.SelectedIndex = FindDefaultPresentIndex();
            }
            finally
            {
                _updatingDisplaySelection = false;
            }

            SaveInitialDisplaySelection();
            RefreshFrameRateModeChoices();
            SetRunningState(false);
            _statusLabel.Text = _displays.Count == 1
                ? Localization.T("StatusFoundSingleDisplay")
                : Localization.F("StatusFoundDisplays", _displays.Count);
            return true;
        }
        catch (Exception exception)
        {
            _statusLabel.Text = exception.Message;
            _startButton.Enabled = false;
            MessageBox.Show(
                exception.ToString(),
                Localization.T("EnumerateFailed"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    private static bool SelectDisplay(ComboBox comboBox, string? displayId)
    {
        if (string.IsNullOrWhiteSpace(displayId))
            return false;

        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is DisplayTarget display &&
                (string.Equals(display.StableId, displayId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(display.DeviceName, displayId, StringComparison.OrdinalIgnoreCase)))
            {
                comboBox.SelectedIndex = i;
                return true;
            }
        }

        return false;
    }

    private void SaveInitialDisplaySelection()
    {
        bool changed = false;
        if (string.IsNullOrWhiteSpace(_settings.CaptureDisplayId) &&
            _captureCombo.SelectedItem is DisplayTarget capture)
        {
            _settings.CaptureDisplayId = capture.StableId;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.OutputDisplayId) &&
            _displays.Count > 1 &&
            _presentCombo.SelectedItem is DisplayTarget present &&
            _captureCombo.SelectedItem is DisplayTarget selectedCapture &&
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

        DisplayTarget capture = _captureCombo.SelectedItem as DisplayTarget ?? _displays[0];
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
            string? captureDisplayId = (_captureCombo.SelectedItem as DisplayTarget)?.StableId;
            string? presentDisplayId = (_presentCombo.SelectedItem as DisplayTarget)?.StableId;
            if (!RefreshDisplays(captureDisplayId, presentDisplayId))
                return;
        }

        if (_captureCombo.SelectedItem is not DisplayTarget capture)
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
                    !analysisOnly && _falseColor.Checked,
                    analysisOnly: analysisOnly);
                overlay.SetAblEstimate(
                    activeAblProfile is not null,
                    activeAblProfile?.Name ?? string.Empty);
                if (mirrorWindow is null)
                    overlay.Show();
                else
                    overlay.Show(mirrorWindow);
                _statusOverlays.Add(overlay);
                if (analysisOnly && !overlay.SetCaptureExclusion(true))
                    throw new InvalidOperationException(Localization.T("CaptureExclusionFailed"));
                if (!_showStatusOverlay.Checked)
                    overlay.Hide();

                AnalysisOverlayForm analysisOverlay = new(
                    target.Bounds,
                    capture,
                    _showCieAnalysis.Checked,
                    _showLuminanceMarkers.Checked);
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
                (float)_paperWhite.Value,
                GetFrameRateMode(),
                decimal.ToInt32(_frameRateLimit.Value),
                analysisOnly || _renderCursor.Checked,
                !analysisOnly && _falseColor.Checked,
                true);
            _session.StatusChanged += OnSessionStatusChanged;
            _session.TelemetryChanged += OnSessionTelemetryChanged;
            _session.LuminanceChanged += OnSessionLuminanceChanged;
            _session.GamutChanged += OnSessionGamutChanged;
            _session.Failed += OnSessionFailed;
            _session.Stopped += OnSessionStopped;
            _session.SetGamutAnalysis(_showCieAnalysis.Checked);
            _session.SetAblProfile(activeAblProfile);
            _session.Start();

            SetRunningState(true);
            _statusLabel.Text = analysisOnly
                ? Localization.T("StatusAnalyzing")
                : Localization.F("StatusMirroring", targets.Count);
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

        if (_presentCombo.SelectedItem is not DisplayTarget present)
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
            session.StatusChanged -= OnSessionStatusChanged;
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
        SetRunningState(false);
        _statusLabel.Text = Localization.T("StatusStopped");
        if (wasSingleDisplayAnalysis && restoreControlWindow && !_exitRequested)
            RecallControlWindow();
    }

    private void SetRunningState(bool running)
    {
        bool analysisMode = running ? _singleDisplayAnalysis : IsAnalysisModeSelected();
        _captureCombo.Enabled = !running;
        _presentCombo.Enabled = !running && !analysisMode && !_allOutputs.Checked;
        _allOutputs.Enabled = !running && !analysisMode && _displays.Count > 1;
        _paperWhite.Enabled = !running;
        _resetPaperWhiteButton.Enabled = !running;
        _frameRateModeCombo.Enabled = !running;
        _modeCombo.Enabled = !running;
        UpdateFrameRateSettingsState();
        _showStatusOverlay.Enabled = true;
        _renderCursor.Enabled = !running && !analysisMode;
        _showPointerLuminance.Enabled = true;
        _falseColor.Enabled = !analysisMode;
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
        UpdateScreenshotSettingsState();
        UpdateModeText();
        UpdateTrayState();
    }

    private void UpdatePresentSelectorState()
    {
        bool analysisMode = _session is not null
            ? _singleDisplayAnalysis
            : IsAnalysisModeSelected();
        _presentCombo.Enabled = _session is null && !analysisMode && !_allOutputs.Checked;
    }

    private void UpdateModeText()
    {
        bool analysisMode = _session is not null
            ? _singleDisplayAnalysis
            : IsAnalysisModeSelected();
        _startButton.Text = Localization.T(analysisMode ? "StartAnalysis" : "StartMirror");
        _showStatusOverlay.Text = Localization.T("StatusOverlay");
        _showCieAnalysis.Text = Localization.T("CieAnalysis");
        _shortcutLabel.Text = Localization.T("ShortcutText");
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

    private void UpdateFalseColorMode()
    {
        bool enabled = !_singleDisplayAnalysis && _falseColor.Checked;
        _session?.SetFalseColor(enabled);
        foreach (StatusOverlayForm overlay in _statusOverlays)
            overlay.SetFalseColor(enabled);
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
        foreach (AnalysisOverlayForm overlay in _analysisOverlays)
        {
            overlay.SetShowCie(_showCieAnalysis.Checked);
            UpdateAnalysisOverlayVisibility(overlay);
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

    private void TakeOutputScreenshots()
    {
        if (_takingScreenshot)
            return;
        if (_session is null || _mirrorWindows.Count == 0)
        {
            _statusLabel.Text = Localization.T("ScreenshotNeedsMirror");
            return;
        }

        _takingScreenshot = true;
        bool screenshotSaved = false;
        try
        {
            string[]? paths = ResolveScreenshotPaths();
            if (paths is null)
                return;

            foreach (AnalysisOverlayForm overlay in _analysisOverlays)
                overlay.HideScreenshotNotification();

            foreach (StatusOverlayForm overlay in _statusOverlays)
            {
                if (overlay.Visible)
                {
                    overlay.Invalidate();
                    overlay.Update();
                }
                overlay.SetCaptureExclusion(false);
            }
            foreach (AnalysisOverlayForm overlay in _analysisOverlays)
            {
                if (overlay.Visible)
                {
                    overlay.Invalidate();
                    overlay.Update();
                }
                overlay.SetCaptureExclusion(false);
            }
            foreach (MirrorForm mirrorWindow in _mirrorWindows)
                mirrorWindow.SetCaptureExclusion(false);

            NativeMethods.DwmFlush();

            for (int i = 0; i < _mirrorWindows.Count; i++)
            {
                Rectangle bounds = _mirrorWindows[i].Bounds;
                using Bitmap bitmap = new(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        bounds.Location,
                        Point.Empty,
                        bounds.Size,
                        CopyPixelOperation.SourceCopy);
                }
                bitmap.Save(paths[i], ImageFormat.Png);
            }

            _statusLabel.Text = paths.Length == 1
                ? Localization.F("ScreenshotSaved", paths[0])
                : Localization.F("ScreenshotsSaved", paths.Length, Path.GetDirectoryName(paths[0]) ?? string.Empty);
            screenshotSaved = true;
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
            foreach (MirrorForm mirrorWindow in _mirrorWindows)
                mirrorWindow.SetCaptureExclusion(true);
            foreach (StatusOverlayForm overlay in _statusOverlays)
                overlay.SetCaptureExclusion(true);
            foreach (AnalysisOverlayForm overlay in _analysisOverlays)
                overlay.SetCaptureExclusion(true);
            NativeMethods.DwmFlush();
            _takingScreenshot = false;
        }

        if (screenshotSaved)
        {
            foreach (AnalysisOverlayForm overlay in _analysisOverlays)
                overlay.ShowScreenshotNotification();
        }
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

    private void OnSessionStatusChanged(string text) => PostToUi(() => _statusLabel.Text = text);

    private void OnSessionTelemetryChanged(MirrorTelemetry telemetry) => PostToUi(() =>
    {
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
        foreach (AnalysisOverlayForm overlay in _analysisOverlays)
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
        DisplayTarget? capture = _captureCombo.SelectedItem as DisplayTarget ?? _displays.FirstOrDefault();
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
        UnregisterConfiguredHotKeys();
        _displayChangeTimer.Stop();
        _displayChangeTimer.Dispose();
        StopMirror();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIconImage.Dispose();
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
                if (_session is null)
                    StartMirror();
                else
                    StopMirror();
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
            if (hotKeyId == ToggleFalseColorHotKeyId)
            {
                if (_session is not null ? _singleDisplayAnalysis : IsAnalysisModeSelected())
                {
                    _statusLabel.Text = Localization.T("FalseColorNeedsOutput");
                    return;
                }
                _falseColor.Checked = !_falseColor.Checked;
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
                ? (_captureCombo.SelectedItem as DisplayTarget)?.StableId
                : null;
            _displayChangePresentId = _restartMirrorAfterDisplayChange
                ? _singleDisplayAnalysis
                    ? _settings.OutputDisplayId
                    : (_presentCombo.SelectedItem as DisplayTarget)?.StableId
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

        uint modifiers = NativeMethods.ModControl | NativeMethods.ModNoRepeat;
        List<string> failedHotKeys = [];
        RegisterHotKeyOrRecord(ToggleMirrorHotKeyId, modifiers, NativeMethods.VirtualKeyF8, "Ctrl + F8", failedHotKeys);
        RegisterHotKeyOrRecord(ToggleFalseColorHotKeyId, modifiers, NativeMethods.VirtualKeyF9, "Ctrl + F9", failedHotKeys);
        RegisterHotKeyOrRecord(ScreenshotHotKeyId, modifiers, NativeMethods.VirtualKeyF10, "Ctrl + F10", failedHotKeys);
        RegisterHotKeyOrRecord(ToggleStatusOverlayHotKeyId, modifiers, NativeMethods.VirtualKeyF11, "Ctrl + F11", failedHotKeys);
        RegisterHotKeyOrRecord(RecallWindowHotKeyId, modifiers, NativeMethods.VirtualKeyF12, "Ctrl + F12", failedHotKeys);

        if (failedHotKeys.Count > 0)
        {
            string separator = Localization.Current == UiLanguage.Chinese ? "、" : ", ";
            _statusLabel.Text = Localization.F("HotkeyFailed", string.Join(separator, failedHotKeys));
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
        NativeMethods.UnregisterHotKey(Handle, ToggleFalseColorHotKeyId);
    }

    private sealed record LanguageChoice(string Code, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record ScreenshotModeChoice(string Code, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record FrameRateModeChoice(string Code, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record OperationModeChoice(string Code, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record AblProfileChoice(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
