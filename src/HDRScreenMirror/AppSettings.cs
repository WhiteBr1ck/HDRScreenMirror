using System.Text.Json;

namespace HDRScreenMirror;

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public bool MinimizeToTrayOnClose { get; set; }
    public string Language { get; set; } = Localization.Automatic;
    public string UiTheme { get; set; } = "auto";
    public string ScreenshotSaveMode { get; set; } = "automatic";
    public string ScreenshotDirectory { get; set; } = string.Empty;
    public bool CopyScreenshotToClipboard { get; set; } = true;
    public double OverlayOpacity { get; set; } = OverlayAppearance.DefaultOpacity;
    public double OverlayScale { get; set; } = OverlayAppearance.DefaultScale;
    public bool OverlayGlassEnabled { get; set; } = OverlayAppearance.DefaultGlassEnabled;
    public int OverlayUpdateHz { get; set; } = 4;
    public string OverlayBackgroundColor { get; set; } = OverlayAppearance.DefaultBackgroundHex;
    public string OverlayAccentColor { get; set; } = OverlayAppearance.DefaultAccentHex;
    public string FrameRateMode { get; set; } = "output";
    public int FrameRateLimit { get; set; } = 60;
    public string OperationMode { get; set; } = "mirror";
    public string CaptureDisplayId { get; set; } = string.Empty;
    public string OutputDisplayId { get; set; } = string.Empty;
    public bool AblEstimationEnabled { get; set; }
    public string ActiveAblProfileId { get; set; } = string.Empty;
    public List<AblProfile> AblProfiles { get; set; } = [];

    public OverlayAppearance GetOverlayAppearance() =>
        OverlayAppearance.FromSettings(
            OverlayOpacity,
            OverlayScale,
            OverlayGlassEnabled,
            OverlayBackgroundColor,
            OverlayAccentColor);

    public static AppSettings Load()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
                return new AppSettings();

            AppSettings settings =
                JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            settings.NormalizeAblProfiles();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void NormalizeAblProfiles()
    {
        AblProfiles ??= [];
        foreach (AblProfile profile in AblProfiles)
            profile.Normalize();

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (AblProfile profile in AblProfiles)
        {
            if (!ids.Add(profile.Id))
                profile.Id = Guid.NewGuid().ToString("N");
            ids.Add(profile.Id);
        }

        AblProfile? active = AblProfiles.FirstOrDefault(profile =>
            profile.IsValid && string.Equals(profile.Id, ActiveAblProfileId, StringComparison.Ordinal));
        active ??= AblProfiles.FirstOrDefault(profile => profile.IsValid);
        ActiveAblProfileId = active?.Id ?? string.Empty;
        if (active is null)
            AblEstimationEnabled = false;
    }

    public void Save(bool allowClearingAblProfiles = false)
    {
        try
        {
            string path = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (File.Exists(path))
            {
                if (!allowClearingAblProfiles && AblProfiles.Count == 0)
                {
                    try
                    {
                        AppSettings? existing = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                        if (existing?.AblProfiles is { Count: > 0 } existingProfiles)
                        {
                            AblProfiles = existingProfiles;
                            if (string.IsNullOrWhiteSpace(ActiveAblProfileId))
                                ActiveAblProfileId = existing.ActiveAblProfileId;
                        }
                    }
                    catch
                    {
                    }
                }

                try
                {
                    File.Copy(path, path + ".bak", overwrite: true);
                }
                catch
                {
                }
            }

            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, SerializerOptions));
            File.Move(temporaryPath, path, true);
        }
        catch
        {
        }
    }

    private static string GetSettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HDRScreenMirror",
        "settings.json");
}
