using System.ComponentModel;
using HDRScreenMirror.DirectX;
using HDRScreenMirror.Interop;

namespace HDRScreenMirror.UI;

internal sealed class ControlForm : Form
{
    private const decimal DefaultPaperWhiteNits = 203;
    private const int RecallWindowHotKeyId = 0x4848;
    private const int ToggleMirrorHotKeyId = 0x484D;
    private const int EmergencyStopHotKeyId = 0x4851;

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
    private readonly CheckBox _mouseThrough = CreateOptionCheckBox("MouseThrough", false);
    private readonly CheckBox _enableHotKeys = CreateOptionCheckBox("EnableHotkeys", true);
    private readonly CheckBox _minimizeToTray;
    private readonly Button _refreshButton = CreateButton("RefreshDisplays", 176);
    private readonly Button _startButton = CreateButton("StartMirror", 164);
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

    public ControlForm(AppSettings settings)
    {
        Text = "HDRScreenMirror";
        Width = 1120;
        Height = 720;
        MinimumSize = new Size(980, 680);
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
        Controls.Add(BuildLayout());
        ApplyLanguage();

        _refreshButton.Click += (_, _) => RefreshDisplays();
        _resetPaperWhiteButton.Click += (_, _) => _paperWhite.Value = DefaultPaperWhiteNits;
        _startButton.Click += (_, _) => StartMirror();
        _stopButton.Click += (_, _) => StopMirror();
        _aboutButton.Click += (_, _) => ShowAbout();
        _allOutputs.CheckedChanged += (_, _) => UpdatePresentSelectorState();
        _enableHotKeys.CheckedChanged += (_, _) => RegisterConfiguredHotKeys();
        _minimizeToTray.CheckedChanged += (_, _) => SaveCloseBehavior();
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
            RowCount = 9,
            BackColor = Color.FromArgb(248, 249, 251)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
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
        layout.Controls.Add(CreateLabel("InteractionSafety"), 0, 4);
        layout.Controls.Add(BuildInteractionOptions(), 1, 4);

        GroupBox shortcutGroup = new()
        {
            Text = Localization.T("GlobalHotkeys"),
            Tag = "GlobalHotkeys",
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 8),
            BackColor = Color.White
        };
        shortcutGroup.Controls.Add(_shortcutLabel);
        layout.Controls.Add(shortcutGroup, 0, 5);
        layout.SetColumnSpan(shortcutGroup, 2);

        layout.Controls.Add(_noteLabel, 0, 6);
        layout.SetColumnSpan(_noteLabel, 2);

        Control actionRow = BuildActionRow();
        layout.Controls.Add(actionRow, 0, 7);
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
        layout.Controls.Add(statusGroup, 0, 8);
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
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
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
            List<MirrorOutputBinding> bindings = [];
            foreach (DisplayTarget target in targets)
            {
                MirrorForm mirrorWindow = new(target.Bounds, _mouseThrough.Checked);
                mirrorWindow.Show();
                _mirrorWindows.Add(mirrorWindow);
                bindings.Add(new MirrorOutputBinding(target, mirrorWindow.Handle));

                if (_showStatusOverlay.Checked)
                {
                    StatusOverlayForm overlay = new(
                        target.Bounds,
                        capture,
                        target,
                        _mouseThrough.Checked);
                    overlay.Show(mirrorWindow);
                    _statusOverlays.Add(overlay);
                }
            }

            _session = new MirrorSession(
                capture,
                bindings,
                (float)_paperWhite.Value,
                _vsync.Checked,
                _renderCursor.Checked);
            _session.StatusChanged += OnSessionStatusChanged;
            _session.TelemetryChanged += OnSessionTelemetryChanged;
            _session.Failed += OnSessionFailed;
            _session.Stopped += OnSessionStopped;
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
        _showStatusOverlay.Enabled = !running;
        _renderCursor.Enabled = !running;
        _mouseThrough.Enabled = !running;
        _refreshButton.Enabled = !running;
        _startButton.Enabled = !running && _displays.Count > 1;
        _stopButton.Enabled = running;
        UpdateTrayState();
    }

    private void UpdatePresentSelectorState()
    {
        _presentCombo.Enabled = _session is null && !_allOutputs.Checked;
    }

    private void OnSessionStatusChanged(string text) => PostToUi(() => _statusLabel.Text = text);

    private void OnSessionTelemetryChanged(MirrorTelemetry telemetry) => PostToUi(() =>
    {
        foreach (StatusOverlayForm overlay in _statusOverlays)
            overlay.UpdateTelemetry(telemetry);
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
        Screen screen = Screen.AllScreens
            .OrderByDescending(x => IntersectionArea(x.Bounds, capture.Bounds))
            .First();
        Rectangle workArea = screen.WorkingArea;
        WindowState = FormWindowState.Normal;
        Location = new Point(
            workArea.Left + Math.Max(16, (workArea.Width - Width) / 2),
            workArea.Top + Math.Max(16, (workArea.Height - Height) / 2));
    }

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

    private static int IntersectionArea(Rectangle left, Rectangle right)
    {
        Rectangle intersection = Rectangle.Intersect(left, right);
        return Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
    }

    private void ShowAbout()
    {
        using AboutForm about = new();
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

        uint modifiers = NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModShift;
        bool toggleRegistered = NativeMethods.RegisterHotKey(
            Handle,
            ToggleMirrorHotKeyId,
            modifiers,
            NativeMethods.VirtualKeyM);
        bool stopRegistered = NativeMethods.RegisterHotKey(
            Handle,
            EmergencyStopHotKeyId,
            modifiers,
            NativeMethods.VirtualKeyQ);
        bool recallRegistered = NativeMethods.RegisterHotKey(
            Handle,
            RecallWindowHotKeyId,
            modifiers,
            NativeMethods.VirtualKeyH);

        if (!toggleRegistered || !stopRegistered || !recallRegistered)
            _statusLabel.Text = Localization.T("HotkeyFailed");
    }

    private void UnregisterConfiguredHotKeys()
    {
        if (!IsHandleCreated)
            return;

        NativeMethods.UnregisterHotKey(Handle, ToggleMirrorHotKeyId);
        NativeMethods.UnregisterHotKey(Handle, EmergencyStopHotKeyId);
        NativeMethods.UnregisterHotKey(Handle, RecallWindowHotKeyId);
    }

    private sealed record LanguageChoice(string Code, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
