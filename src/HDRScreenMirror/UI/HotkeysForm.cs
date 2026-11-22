using System.Drawing.Drawing2D;
using System.Drawing.Text;
using HDRScreenMirror.Interop;

namespace HDRScreenMirror.UI;

internal sealed class HotkeysForm : Form
{
    private readonly Icon _windowIcon;

    public HotkeysForm()
    {
        SuspendLayout();
        _windowIcon = BrandAssets.LoadApplicationIcon();

        Text = Localization.T("GlobalHotkeys");
        Icon = _windowIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(510, 354);
        BackColor = HextaTheme.Surface;
        Font = HextaTheme.CreateSmallFont();

        Label title = new()
        {
            Text = Localization.T("GlobalHotkeys"),
            Font = HextaTheme.CreateTitleFont(FontStyle.Bold),
            ForeColor = HextaTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(24, 20)
        };

        HextaSeparator topDivider = new()
        {
            Dock = DockStyle.None,
            Location = new Point(24, 58),
            Size = new Size(462, 1)
        };

        HotkeyListPanel listPanel = new()
        {
            Location = new Point(24, 70),
            Size = new Size(462, 214)
        };

        HextaSeparator bottomDivider = new()
        {
            Dock = DockStyle.None,
            Location = new Point(24, 292),
            Size = new Size(462, 1)
        };

        HextaButton closeButton = new()
        {
            Text = Localization.T("Close"),
            Variant = HextaButtonVariant.Primary,
            DialogResult = DialogResult.OK,
            Size = new Size(88, 34),
            Location = new Point(398, 306)
        };

        Controls.Add(title);
        Controls.Add(topDivider);
        Controls.Add(listPanel);
        Controls.Add(bottomDivider);
        Controls.Add(closeButton);

        AutoScaleDimensions = new SizeF(HextaTheme.BaselineDpi, HextaTheme.BaselineDpi);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.ApplyImmersiveDarkMode(Handle, HextaTheme.IsDark);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape || keyData == Keys.Enter)
        {
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _windowIcon.Dispose();
        base.Dispose(disposing);
    }

    private sealed class HotkeyListPanel : Control
    {
        public HotkeyListPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            BackColor = HextaTheme.Surface;
            Font = HextaTheme.CreateSmallFont();
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float scale = HextaTheme.ScaleFactor(this);
            (string Key, string Desc)[] rows =
            [
                ("Ctrl + F8", Localization.T("HotkeyDescMirror")),
                ("Ctrl + F9", Localization.T("HotkeyDescFalseColor")),
                ("Ctrl + F10", Localization.T("HotkeyDescFullShot")),
                ("Ctrl + Shift + F10", Localization.T("HotkeyDescRegionShot")),
                ("Ctrl + F11", Localization.T("HotkeyDescPanel")),
                ("Ctrl + F12", Localization.T("HotkeyDescRecall"))
            ];

            float rowH = Height / (float)rows.Length;
            float keyH = 24f * scale;
            using SolidBrush keyBg = new(HextaTheme.MutedSurface);
            using Pen keyBorder = new(HextaTheme.Border, HextaTheme.HairlineWidth);
            using Pen rowDivider = new(HextaTheme.Border, HextaTheme.HairlineWidth);

            for (int i = 0; i < rows.Length; i++)
            {
                float y = i * rowH;
                if (i > 0)
                    graphics.DrawLine(rowDivider, 0, y, Width, y);

                Size keySize = TextRenderer.MeasureText(graphics, rows[i].Key, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                float pillW = keySize.Width + 18f * scale;
                float pillX = Width - pillW - 0.5f;
                float pillY = y + (rowH - keyH) * 0.5f;

                int descW = Math.Max(40, (int)Math.Floor(pillX - 14f * scale));
                Rectangle descRect = new(0, (int)y, descW, (int)rowH);
                TextRenderer.DrawText(
                    graphics,
                    rows[i].Desc,
                    Font,
                    descRect,
                    HextaTheme.TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

                RectangleF pillRect = new(pillX, pillY, pillW, keyH);
                using GraphicsPath path = HextaTheme.CreateRoundedPath(pillRect, 5f * scale);
                graphics.FillPath(keyBg, path);
                graphics.DrawPath(keyBorder, path);

                TextRenderer.DrawText(
                    graphics,
                    rows[i].Key,
                    Font,
                    Rectangle.Round(pillRect),
                    HextaTheme.TextPrimary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }
    }
}
