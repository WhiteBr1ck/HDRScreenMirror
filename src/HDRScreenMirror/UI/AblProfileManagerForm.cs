using System.Globalization;
using System.Drawing.Drawing2D;

namespace HDRScreenMirror.UI;

internal sealed class AblProfileManagerForm : Form
{
    private static readonly Color WindowBackground = Color.FromArgb(246, 247, 250);
    private static readonly Color SurfaceBackground = Color.White;
    private static readonly Color PrimaryText = Color.FromArgb(31, 36, 46);
    private static readonly Color SecondaryText = Color.FromArgb(92, 101, 116);
    private static readonly Color Accent = Color.FromArgb(45, 112, 225);
    private static readonly Color AccentHover = Color.FromArgb(36, 96, 199);
    private static readonly Color SubtleSurface = Color.FromArgb(248, 249, 252);
    private static readonly Color BorderColor = Color.FromArgb(222, 226, 234);
    private static readonly Color Danger = Color.FromArgb(190, 53, 63);

    private readonly List<AblProfile> _profiles;
    private readonly Icon _windowIcon;
    private readonly ListBox _profileList = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        BorderStyle = BorderStyle.None,
        BackColor = SubtleSurface,
        Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular),
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 42,
        AllowDrop = true,
        Cursor = Cursors.Hand
    };
    private readonly TextBox _nameTextBox = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular),
        Margin = new Padding(0, 6, 0, 6)
    };
    private readonly DataGridView _measurements = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoGenerateColumns = false,
        BackgroundColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
        EnableHeadersVisualStyles = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        MultiSelect = false,
        EditMode = DataGridViewEditMode.EditOnEnter
    };
    private readonly Panel _editorPanel = new() { Dock = DockStyle.Fill };
    private readonly Panel _emptyStatePanel = new() { Dock = DockStyle.Fill, BackColor = SubtleSurface };
    private readonly EotfCurveEditor _eotfEditor = new()
    {
        Dock = DockStyle.Fill,
        Margin = Padding.Empty,
        Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
    };
    private readonly Panel _eotfPanel = new() { Dock = DockStyle.Fill };
    private readonly Button _newButton;
    private readonly Button _duplicateButton;
    private readonly Button _deleteButton;
    private readonly Button _resetEotfButton;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;
    private bool _loading;
    private int _selectedIndex = -1;
    private Point _profileDragStart;
    private string? _profileDragProfileId;
    private int _profileDropIndex = -1;

    public AblProfileManagerForm(IEnumerable<AblProfile> profiles, string activeProfileId)
    {
        _profiles = profiles.Select(profile => profile.Clone()).ToList();
        ActiveProfileId = activeProfileId;
        _windowIcon = BrandAssets.LoadApplicationIcon();

        Text = Localization.T("AblProfilesTitle");
        Icon = _windowIcon;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = WindowBackground;
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
        ClientSize = new Size(1500, 780);
        MinimumSize = new Size(1320, 740);

        _newButton = CreateButton("AblNew", 96, ButtonTone.Primary);
        _duplicateButton = CreateButton("AblDuplicate", 96, ButtonTone.Neutral);
        _deleteButton = CreateButton("AblDelete", 96, ButtonTone.Danger);
        _resetEotfButton = CreateButton("AblEotfReset", 148, ButtonTone.Neutral);
        _saveButton = CreateButton("AblSave", 112, ButtonTone.Primary);
        _cancelButton = CreateButton("AblCancel", 112, ButtonTone.Neutral);
        _cancelButton.DialogResult = DialogResult.Cancel;

        ConfigureMeasurementGrid();
        Controls.Add(BuildLayout());

        _profileList.SelectedIndexChanged += (_, _) => ChangeSelectedProfile();
        _profileList.DrawItem += DrawProfileListItem;
        _profileList.MouseDown += BeginProfileDrag;
        _profileList.MouseMove += ContinueProfileDrag;
        _profileList.DragEnter += UpdateProfileDrag;
        _profileList.DragOver += UpdateProfileDrag;
        _profileList.DragDrop += DropProfile;
        _profileList.DragLeave += (_, _) => ClearProfileDropIndicator();
        _nameTextBox.TextChanged += (_, _) => ChangeProfileName();
        _measurements.CellValidating += ValidateMeasurementCell;
        _measurements.CellEndEdit += (_, eventArgs) =>
        {
            _measurements.Rows[eventArgs.RowIndex].ErrorText = string.Empty;
            StoreEditorValues(false);
            LoadEotfForSelectedProfile();
        };
        _eotfEditor.CurveChanged += values => StoreEotfCurve(values);
        _eotfEditor.ClipPointChanged += pqPercent => StoreEotfClipPoint(pqPercent);
        _newButton.Click += (_, _) => AddProfile();
        _duplicateButton.Click += (_, _) => DuplicateProfile();
        _deleteButton.Click += (_, _) => DeleteProfile();
        _resetEotfButton.Click += (_, _) => RestoreStandardPq();
        _saveButton.Click += (_, _) => SaveAndClose();

        RefreshProfileList(activeProfileId);
        UpdateEditorState();
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    public IReadOnlyList<AblProfile> Profiles =>
        _profiles.Select(profile => profile.Clone()).ToArray();

    public string ActiveProfileId { get; private set; }

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        TopMost = Owner?.TopMost == true;
        BringToFront();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _windowIcon.Dispose();

        base.Dispose(disposing);
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 22),
            ColumnCount = 1,
            RowCount = 3,
            BackColor = WindowBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildContent(), 0, 1);
        root.Controls.Add(BuildActions(), 0, 2);
        return root;
    }

    private Control BuildHeader()
    {
        Panel panel = new() { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = Localization.T("AblProfilesTitle"),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold),
            ForeColor = PrimaryText,
            Location = new Point(0, 2)
        });
        return panel;
    }

    private Control BuildContent()
    {
        TableLayoutPanel content = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 276));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 600));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.Controls.Add(BuildProfilePanel(), 0, 0);
        content.Controls.Add(BuildMeasurementPanel(), 1, 0);
        content.Controls.Add(BuildEotfPanel(), 2, 0);
        return content;
    }

    private Control BuildProfilePanel()
    {
        SurfacePanel card = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 16, 0),
            BackColor = SurfaceBackground,
            BorderColor = BorderColor,
            CornerRadius = 12
        };
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        layout.Controls.Add(new Label
        {
            Text = Localization.T("AblProfiles"),
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
            ForeColor = PrimaryText,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        Panel listSurface = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            Margin = new Padding(0, 0, 0, 12),
            BackColor = SubtleSurface
        };
        listSurface.Controls.Add(_profileList);
        layout.Controls.Add(listSurface, 0, 1);

        TableLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _newButton.Dock = DockStyle.Fill;
        _newButton.Margin = new Padding(0, 0, 0, 7);
        buttons.Controls.Add(_newButton, 0, 0);

        TableLayoutPanel secondaryButtons = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        secondaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        secondaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _duplicateButton.Dock = DockStyle.Fill;
        _duplicateButton.Margin = new Padding(0, 0, 5, 0);
        _deleteButton.Dock = DockStyle.Fill;
        _deleteButton.Margin = new Padding(5, 0, 0, 0);
        secondaryButtons.Controls.Add(_duplicateButton, 0, 0);
        secondaryButtons.Controls.Add(_deleteButton, 1, 0);
        buttons.Controls.Add(secondaryButtons, 0, 1);

        layout.Controls.Add(buttons, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildMeasurementPanel()
    {
        SurfacePanel card = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 14, 18, 16),
            Margin = new Padding(0),
            BackColor = SurfaceBackground,
            BorderColor = BorderColor,
            CornerRadius = 12
        };
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = Localization.T("AblMeasurements"),
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = PrimaryText
        }, 0, 0);

        _editorPanel.Controls.Add(BuildEditorLayout());
        _emptyStatePanel.Controls.Add(BuildEmptyState());
        Panel editorHost = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
        editorHost.Controls.Add(_editorPanel);
        editorHost.Controls.Add(_emptyStatePanel);
        layout.Controls.Add(editorHost, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildEotfPanel()
    {
        SurfacePanel card = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 14, 18, 16),
            Margin = new Padding(16, 0, 0, 0),
            BackColor = SurfaceBackground,
            BorderColor = BorderColor,
            CornerRadius = 12
        };
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.Controls.Add(new Label
        {
            Text = Localization.T("AblEotfTitle"),
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = PrimaryText
        }, 0, 0);
        layout.Controls.Add(_eotfEditor, 0, 1);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0),
            Margin = Padding.Empty
        };
        _resetEotfButton.Margin = new Padding(0);
        actions.Controls.Add(_resetEotfButton);
        layout.Controls.Add(actions, 0, 2);

        _eotfPanel.Controls.Add(layout);
        card.Controls.Add(_eotfPanel);
        return card;
    }

    private Control BuildEditorLayout()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = Localization.T("AblProfileName"),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = SecondaryText
        }, 0, 0);
        layout.Controls.Add(_nameTextBox, 0, 1);
        layout.Controls.Add(_measurements, 0, 2);
        return layout;
    }

    private Control BuildEmptyState()
    {
        TableLayoutPanel empty = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24),
            BackColor = SubtleSurface
        };
        empty.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        empty.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        empty.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        empty.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        empty.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        empty.Controls.Add(new Label
        {
            Text = "+",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 28f, FontStyle.Regular),
            ForeColor = Accent
        }, 0, 1);
        empty.Controls.Add(new Label
        {
            Text = Localization.T("AblEmptyTitle"),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
            ForeColor = PrimaryText
        }, 0, 2);
        empty.Controls.Add(new Label
        {
            Text = Localization.T("AblEmptyHelp"),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular),
            ForeColor = SecondaryText
        }, 0, 3);
        return empty;
    }

    private Control BuildActions()
    {
        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 18, 0, 4)
        };
        actions.Controls.Add(_saveButton);
        actions.Controls.Add(_cancelButton);
        return actions;
    }

    private void ConfigureMeasurementGrid()
    {
        _measurements.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(239, 242, 247),
            ForeColor = PrimaryText,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleCenter,
            SelectionBackColor = Color.FromArgb(239, 242, 247),
            SelectionForeColor = PrimaryText
        };
        _measurements.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = PrimaryText,
            SelectionBackColor = Color.FromArgb(223, 234, 252),
            SelectionForeColor = PrimaryText,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            Padding = new Padding(8, 0, 8, 0),
            NullValue = string.Empty
        };
        _measurements.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 252);
        _measurements.ColumnHeadersHeight = 40;
        _measurements.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _measurements.RowTemplate.Height = 36;

        _measurements.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "AplPercent",
            HeaderText = Localization.T("AblAplColumn"),
            ReadOnly = true,
            Width = 180,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Regular)
            }
        });
        _measurements.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PeakNits",
            HeaderText = Localization.T("AblPeakColumn"),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
                Format = "0.###"
            }
        });

        foreach (int aplPercent in AblProfile.SupportedAplPercentages)
        {
            int rowIndex = _measurements.Rows.Add(
                aplPercent is 1 or 100
                    ? Localization.F("AblRequiredApl", aplPercent)
                    : $"{aplPercent}%",
                null);
            if (aplPercent is 1 or 100)
            {
                _measurements.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(240, 246, 255);
                _measurements.Rows[rowIndex].DefaultCellStyle.SelectionBackColor = Color.FromArgb(211, 228, 253);
            }
            _measurements.Rows[rowIndex].Tag = aplPercent;
        }
    }

    private void DrawProfileListItem(object? sender, DrawItemEventArgs eventArgs)
    {
        if (eventArgs.Index < 0 || eventArgs.Index >= _profileList.Items.Count)
            return;

        bool selected = (eventArgs.State & DrawItemState.Selected) != 0;
        Color background = selected ? Color.FromArgb(229, 238, 253) : SubtleSurface;
        using SolidBrush backgroundBrush = new(background);
        eventArgs.Graphics.FillRectangle(backgroundBrush, eventArgs.Bounds);
        if (selected)
        {
            using SolidBrush accentBrush = new(Accent);
            eventArgs.Graphics.FillRectangle(
                accentBrush,
                new Rectangle(eventArgs.Bounds.Left, eventArgs.Bounds.Top + 5, 4, eventArgs.Bounds.Height - 10));
        }

        Rectangle textBounds = new(
            eventArgs.Bounds.Left + 14,
            eventArgs.Bounds.Top,
            eventArgs.Bounds.Width - 42,
            eventArgs.Bounds.Height);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            _profileList.Items[eventArgs.Index].ToString(),
            _profileList.Font,
            textBounds,
            PrimaryText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        using Pen gripPen = new(Color.FromArgb(145, 153, 167), 1.5f);
        int gripX = eventArgs.Bounds.Right - 15;
        int gripY = eventArgs.Bounds.Top + eventArgs.Bounds.Height / 2;
        eventArgs.Graphics.DrawLine(gripPen, gripX - 4, gripY - 3, gripX + 2, gripY - 3);
        eventArgs.Graphics.DrawLine(gripPen, gripX - 4, gripY, gripX + 2, gripY);
        eventArgs.Graphics.DrawLine(gripPen, gripX - 4, gripY + 3, gripX + 2, gripY + 3);

        if (_profileDropIndex == eventArgs.Index ||
            (_profileDropIndex == _profileList.Items.Count && eventArgs.Index == _profileList.Items.Count - 1))
        {
            int lineY = _profileDropIndex == _profileList.Items.Count
                ? eventArgs.Bounds.Bottom - 2
                : eventArgs.Bounds.Top + 1;
            using Pen dropPen = new(Accent, 2);
            eventArgs.Graphics.DrawLine(
                dropPen,
                eventArgs.Bounds.Left + 8,
                lineY,
                eventArgs.Bounds.Right - 8,
                lineY);
        }
    }

    private void BeginProfileDrag(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
            return;

        int index = _profileList.IndexFromPoint(eventArgs.Location);
        _profileDragStart = eventArgs.Location;
        _profileDragProfileId = index >= 0 && index < _profiles.Count ? _profiles[index].Id : null;
    }

    private void ContinueProfileDrag(object? sender, MouseEventArgs eventArgs)
    {
        if ((eventArgs.Button & MouseButtons.Left) == 0 || string.IsNullOrWhiteSpace(_profileDragProfileId))
            return;

        Size dragSize = SystemInformation.DragSize;
        Rectangle dragBounds = new(
            _profileDragStart.X - dragSize.Width / 2,
            _profileDragStart.Y - dragSize.Height / 2,
            dragSize.Width,
            dragSize.Height);
        if (dragBounds.Contains(eventArgs.Location))
            return;

        string profileId = _profileDragProfileId;
        _profileList.DoDragDrop(new ProfileDragData(profileId), DragDropEffects.Move);
        _profileDragProfileId = null;
        ClearProfileDropIndicator();
    }

    private void UpdateProfileDrag(object? sender, DragEventArgs eventArgs)
    {
        if (eventArgs.Data?.GetData(typeof(ProfileDragData)) is not ProfileDragData)
        {
            eventArgs.Effect = DragDropEffects.None;
            ClearProfileDropIndicator();
            return;
        }

        eventArgs.Effect = DragDropEffects.Move;
        Point location = _profileList.PointToClient(new Point(eventArgs.X, eventArgs.Y));
        int insertionIndex = GetProfileInsertionIndex(location);
        if (_profileDropIndex != insertionIndex)
        {
            _profileDropIndex = insertionIndex;
            _profileList.Invalidate();
        }
    }

    private int GetProfileInsertionIndex(Point location)
    {
        int itemIndex = _profileList.IndexFromPoint(location);
        if (itemIndex < 0)
            return location.Y <= 0 ? 0 : _profileList.Items.Count;

        Rectangle itemBounds = _profileList.GetItemRectangle(itemIndex);
        return location.Y >= itemBounds.Top + itemBounds.Height / 2 ? itemIndex + 1 : itemIndex;
    }

    private void DropProfile(object? sender, DragEventArgs eventArgs)
    {
        if (eventArgs.Data?.GetData(typeof(ProfileDragData)) is not ProfileDragData dragData)
            return;

        int sourceIndex = _profiles.FindIndex(profile =>
            string.Equals(profile.Id, dragData.ProfileId, StringComparison.Ordinal));
        int insertionIndex = _profileDropIndex;
        ClearProfileDropIndicator();
        if (sourceIndex < 0 || insertionIndex < 0 || insertionIndex > _profiles.Count)
            return;

        _measurements.EndEdit();
        if (!StoreEditorValues(false))
            return;

        int destinationIndex = insertionIndex > sourceIndex ? insertionIndex - 1 : insertionIndex;
        destinationIndex = Math.Clamp(destinationIndex, 0, _profiles.Count - 1);
        if (destinationIndex == sourceIndex)
            return;

        AblProfile profile = _profiles[sourceIndex];
        _profiles.RemoveAt(sourceIndex);
        _profiles.Insert(destinationIndex, profile);
        RefreshProfileList(profile.Id);
    }

    private void ClearProfileDropIndicator()
    {
        if (_profileDropIndex < 0)
            return;

        _profileDropIndex = -1;
        _profileList.Invalidate();
    }

    private static Button CreateButton(string localizationKey, int width, ButtonTone tone)
    {
        Button button = new()
        {
            Text = Localization.T(localizationKey),
            Tag = localizationKey,
            Width = width,
            Height = 44,
            MinimumSize = new Size(width, 44),
            Margin = new Padding(6),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 1;
        button.EnabledChanged += (_, _) => ApplyButtonAppearance(button, tone);
        ApplyButtonAppearance(button, tone);
        return button;
    }

    private static void ApplyButtonAppearance(Button button, ButtonTone tone)
    {
        if (!button.Enabled)
        {
            button.BackColor = Color.FromArgb(241, 243, 247);
            button.ForeColor = Color.FromArgb(155, 162, 174);
            button.FlatAppearance.BorderColor = Color.FromArgb(225, 229, 236);
            button.FlatAppearance.MouseOverBackColor = button.BackColor;
            button.FlatAppearance.MouseDownBackColor = button.BackColor;
            button.Cursor = Cursors.Default;
            return;
        }

        bool primary = tone == ButtonTone.Primary;
        bool danger = tone == ButtonTone.Danger;
        button.BackColor = primary ? Accent : Color.White;
        button.ForeColor = primary ? Color.White : danger ? Danger : PrimaryText;
        button.FlatAppearance.BorderColor = primary
            ? Accent
            : danger ? Color.FromArgb(228, 181, 185) : BorderColor;
        button.FlatAppearance.MouseOverBackColor = primary
            ? AccentHover
            : danger ? Color.FromArgb(254, 244, 245) : Color.FromArgb(244, 246, 250);
        button.FlatAppearance.MouseDownBackColor = primary
            ? Color.FromArgb(31, 83, 173)
            : danger ? Color.FromArgb(250, 232, 234) : Color.FromArgb(234, 238, 245);
        button.Cursor = Cursors.Hand;
    }

    private void RefreshProfileList(string? selectedProfileId = null)
    {
        _loading = true;
        _profileList.Items.Clear();
        foreach (AblProfile profile in _profiles)
            _profileList.Items.Add(new ProfileListItem(profile));

        int selectedIndex = !string.IsNullOrWhiteSpace(selectedProfileId)
            ? _profiles.FindIndex(profile =>
                string.Equals(profile.Id, selectedProfileId, StringComparison.Ordinal))
            : Math.Min(_selectedIndex, _profiles.Count - 1);
        _profileList.SelectedIndex = selectedIndex >= 0 ? selectedIndex : _profiles.Count > 0 ? 0 : -1;
        _selectedIndex = _profileList.SelectedIndex;
        LoadSelectedProfile();
        _loading = false;
        UpdateEditorState();
    }

    private void ChangeSelectedProfile()
    {
        if (_loading)
            return;

        _measurements.EndEdit();
        StoreEditorValues(false);
        _selectedIndex = _profileList.SelectedIndex;
        LoadSelectedProfile();
        UpdateEditorState();
    }

    private void LoadSelectedProfile()
    {
        _loading = true;
        AblProfile? profile = GetSelectedProfile();
        _nameTextBox.Text = profile?.Name ?? string.Empty;
        foreach (DataGridViewRow row in _measurements.Rows)
        {
            int aplPercent = (int)row.Tag!;
            row.Cells[1].Value = profile is not null && profile.TryGetPeakNits(aplPercent, out double peakNits)
                ? peakNits.ToString("0.###", CultureInfo.CurrentCulture)
                : string.Empty;
            row.ErrorText = string.Empty;
        }
        LoadEotfForSelectedProfile();
        _loading = false;
    }

    private void LoadEotfForSelectedProfile()
    {
        AblProfile? profile = GetSelectedProfile();
        double referencePeakNits = profile?.ReferencePeakNits ?? 0;
        IReadOnlyList<double> values = profile?.GetEotfEditorValues() ?? [];
        _eotfEditor.LoadCurve(
            referencePeakNits,
            values,
            profile?.UsesStandardPq ?? true,
            profile?.EffectiveEotfClipPqPercent ??
                AblProfile.GetStandardClipPqPercent(referencePeakNits),
            profile?.HasCustomEotfCurve ?? false);
        _eotfEditor.Enabled = profile is not null && referencePeakNits > 0;
        _resetEotfButton.Enabled = profile is not null &&
            referencePeakNits > 0 &&
            !profile.UsesStandardPq;
    }

    private void StoreEotfCurve(IReadOnlyList<double> values)
    {
        if (_loading || GetSelectedProfile() is not AblProfile profile)
            return;

        profile.SetCustomEotf(values);
        _resetEotfButton.Enabled = !profile.UsesStandardPq;
    }

    private void StoreEotfClipPoint(double pqPercent)
    {
        if (_loading || GetSelectedProfile() is not AblProfile profile)
            return;

        profile.SetEotfClipPqPercent(pqPercent);
        _resetEotfButton.Enabled = !profile.UsesStandardPq;
    }

    private void RestoreStandardPq()
    {
        if (GetSelectedProfile() is not AblProfile profile)
            return;

        profile.RestoreStandardPq();
        _eotfEditor.RestoreStandardPq();
        _resetEotfButton.Enabled = false;
    }

    private void ChangeProfileName()
    {
        if (_loading || GetSelectedProfile() is not AblProfile profile)
            return;

        profile.Name = _nameTextBox.Text;
        _profileList.Refresh();
    }

    private void ValidateMeasurementCell(object? sender, DataGridViewCellValidatingEventArgs eventArgs)
    {
        if (_loading || eventArgs.ColumnIndex != 1)
            return;

        string text = eventArgs.FormattedValue?.ToString()?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            _measurements.Rows[eventArgs.RowIndex].ErrorText = string.Empty;
            return;
        }

        if (!TryParsePeakNits(text, out _))
        {
            eventArgs.Cancel = true;
            _measurements.Rows[eventArgs.RowIndex].ErrorText = Localization.T("AblInvalidPeak");
        }
    }

    private bool StoreEditorValues(bool showError)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count)
            return true;

        AblProfile profile = _profiles[_selectedIndex];
        profile.Name = _nameTextBox.Text.Trim();
        List<AblMeasurementPoint> points = [];
        foreach (DataGridViewRow row in _measurements.Rows)
        {
            string text = row.Cells[1].Value?.ToString()?.Trim() ?? string.Empty;
            if (text.Length == 0)
                continue;
            if (!TryParsePeakNits(text, out double peakNits))
            {
                if (showError)
                {
                    MessageBox.Show(
                        Localization.T("AblInvalidPeak"),
                        Localization.T("AblInvalidProfileTitle"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                return false;
            }

            points.Add(new AblMeasurementPoint
            {
                AplPercent = (int)row.Tag!,
                PeakNits = peakNits
            });
        }
        profile.Points = points;
        if (!profile.UsesStandardPq)
            profile.SetCustomEotf(profile.EotfOutputNits);
        return true;
    }

    private static bool TryParsePeakNits(string text, out double peakNits)
    {
        bool parsed = double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.CurrentCulture,
            out peakNits) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out peakNits);
        return parsed && peakNits is > 0 and <= 10000;
    }

    private void AddProfile()
    {
        StoreEditorValues(false);
        AblProfile profile = new()
        {
            Name = Localization.F("AblDefaultProfileName", _profiles.Count + 1)
        };
        _profiles.Add(profile);
        RefreshProfileList(profile.Id);
        _nameTextBox.Focus();
        _nameTextBox.SelectAll();
    }

    private void DuplicateProfile()
    {
        if (GetSelectedProfile() is not AblProfile selected)
            return;

        StoreEditorValues(false);
        AblProfile copy = selected.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = Localization.F("AblCopyName", selected.Name);
        _profiles.Add(copy);
        RefreshProfileList(copy.Id);
    }

    private void DeleteProfile()
    {
        if (GetSelectedProfile() is not AblProfile selected)
            return;

        DialogResult result = MessageBox.Show(
            Localization.F("AblDeleteConfirm", selected.Name),
            Localization.T("AblDeleteTitle"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes)
            return;

        int nextIndex = Math.Min(_selectedIndex, _profiles.Count - 2);
        _profiles.RemoveAt(_selectedIndex);
        _selectedIndex = nextIndex;
        RefreshProfileList(nextIndex >= 0 ? _profiles[nextIndex].Id : null);
    }

    private void SaveAndClose()
    {
        _measurements.EndEdit();
        if (!StoreEditorValues(true))
            return;

        for (int index = 0; index < _profiles.Count; index++)
        {
            AblProfile profile = _profiles[index];
            profile.Normalize();
            if (!profile.IsValid)
            {
                RefreshProfileList(profile.Id);
                MessageBox.Show(
                    Localization.F("AblInvalidProfile", string.IsNullOrWhiteSpace(profile.Name)
                        ? Localization.T("AblUnnamedProfile")
                        : profile.Name),
                    Localization.T("AblInvalidProfileTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
        }

        ActiveProfileId = GetSelectedProfile()?.Id ?? _profiles.FirstOrDefault()?.Id ?? string.Empty;
        DialogResult = DialogResult.OK;
        Close();
    }

    private AblProfile? GetSelectedProfile() =>
        _selectedIndex >= 0 && _selectedIndex < _profiles.Count ? _profiles[_selectedIndex] : null;

    private void UpdateEditorState()
    {
        bool hasSelection = GetSelectedProfile() is not null;
        _nameTextBox.Enabled = hasSelection;
        _measurements.Enabled = hasSelection;
        _eotfPanel.Enabled = hasSelection;
        _duplicateButton.Enabled = hasSelection;
        _deleteButton.Enabled = hasSelection;
        _saveButton.Enabled = hasSelection;
        _editorPanel.Visible = hasSelection;
        _emptyStatePanel.Visible = !hasSelection;
        if (!hasSelection)
            _emptyStatePanel.BringToFront();
        LoadEotfForSelectedProfile();
    }

    private enum ButtonTone
    {
        Neutral,
        Primary,
        Danger
    }

    private sealed record ProfileDragData(string ProfileId);

    private sealed class SurfacePanel : Panel
    {
        public int CornerRadius { get; init; } = 12;
        public Color BorderColor { get; init; } = Color.FromArgb(222, 226, 234);

        public SurfacePanel()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
        }

        protected override void OnSizeChanged(EventArgs eventArgs)
        {
            base.OnSizeChanged(eventArgs);
            if (Width <= 0 || Height <= 0)
                return;

            using GraphicsPath path = CreateRoundedRectangle(ClientRectangle, CornerRadius);
            Region? oldRegion = Region;
            Region = new Region(path);
            oldRegion?.Dispose();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle borderBounds = new(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            using GraphicsPath path = CreateRoundedRectangle(borderBounds, CornerRadius);
            using Pen borderPen = new(BorderColor, 1);
            eventArgs.Graphics.DrawPath(borderPen, path);
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new();
            int diameter = Math.Max(1, radius * 2);
            Rectangle arc = new(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class ProfileListItem(AblProfile profile)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(profile.Name)
            ? Localization.T("AblUnnamedProfile")
            : profile.Name;
    }
}
