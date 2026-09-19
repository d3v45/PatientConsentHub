namespace PatientConsentHub.Models;

/// <summary>What the doctor chose on the main screen.</summary>
public enum ConsentType
{
    ConsentView = 0,
    ConsentViewPlusScreen = 1
}

public static class ConsentTypeText
{
    public static string Display(ConsentType t) =>
        t == ConsentType.ConsentViewPlusScreen ? "Consent View + Screen" : "Consent View";
}

/// <summary>A capture device as Windows/DirectShow reports it.</summary>
public sealed class CaptureDevice
{
    /// <summary>DirectShow friendly name. This is what FFmpeg needs, verbatim.</summary>
    public string Name { get; init; } = "";

    /// <summary>Stable "@device_pnp_..." moniker, used to remember a device across reboots.</summary>
    public string? AlternativeName { get; init; }

    /// <summary>Enumeration order, used by the OpenCV preview.</summary>
    public int Index { get; init; }

    public override string ToString() => Name;
}

public sealed class DisplayDevice
{
    public string Name { get; init; } = "";
    public int Index { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsPrimary { get; init; }

    public string Display => IsPrimary
        ? $"{Name} ({Width}x{Height}, main display)"
        : $"{Name} ({Width}x{Height})";

    public override string ToString() => Display;
}

/// <summary>Persisted application settings (hospital admin controls these).</summary>
public sealed class AppSettings
{
    public string RecordingRoot { get; set; } = @"C:\Patient Consent Hub\Recordings";
    public string? DefaultCameraName { get; set; }
    public string? DefaultMicrophoneName { get; set; }
    public int DefaultScreenIndex { get; set; }
    public bool ConfirmBeforeStop { get; set; } = true;
    public int LowDiskWarningGb { get; set; } = 5;
    public int ScreenFrameRate { get; set; } = 15;
    public int CameraFrameRate { get; set; } = 30;
}

/// <summary>One completed recording, as shown in Recording History.</summary>
public sealed class RecordingSession
{
    public string PatientId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public int DurationSeconds { get; set; }
    public ConsentType Type { get; set; }
    public string CameraFile { get; set; } = "";
    public string? ScreenFile { get; set; }
    public string FolderPath { get; set; } = "";

    public string DateText => StartedAt.ToString("dd MMM yyyy");
    public string TimeText => StartedAt.ToString("HH:mm");
    public string TypeText => ConsentTypeText.Display(Type);
    public string DurationText => TimeSpan.FromSeconds(DurationSeconds).ToString(@"hh\:mm\:ss");
}

/// <summary>Outcome of a stop-and-finalise operation.</summary>
public sealed class SaveResult
{
    public bool Success { get; init; }
    public string? FriendlyError { get; init; }
    public RecordingSession? Session { get; init; }

    public static SaveResult Ok(RecordingSession s) => new() { Success = true, Session = s };
    public static SaveResult Fail(string message) => new() { Success = false, FriendlyError = message };
}
