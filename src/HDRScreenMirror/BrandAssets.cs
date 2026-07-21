using System.Reflection;

namespace HDRScreenMirror;

internal static class BrandAssets
{
    private const string LogoResourceName = "HDRScreenMirror.Assets.logo-hdrscreenmirror.png";

    public static Icon LoadApplicationIcon() =>
        System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ??
        (Icon)SystemIcons.Application.Clone();

    public static Image LoadLogoImage()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(LogoResourceName);
        if (stream is null)
            return SystemIcons.Application.ToBitmap();

        using Image source = Image.FromStream(stream);
        return new Bitmap(source);
    }
}
