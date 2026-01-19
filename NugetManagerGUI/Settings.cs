using System;
using System.IO;
using System.Text.Json;

namespace NugetManagerGUI;

public class Settings
{
    public string FeedUrl { get; set; }
    public string? ApiKey { get; set; }

    private static string? _path;
    private static string GetConfigPath()
    {
        if (_path is null)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NugetManagerGUI");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "settings.json");
        }

        return _path;
    }

    public void Save()
    {
        var path = GetConfigPath();
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(_instance, options));
        _instance = this;
    }

    private static Settings? _instance;
    public static Settings? Load()
    {
        if (_instance is not null)
            return _instance;

        var path = GetConfigPath();
        if (!File.Exists(path))
            return null;

        try
        {
            var txt = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<Settings>(txt);
            _instance = cfg;
            return cfg ?? null;
        }
        catch
        {
            return null;
        }
    }
}
