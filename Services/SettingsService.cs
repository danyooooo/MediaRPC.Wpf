using MediaRPC.Models;
using System.IO;
using System.Text.Json;

namespace MediaRPC.Services;

/// <summary>
/// Manages application settings persistence.
/// </summary>
public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings;

    public SettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MediaRPC"
        );
        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "settings.json");
        _settings = Load();
    }

    public AppSettings Settings => _settings;

    public bool RunAtStartup
    {
        get => _settings.RunAtStartup;
        set
        {
            if (_settings.RunAtStartup != value)
            {
                _settings.RunAtStartup = value;
                Save();
            }
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // If loading fails, return default settings
        }
        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Silently fail if save fails
        }
    }
}
