using System.IO;
using System.Text.Json;
using PatientConsentHub.Models;
using PatientConsentHub.Services.Devices;
using PatientConsentHub.Services.Logging;
using PatientConsentHub.Services.Settings;
using PatientConsentHub.Services.Storage;

namespace PatientConsentHub.Services.Recovery;

public sealed class RecoveryInfo
{
    public string PatientId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public ConsentType ConsentType { get; set; }
    public string PatientFolder { get; set; } = "";
    public string CameraPartial { get; set; } = "";
    public string CameraFinal { get; set; } = "";
    public string? ScreenPartial { get; set; }
    public string? ScreenFinal { get; set; }
}

/// <summary>
/// If Windows or the application stops during a recording, a marker file is
/// left behind pointing at the captured data. Because capture writes
/// fragmented MP4, that data is still usable - it only needs remuxing.
///
/// Nothing here ever deletes a recording without the user asking for it.
/// </summary>
public static class RecoveryManager
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string MarkerFile =>
        Path.Combine(SettingsManager.ConfigDirectory, "unfinished-recording.json");

    public static void WriteMarker(RecoveryInfo info)
    {
        try
        {
            Directory.CreateDirectory(SettingsManager.ConfigDirectory);
            File.WriteAllText(MarkerFile, JsonSerializer.Serialize(info, JsonOpts));
        }
        catch (Exception ex)
        {
            AppLogger.Error("Could not write the recovery marker.", ex);
        }
    }

    public static void ClearMarker()
    {
        try { if (File.Exists(MarkerFile)) File.Delete(MarkerFile); }
        catch (Exception ex) { AppLogger.Error("Could not clear the recovery marker.", ex); }
    }

    /// <summary>Returns pending recovery data, but only if captured data actually exists.</summary>
    public static RecoveryInfo? FindPending()
    {
        try
        {
            if (!File.Exists(MarkerFile)) return null;

            var info = JsonSerializer.Deserialize<RecoveryInfo>(File.ReadAllText(MarkerFile));
            if (info is null) { ClearMarker(); return null; }

            var cameraOk = File.Exists(info.CameraPartial) && new FileInfo(info.CameraPartial).Length > 0;
            var screenOk = info.ScreenPartial is not null
                           && File.Exists(info.ScreenPartial)
                           && new FileInfo(info.ScreenPartial).Length > 0;

            if (!cameraOk && !screenOk)
            {
                AppLogger.Info("A recovery marker was found but no captured data remained.");
                ClearMarker();
                return null;
            }

            AppLogger.Info("An unfinished recording was detected.");
            return info;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Recovery check failed.", ex);
            return null;
        }
    }

    /// <summary>Repairs the captured data into normal, playable recordings.</summary>
    public static async Task<bool> RecoverAsync(RecoveryInfo info)
    {
        var anySucceeded = false;

        if (File.Exists(info.CameraPartial))
            anySucceeded |= await RemuxAsync(info.CameraPartial, info.CameraFinal);

        if (info.ScreenPartial is not null && info.ScreenFinal is not null && File.Exists(info.ScreenPartial))
            anySucceeded |= await RemuxAsync(info.ScreenPartial, info.ScreenFinal);

        if (anySucceeded) ClearMarker();
        return anySucceeded;
    }

    /// <summary>Discards the recovery data. Only ever called on an explicit user choice.</summary>
    public static void Discard(RecoveryInfo info)
    {
        try
        {
            if (File.Exists(info.CameraPartial)) File.Delete(info.CameraPartial);
            if (info.ScreenPartial is not null && File.Exists(info.ScreenPartial))
                File.Delete(info.ScreenPartial);
            AppLogger.Info("Recovery data deleted at the user's request.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Could not delete the recovery data.", ex);
        }
        finally
        {
            ClearMarker();
        }
    }

    private static async Task<bool> RemuxAsync(string partial, string final)
    {
        return await Task.Run(() =>
        {
            try
            {
                var target = StorageManager.EnsureUniquePath(final);
                var (exit, _) = FfmpegRunner.Run(new[]
                {
                    "-nostdin", "-y", "-i", partial,
                    "-c", "copy", "-movflags", "+faststart", target
                }, timeoutMs: 300_000);

                if (exit == 0 && StorageManager.VerifySaved(target, out var size) && size > 0)
                {
                    try { File.Delete(partial); } catch { /* keep it if locked */ }
                    AppLogger.Info("An unfinished recording was recovered successfully.");
                    return true;
                }

                AppLogger.Error("Recovery could not produce a valid recording.");
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Recovery failed.", ex);
                return false;
            }
        });
    }
}
