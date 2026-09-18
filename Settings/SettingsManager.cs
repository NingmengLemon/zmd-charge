using System.Text.Json;

namespace EndfieldCharge.Settings;

public static class SettingsManager
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EndfieldCharge");

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // 保存失败静默忽略
        }
    }
}