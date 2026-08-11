using System.Text.Json;

namespace HDRScreenMirror;

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public bool MinimizeToTrayOnClose { get; set; }
    public string Language { get; set; } = Localization.Automatic;
    public string ScreenshotSaveMode { get; set; } = "automatic";
    public string ScreenshotDirectory { get; set; } = string.Empty;
    public string FrameRateMode { get; set; } = "output";
    public int FrameRateLimit { get; set; } = 60;
    public string OperationMode { get; set; } = "mirror";
    public string CaptureDisplayId { get; set; } = string.Empty;
    public string OutputDisplayId { get; set; } = string.Empty;
    public bool AblEstimationEnabled { get; set; }
    public string ActiveAblProfileId { get; set; } = string.Empty;
    public List<AblProfile> AblProfiles { get; set; } = [];

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

    public void Save()
    {
        try
        {
            string path = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
