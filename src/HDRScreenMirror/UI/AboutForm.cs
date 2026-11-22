using System.Diagnostics;
using System.Reflection;
using HDRScreenMirror.Interop;

namespace HDRScreenMirror.UI;

internal sealed class AboutForm : Form
{
    private const string RepositoryUrl = "https://github.com/WhiteBr1ck/HDRScreenMirror";

    private readonly Icon _windowIcon;
    private readonly Image _logoImage;

    public AboutForm()
    {
        SuspendLayout();
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.7.1";

        _windowIcon = BrandAssets.LoadApplicationIcon();
        _logoImage = BrandAssets.LoadLogoImage();

        Text = Localization.T("AboutTitle");
        Icon = _windowIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, 344);
        BackColor = HextaTheme.Surface;
        Font = HextaTheme.CreateSmallFont();

        PictureBox logo = new()
        {
            Image = _logoImage,
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(28, 26),
            Size = new Size(48, 48),
            TabStop = false
        };
        Label title = new()
        {
            Text = "HDRScreenMirror",
            Font = HextaTheme.CreateTitleFont(FontStyle.Bold),
            ForeColor = HextaTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(88, 24)
        };
        Label subtitle = new()
        {
            Text = Localization.T("AboutSubtitle"),
            Font = HextaTheme.CreateSmallFont(),
            ForeColor = HextaTheme.TextMuted,
            AutoSize = true,
            Location = new Point(89, 54)
        };

        HextaSeparator topDivider = new()
        {
            Dock = DockStyle.None,
            Location = new Point(28, 96),
            Size = new Size(404, 1)
        };

        Label versionLabel = new()
        {
            Text = Localization.F("AboutVersion", version),
            Font = HextaTheme.CreateBodyFont(FontStyle.Bold),
            ForeColor = HextaTheme.TextPrimary,
            AutoSize = false,
            Location = new Point(28, 112),
            Size = new Size(404, 28),
            TextAlign = ContentAlignment.MiddleLeft
        };
        Label details = new()
        {
            Text = Localization.T("AboutDetails"),
            Font = HextaTheme.CreateSmallFont(),
            ForeColor = HextaTheme.TextSecondary,
            AutoSize = false,
            Location = new Point(28, 146),
            Size = new Size(404, 78)
        };

        LinkLabel githubLink = new()
        {
            Text = "GitHub  WhiteBr1ck/HDRScreenMirror",
            Font = HextaTheme.CreateSmallFont(),
            LinkColor = HextaTheme.TextPrimary,
            ActiveLinkColor = HextaTheme.TextSecondary,
            VisitedLinkColor = HextaTheme.TextPrimary,
            LinkBehavior = LinkBehavior.HoverUnderline,
            AutoSize = true,
            Location = new Point(28, 234)
        };
        githubLink.LinkClicked += (_, _) => OpenRepository();

        HextaSeparator bottomDivider = new()
        {
            Dock = DockStyle.None,
            Location = new Point(28, 274),
            Size = new Size(404, 1)
        };

        HextaButton githubButton = new()
        {
            Text = "GitHub",
            Variant = HextaButtonVariant.Secondary,
            Size = new Size(88, 36),
            Location = new Point(28, 290)
        };
        githubButton.Click += (_, _) => OpenRepository();

        HextaButton closeButton = new()
        {
            Text = Localization.T("Close"),
            Variant = HextaButtonVariant.Primary,
            DialogResult = DialogResult.OK,
            Size = new Size(88, 36),
            Location = new Point(344, 290)
        };

        Controls.Add(logo);
        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(topDivider);
        Controls.Add(versionLabel);
        Controls.Add(details);
        Controls.Add(githubLink);
        Controls.Add(bottomDivider);
        Controls.Add(githubButton);
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

    private static void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        }
        catch
        {
        }
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
        {
            _logoImage.Dispose();
            _windowIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
