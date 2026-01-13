using System;
using System.IO;
using System.Text.Json;

namespace NugetManagerGUI;

public class Settings
{
    public string? FeedUrl { get; set; }
    public string? ApiKey { get; set; }

    private static string GetConfigPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NugetManagerGUI");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public void Save()
    {
        var path = GetConfigPath();
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(this, options));
    }

    public static Settings Load()
    {
        var path = GetConfigPath();
        if (!File.Exists(path))
            return new Settings();

        try
        {
            var txt = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<Settings>(txt);
            return cfg ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }
}
