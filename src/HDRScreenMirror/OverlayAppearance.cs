using System.Globalization;

namespace HDRScreenMirror;

internal sealed record OverlayAppearance(
    double Opacity,
    double Scale,
    bool GlassEnabled,
    Color BackgroundColor,
    Color AccentColor)
{
    public const double DefaultOpacity = 0.82;
    public const double MinOpacity = 0.40;
    public const double MaxOpacity = 1.00;
    public const double DefaultScale = 1.00;
    public const double MinScale = 0.50;
    public const double MaxScale = 1.50;
    public const bool DefaultGlassEnabled = true;
    public const string DefaultBackgroundHex = "#111318";
    public const string DefaultAccentHex = "#78B2FF";

    public static readonly (string NameKey, string Hex)[] BackgroundPresets =
    [
        ("OverlayBgObsidian", "#111318"),
        ("OverlayBgPureBlack", "#09090B"),
        ("OverlayBgGraphite", "#1C1F26"),
        ("OverlayBgNavy", "#0F172A"),
        ("OverlayBgLight", "#F4F4F5")
    ];

    public static readonly (string NameKey, string Hex)[] AccentPresets =
    [
        ("OverlayAccentBlue", "#78B2FF"),
        ("OverlayAccentEmerald", "#3FD184"),
        ("OverlayAccentAmber", "#FFC738"),
        ("OverlayAccentViolet", "#C084FC"),
        ("OverlayAccentMono", "#D4D4D8")
    ];

    public bool IsLightSurface =>
        0.2126 * BackgroundColor.R + 0.7152 * BackgroundColor.G + 0.0722 * BackgroundColor.B >= 150.0;

    public Color PrimaryTextColor => IsLightSurface
        ? Color.FromArgb(9, 9, 11)
        : Color.FromArgb(243, 245, 248);

    public Color SecondaryTextColor => IsLightSurface
        ? Color.FromArgb(63, 63, 70)
        : Color.FromArgb(176, 186, 202);

    public Color MutedTextColor => IsLightSurface
        ? Color.FromArgb(98, 98, 108)
        : Color.FromArgb(132, 144, 162);

    public Color BorderColor => IsLightSurface
        ? Color.FromArgb(46, 0, 0, 0)
        : Color.FromArgb(52, 255, 255, 255);

    public Color DividerColor => IsLightSurface
        ? Color.FromArgb(26, 0, 0, 0)
        : Color.FromArgb(28, 255, 255, 255);

    public Color PlotBackgroundColor => IsLightSurface
        ? Color.FromArgb(24, 24, 27)
        : Color.FromArgb(11, 12, 16);

    public Color EffectiveAccentColor
    {
        get
        {
            if (!IsLightSurface)
                return AccentColor;

            double luma = 0.2126 * AccentColor.R + 0.7152 * AccentColor.G + 0.0722 * AccentColor.B;
            if (luma < 150.0)
                return AccentColor;

            return Color.FromArgb(
                (int)Math.Round(AccentColor.R * 0.55),
                (int)Math.Round(AccentColor.G * 0.55),
                (int)Math.Round(AccentColor.B * 0.55));
        }
    }

    public static OverlayAppearance Default => FromSettings(
        DefaultOpacity,
        DefaultScale,
        DefaultGlassEnabled,
        DefaultBackgroundHex,
        DefaultAccentHex);

    public static OverlayAppearance FromSettings(
        double opacity,
        double scale,
        bool glassEnabled,
        string? backgroundHex,
        string? accentHex) =>
        new(
            Math.Clamp(double.IsFinite(opacity) ? opacity : DefaultOpacity, MinOpacity, MaxOpacity),
            Math.Clamp(double.IsFinite(scale) ? scale : DefaultScale, MinScale, MaxScale),
            glassEnabled,
            ParseHex(backgroundHex, Color.FromArgb(17, 19, 24)),
            ParseHex(accentHex, Color.FromArgb(120, 178, 255)));

    public static Color ParseHex(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        string trimmed = hex.Trim();
        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..];
        if (trimmed.Length != 6 ||
            !int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            return fallback;
        }

        return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
