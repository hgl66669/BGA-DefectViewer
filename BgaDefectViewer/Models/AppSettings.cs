using System.IO;
using System.Text.Json;

namespace BgaDefectViewer.Models;

public class AppSettings
{
    public string AthleteSysPath { get; set; } = "";
    public string LastPartNumber { get; set; } = "";
    public string LastLotNumber { get; set; } = "";

    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "BgaDefectViewer", "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
