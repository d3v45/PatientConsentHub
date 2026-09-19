using PatientConsentHub.Models;

namespace PatientConsentHub.Services.Recording;

public sealed class RecordingRequest
{
    public required string PatientId { get; init; }            // already sanitised
    public required ConsentType Type { get; init; }
    public required string CameraName { get; init; }
    public required string MicrophoneName { get; init; }
    public DisplayDevice? Screen { get; init; }                // required for Consent View + Screen
    public required string RecordingRoot { get; init; }
    public int CameraFps { get; init; } = 30;
    public int ScreenFps { get; init; } = 15;
}

public sealed class RecordingStartResult
{
    public bool Success { get; init; }
    public string? FriendlyError { get; init; }

    public static RecordingStartResult Ok() => new() { Success = true };
    public static RecordingStartResult Fail(string msg) => new() { Success = false, FriendlyError = msg };
}

/// <summary>
/// Captures camera, microphone and (optionally) the screen, then finalises the
/// files. Implementations must never throw at the UI - they return friendly
/// results and write technical detail to the log.
/// </summary>
public interface IRecordingEngine
{
    bool IsRecording { get; }
    DateTime? StartedAt { get; }

    /// <summary>Raised if capture stops on its own (device unplugged, disk lost).</summary>
    event Action<string>? CaptureFailed;

    RecordingStartResult Start(RecordingRequest request);

    /// <summary>Stops, finalises and verifies. Never reports success unless the file is really on disk.</summary>
    Task<SaveResult> StopAsync();

    /// <summary>Best-effort stop used when the window is closing.</summary>
    void AbortQuietly();
}
