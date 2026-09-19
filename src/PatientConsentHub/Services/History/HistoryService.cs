using System.IO;
using System.Text.Json;
using PatientConsentHub.Models;
using PatientConsentHub.Services.Logging;
using PatientConsentHub.Services.Settings;

namespace PatientConsentHub.Services.History;

/// <summary>
/// Keeps a local index of completed sessions for the Recording History screen.
/// Stored locally only - nothing is ever sent anywhere.
/// </summary>
public sealed class HistoryService
{
    private const int MaxEntries = 500;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly object _gate = new();

    private static string HistoryFile =>
        Path.Combine(SettingsManager.ConfigDirectory, "history.json");

    public List<RecordingSession> Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(HistoryFile)) return new List<RecordingSession>();
                var list = JsonSerializer.Deserialize<List<RecordingSession>>(File.ReadAllText(HistoryFile));
                return list?.OrderByDescending(s => s.StartedAt).ToList() ?? new List<RecordingSession>();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Could not read the recording history.", ex);
                return new List<RecordingSession>();
            }
        }
    }

    public void Add(RecordingSession session)
    {
        lock (_gate)
        {
            try
            {
                var list = File.Exists(HistoryFile)
                    ? JsonSerializer.Deserialize<List<RecordingSession>>(File.ReadAllText(HistoryFile))
                      ?? new List<RecordingSession>()
                    : new List<RecordingSession>();

                list.Add(session);

                if (list.Count > MaxEntries)
                    list = list.OrderByDescending(s => s.StartedAt).Take(MaxEntries).ToList();

                Directory.CreateDirectory(SettingsManager.ConfigDirectory);
                File.WriteAllText(HistoryFile, JsonSerializer.Serialize(list, JsonOpts));
            }
            catch (Exception ex)
            {
                // A history write failure must never look like a save failure.
                AppLogger.Error("Could not update the recording history.", ex);
            }
        }
    }
}
