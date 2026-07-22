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
    private const int EmergencyStopHotKeyId = 0x4851;
    private const int ToggleFalseColorHotKeyId = 0x4846;
    private const int ScreenshotHotKeyId = 0x4853;
    private const string AutomaticScreenshotMode = "automatic";
    private const string PromptScreenshotMode = "prompt";

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
    private readonly CheckBox _vsync = CreateOptionCheckBox("VSync", true);
    private readonly CheckBox _showStatusOverlay = CreateOptionCheckBox("StatusOverlay", true);
    private readonly CheckBox _renderCursor = CreateOptionCheckBox("RenderCursor", true);
    private readonly CheckBox _showPointerLuminance = CreateOptionCheckBox("ShowPointerLuminance", true);
    private readonly CheckBox _falseColor = CreateOptionCheckBox("FalseColor", false);
    private readonly CheckBox _showLuminanceMarkers = CreateOptionCheckBox("LuminanceMarkers", false);
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
    private readonly Label _noteLabel = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
        ForeColor = Color.FromArgb(84, 91, 104),
        Padding = new Padding(2, 8, 2, 4)
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
    private bool _takingScreenshot;

    public ControlForm(AppSettings settings)
    {
        Text = "HDRScreenMirror";
        Width = 1120;
        Height = 984;
        MinimumSize = new Size(980, 728);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(248, 249, 251);

        _settings = settings;
        _minimizeToTray = CreateOptionCheckBox("MinimizeToTray", _settings.MinimizeToTrayOnClose);
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
        Controls.Add(BuildLayout());
        ApplyLanguage();

        _refreshButton.Click += (_, _) => RefreshDisplays();
        _resetPaperWhiteButton.Click += (_, _) => _paperWhite.Value = DefaultPaperWhiteNits;
        _startButton.Click += (_, _) => StartMirror();
        _stopButton.Click += (_, _) => StopMirror();
        _aboutButton.Click += (_, _) => ShowAbout();
        _allOutputs.CheckedChanged += (_, _) => UpdatePresentSelectorState();
        _showStatusOverlay.CheckedChanged += (_, _) => UpdateStatusOverlayVisibility();
        _showPointerLuminance.CheckedChanged += (_, _) => UpdatePointerLuminanceVisibility();
        _falseColor.CheckedChanged += (_, _) => UpdateFalseColorMode();
        _showLuminanceMarkers.CheckedChanged += (_, _) => UpdateLuminanceMarkerVisibility();
        _showCieAnalysis.CheckedChanged += (_, _) => UpdateCieAnalysisVisibility();
        _enableHotKeys.CheckedChanged += (_, _) => RegisterConfiguredHotKeys();
        _minimizeToTray.CheckedChanged += (_, _) => SaveCloseBehavior();
        _screenshotModeCombo.SelectedIndexChanged += (_, _) => ChangeScreenshotMode();
        _chooseScreenshotDirectoryButton.Click += (_, _) => ChooseScreenshotDirectory();
        _languageCombo.SelectedIndexChanged += (_, _) => ChangeLanguage();
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
            RowCount = 13,
            BackColor = Color.FromArgb(248, 249, 251)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
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

        layout.Controls.Add(_noteLabel, 0, 10);
        layout.SetColumnSpan(_noteLabel, 2);

        Control actionRow = BuildActionRow();
        layout.Controls.Add(actionRow, 0, 11);
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
        layout.Controls.Add(statusGroup, 0, 12);
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
        FlowLayoutPanel row = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        row.Controls.Add(_paperWhite);
        row.Controls.Add(_resetPaperWhiteButton);
        row.Controls.Add(new Label
        {
            Text = Localization.T("PaperWhiteHelp"),
            Tag = "PaperWhiteHelp",
            AutoSize = true,
            Padding = new Padding(8, 14, 0, 0),
            ForeColor = Color.FromArgb(84, 91, 104)
        });
        return row;
    }

    private Control BuildPresentOptions()
    {
        FlowLayoutPanel row = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        row.Controls.Add(_vsync);
        row.Controls.Add(_showStatusOverlay);
        row.Controls.Add(_renderCursor);
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
        FlowLayoutPanel row = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        row.Controls.Add(_showPointerLuminance);
        row.Controls.Add(_falseColor);
        row.Controls.Add(_showLuminanceMarkers);
        return row;
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
        TableLayoutPanel row = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 404));

        FlowLayoutPanel primaryActions = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        primaryActions.Controls.Add(_startButton);
        primaryActions.Controls.Add(_stopButton);
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
        row.Controls.Add(secondaryActions, 1, 0);
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
        _screenshotDirectoryTextBox.Text = GetScreenshotDirectory();
        _screenshotDirectoryTextBox.Enabled = automatic;
        _chooseScreenshotDirectoryButton.Enabled = automatic;
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
        UpdateScreenshotSettingsState();
        _shortcutLabel.Text = Localization.T("ShortcutText");
        _noteLabel.Text = Localization.T("MainNote");
        RefreshDisplayLabels();

        if (_session is not null)
            _statusLabel.Text = Localization.F("StatusMirroring", _mirrorWindows.Count);
        else if (!_hasEnumeratedDisplays)
            _statusLabel.Text = Localization.T("StatusEnumerating");
        else if (_displays.Count == 0)
            _statusLabel.Text = Localization.T("StatusNoDisplays");
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
        int? captureIndex = (_captureCombo.SelectedItem as DisplayTarget)?.GlobalIndex;
        int? presentIndex = (_presentCombo.SelectedItem as DisplayTarget)?.GlobalIndex;
        _captureCombo.DataSource = _displays.ToArray();
        _presentCombo.DataSource = _displays.ToArray();
        SelectDisplay(_captureCombo, captureIndex);
        SelectDisplay(_presentCombo, presentIndex);
    }

    private static void SelectDisplay(ComboBox comboBox, int? globalIndex)
    {
        if (globalIndex is null)
            return;

        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is DisplayTarget display && display.GlobalIndex == globalIndex)
            {
                comboBox.SelectedIndex = i;
                return;
            }
        }
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

    private string GetScreenshotDirectory() => string.IsNullOrWhiteSpace(_settings.ScreenshotDirectory)
        ? Path.Combine(AppContext.BaseDirectory, "Screenshots")
        : _settings.ScreenshotDirectory;

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        RegisterConfiguredHotKeys();
    }

    private void RefreshDisplays()
    {
        if (_session is not null)
            return;

        try
        {
            _hasEnumeratedDisplays = true;
            _displays = DxgiDisplayEnumerator.GetDisplays().Where(x => x.AttachedToDesktop).ToArray();
            _captureCombo.DataSource = _displays.ToArray();
            _presentCombo.DataSource = _displays.ToArray();

            if (_displays.Count == 0)
            {
                _statusLabel.Text = Localization.T("StatusNoDisplays");
                _startButton.Enabled = false;
                return;
            }

            _captureCombo.SelectedIndex = 0;
            _presentCombo.SelectedIndex = FindDefaultPresentIndex();
            _startButton.Enabled = _displays.Count > 1;
            _statusLabel.Text = Localization.F("StatusFoundDisplays", _displays.Count);
            UpdatePresentSelectorState();
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
        }
    }

    private int FindDefaultPresentIndex()
    {
        if (_displays.Count < 2)
            return 0;

        DisplayTarget capture = _displays[0];
        for (int i = 1; i < _displays.Count; i++)
        {
            if (_displays[i].AdapterIndex == capture.AdapterIndex)
                return i;
        }

        return 1;
    }

    private void StartMirror()
    {
        if (_session is not null || _captureCombo.SelectedItem is not DisplayTarget capture)
            return;

        IReadOnlyList<DisplayTarget> targets = ResolveOutputTargets(capture);
        if (targets.Count == 0)
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
            PositionOnCaptureDisplay(capture);
            if (_moveOutputWindows.Checked)
            {
                Rectangle captureWorkArea = FindScreenForDisplay(capture).WorkingArea;
                WindowRelocator.MoveWindowsToCapture(targets, captureWorkArea);
            }

            List<MirrorOutputBinding> bindings = [];
            foreach (DisplayTarget target in targets)
            {
                MirrorForm mirrorWindow = new(target.Bounds, _mouseThrough.Checked);
                mirrorWindow.Show();
                _mirrorWindows.Add(mirrorWindow);
                bindings.Add(new MirrorOutputBinding(target, mirrorWindow.Handle));

                StatusOverlayForm overlay = new(
                    target.Bounds,
                    capture,
                    target,
                    _mouseThrough.Checked,
                    _showPointerLuminance.Checked,
                    _falseColor.Checked);
                overlay.Show(mirrorWindow);
                if (!_showStatusOverlay.Checked)
                    overlay.Hide();
                _statusOverlays.Add(overlay);

                AnalysisOverlayForm analysisOverlay = new(
                    target.Bounds,
                    capture,
                    _showCieAnalysis.Checked,
                    _showLuminanceMarkers.Checked);
                analysisOverlay.Show(mirrorWindow);
                if (!analysisOverlay.HasVisibleContent)
                    analysisOverlay.Hide();
                _analysisOverlays.Add(analysisOverlay);
            }

            _session = new MirrorSession(
                capture,
                bindings,
                (float)_paperWhite.Value,
                _vsync.Checked,
                _renderCursor.Checked,
                _falseColor.Checked,
                true);
            _session.StatusChanged += OnSessionStatusChanged;
            _session.TelemetryChanged += OnSessionTelemetryChanged;
            _session.LuminanceChanged += OnSessionLuminanceChanged;
            _session.GamutChanged += OnSessionGamutChanged;
            _session.Failed += OnSessionFailed;
            _session.Stopped += OnSessionStopped;
            _session.SetGamutAnalysis(_showCieAnalysis.Checked);
            _session.Start();

            SetRunningState(true);
            TopMost = true;
            BringToFront();
            Activate();
            _statusLabel.Text = Localization.F("StatusMirroring", targets.Count);
        }
        catch (Exception exception)
        {
            StopMirror();
            MessageBox.Show(
                exception.ToString(),
                Localization.T("StartFailed"),
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

    private void StopMirror()
    {
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

        TopMost = false;
        SetRunningState(false);
        _statusLabel.Text = Localization.T("StatusStopped");
    }

    private void SetRunningState(bool running)
    {
        _captureCombo.Enabled = !running;
        _presentCombo.Enabled = !running && !_allOutputs.Checked;
        _allOutputs.Enabled = !running;
        _paperWhite.Enabled = !running;
        _resetPaperWhiteButton.Enabled = !running;
        _vsync.Enabled = !running;
        _showStatusOverlay.Enabled = true;
        _renderCursor.Enabled = !running;
        _showPointerLuminance.Enabled = true;
        _falseColor.Enabled = true;
        _showLuminanceMarkers.Enabled = true;
        _showCieAnalysis.Enabled = true;
        _mouseThrough.Enabled = !running;
        _moveOutputWindows.Enabled = !running;
        _refreshButton.Enabled = !running;
        _startButton.Enabled = !running && _displays.Count > 1;
        _stopButton.Enabled = running;
        UpdateTrayState();
    }

    private void UpdatePresentSelectorState()
    {
        _presentCombo.Enabled = _session is null && !_allOutputs.Checked;
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
    }

    private void UpdatePointerLuminanceVisibility()
    {
        foreach (StatusOverlayForm overlay in _statusOverlays)
            overlay.SetShowPointerLuminance(_showPointerLuminance.Checked);
    }

    private void UpdateFalseColorMode()
    {
        _session?.SetFalseColor(_falseColor.Checked);
        foreach (StatusOverlayForm overlay in _statusOverlays)
            overlay.SetFalseColor(_falseColor.Checked);
    }

    private void UpdateLuminanceMarkerVisibility()
    {
        foreach (AnalysisOverlayForm overlay in _analysisOverlays)
        {
            overlay.SetShowMarkers(_showLuminanceMarkers.Checked);
            UpdateAnalysisOverlayVisibility(overlay);
        }
    }

    private void UpdateCieAnalysisVisibility()
    {
        _session?.SetGamutAnalysis(_showCieAnalysis.Checked);
        foreach (AnalysisOverlayForm overlay in _analysisOverlays)
        {
            overlay.SetShowCie(_showCieAnalysis.Checked);
            UpdateAnalysisOverlayVisibility(overlay);
        }
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
    });

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
        MessageBox.Show(
            exception.ToString(),
            Localization.T("RuntimeFailed"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        StopMirror();
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
        _trayToggleMirrorItem.Text = Localization.T(running ? "TrayStop" : "TrayStart");
        _trayIcon.Text = running ? Localization.T("TrayRunning") : "HDRScreenMirror";
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
        if (message.Msg == NativeMethods.WmHotKey)
        {
            int hotKeyId = message.WParam.ToInt32();
            if (hotKeyId == EmergencyStopHotKeyId)
            {
                if (_session is not null)
                    StopMirror();
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
                _falseColor.Checked = !_falseColor.Checked;
                return;
            }
        }

        base.WndProc(ref message);
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
        RegisterHotKeyOrRecord(EmergencyStopHotKeyId, modifiers, NativeMethods.VirtualKeyF11, "Ctrl + F11", failedHotKeys);
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
        NativeMethods.UnregisterHotKey(Handle, EmergencyStopHotKeyId);
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
}
