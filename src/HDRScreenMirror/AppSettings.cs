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

    public static AppSettings Load()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
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
