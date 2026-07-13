using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrammarFixer.Models;

namespace GrammarFixer.Services;

public static class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GrammarFixer");
    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();
        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            settings.AllowedApps ??= new List<string>();
            settings.DeniedApps ??= new List<string> { "GrammarFixer", "devenv", "rider" };
            return settings;
        }
        catch { return new AppSettings(); }
    }

    public static void Save(AppSettings settings)
    {
        settings.AllowedApps ??= new List<string>();
        settings.DeniedApps ??= new List<string>();
        Directory.CreateDirectory(SettingsDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
    }
}
