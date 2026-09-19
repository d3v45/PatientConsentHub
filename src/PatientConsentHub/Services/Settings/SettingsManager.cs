using System.IO;
using System.Text.Json;
using PatientConsentHub.Models;
using PatientConsentHub.Services.Logging;

namespace PatientConsentHub.Services.Settings;

public sealed class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "A&T", "Patient Consent Hub");

    private string ConfigFile => Path.Combine(ConfigDirectory, "settings.json");

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);
                Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                AppLogger.Info("Settings loaded.");
            }
            else
            {
                Current = new AppSettings();
                Save();
                AppLogger.Info("Default settings created.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Could not read settings; using defaults.", ex);
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(Current, JsonOpts));
        }
        catch (Exception ex)
        {
            AppLogger.Error("Could not save settings.", ex);
            throw;
        }
    }
}
