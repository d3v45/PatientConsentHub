using System.IO;
using PatientConsentHub.Services.Logging;

namespace PatientConsentHub.Services.Storage;

public sealed record StorageCheck(bool Ok, string? FriendlyMessage, long FreeBytes = 0);

/// <summary>
/// Owns everything about where recordings live: the patient-wise folder
/// structure, safe file names, free-space checks, and post-save verification.
/// </summary>
public static class StorageManager
{
    private static readonly string[] ReservedNames =
    {
        "CON","PRN","AUX","NUL",
        "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
    };

    /// <summary>
    /// Converts anything a doctor might type into a valid, stable folder name.
    /// "PATIENT/001" becomes "PATIENT_001" instead of throwing.
    /// The same input always produces the same folder, which is what keeps one
    /// patient's recordings together.
    /// </summary>
    public static string SanitizePatientId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var trimmed = raw.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var chars = trimmed.Select(c => invalid.Contains(c) || c is '.' or ' ' ? '_' : c).ToArray();
        var cleaned = new string(chars);

        while (cleaned.Contains("__")) cleaned = cleaned.Replace("__", "_");
        cleaned = cleaned.Trim('_');

        if (cleaned.Length > 64) cleaned = cleaned[..64];

        if (ReservedNames.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
            cleaned = "_" + cleaned;

        return cleaned;
    }

    public static string GetPatientFolder(string recordingRoot, string sanitizedPatientId)
    {
        var folder = Path.Combine(recordingRoot, sanitizedPatientId);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Consent_2026-09-19_18-45-32.mp4 - generated, never typed.</summary>
    public static string BuildFileName(string prefix, DateTime timestamp, string extension = ".mp4")
        => $"{prefix}_{timestamp:yyyy-MM-dd_HH-mm-ss}{extension}";

    /// <summary>
    /// Guarantees a unique path. Recordings are never overwritten: if a file
    /// with the same second-level timestamp somehow exists, a counter is added.
    /// </summary>
    public static string EnsureUniquePath(string desiredPath)
    {
        if (!File.Exists(desiredPath)) return desiredPath;

        var dir = Path.GetDirectoryName(desiredPath)!;
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = Path.GetExtension(desiredPath);

        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(dir, $"{name}_{Guid.NewGuid():N}{ext}");
    }

    /// <summary>Checks the storage location exists, is writable, and has room.</summary>
    public static StorageCheck CheckBeforeRecording(string recordingRoot, int lowSpaceGb)
    {
        try
        {
            Directory.CreateDirectory(recordingRoot);

            // Prove we can actually write there, rather than assuming.
            var probe = Path.Combine(recordingRoot, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Recording location is not writable.", ex);
            return new StorageCheck(false,
                "The recording location cannot be used. Please check the folder in Settings and try again.");
        }

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(recordingRoot));
            if (string.IsNullOrEmpty(root)) return new StorageCheck(true, null);

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                return new StorageCheck(false,
                    "The storage location is not available right now. Please check the drive or network connection.");

            var free = drive.AvailableFreeSpace;
            var threshold = (long)lowSpaceGb * 1024L * 1024L * 1024L;

            if (free < threshold)
            {
                AppLogger.Warn($"Low storage space: {free / (1024 * 1024)} MB free.");
                return new StorageCheck(false,
                    $"There may not be enough storage space to complete this recording. " +
                    $"About {free / (1024L * 1024L * 1024L)} GB is free.", free);
            }

            return new StorageCheck(true, null, free);
        }
        catch (Exception ex)
        {
            // A network share may not report free space. Do not block the doctor.
            AppLogger.Warn("Free space could not be determined: " + ex.Message);
            return new StorageCheck(true, null);
        }
    }

    /// <summary>
    /// Confirms a file really made it to disk with content in it. Nothing is
    /// ever reported as saved on the strength of the process exit code alone.
    /// </summary>
    public static bool VerifySaved(string path, out long sizeBytes)
    {
        sizeBytes = 0;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return false;
            info.Refresh();
            sizeBytes = info.Length;
            return sizeBytes > 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Could not verify the saved recording.", ex);
            return false;
        }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Could not open the folder.", ex);
        }
    }

    public static void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Could not open the recording.", ex);
        }
    }
}
