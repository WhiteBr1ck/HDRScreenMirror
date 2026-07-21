using System.Reflection;

namespace HDRScreenMirror.UI;

internal sealed class AboutForm : Form
{
    private readonly Icon _windowIcon;
    private readonly Image _logoImage;

    public AboutForm()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.3.0";

        _windowIcon = BrandAssets.LoadApplicationIcon();
        _logoImage = BrandAssets.LoadLogoImage();

        Text = Localization.T("AboutTitle");
        Icon = _windowIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 390);
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);

        Label title = new()
        {
            Text = "HDRScreenMirror",
            Font = new Font("Segoe UI Semibold", 20f, FontStyle.Regular),
            ForeColor = Color.FromArgb(28, 31, 38),
            AutoSize = true,
            Location = new Point(104, 28)
        };
        PictureBox logo = new()
        {
            Image = _logoImage,
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(30, 22),
            Size = new Size(62, 62),
            TabStop = false
        };
        Label subtitle = new()
        {
            Text = Localization.T("AboutSubtitle"),
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(93, 101, 116),
            AutoSize = true,
            Location = new Point(106, 70),
            Padding = new Padding(0, 4, 0, 4),
            UseCompatibleTextRendering = true
        };
        Label versionLabel = new()
        {
            Text = Localization.F("AboutVersion", version),
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular),
            ForeColor = Color.FromArgb(46, 51, 61),
            AutoSize = false,
            Location = new Point(35, 116),
            Size = new Size(430, 32),
            TextAlign = ContentAlignment.MiddleLeft
        };
        Label details = new()
        {
            Text = Localization.T("AboutDetails"),
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular),
            ForeColor = Color.FromArgb(70, 76, 88),
            AutoSize = false,
            Location = new Point(35, 160),
            Size = new Size(430, 110)
        };
        Button closeButton = new()
        {
            Text = Localization.T("Close"),
            DialogResult = DialogResult.OK,
            MinimumSize = new Size(96, 42),
            Size = new Size(96, 42),
            Location = new Point(369, 332)
        };

        Controls.Add(logo);
        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(versionLabel);
        Controls.Add(details);
        Controls.Add(closeButton);
        AcceptButton = closeButton;
        CancelButton = closeButton;
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
