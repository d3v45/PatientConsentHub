using System.Text.RegularExpressions;
using PatientConsentHub.Models;
using PatientConsentHub.Services.Logging;

namespace PatientConsentHub.Services.Devices;

/// <summary>
/// Discovers the cameras and microphones Windows exposes through DirectShow.
///
/// Enumeration goes through FFmpeg on purpose: the names it reports are exactly
/// the names it later needs to open the device, so there is no name-matching
/// guesswork at the moment that matters (the start of a recording).
/// </summary>
public sealed class DeviceManager
{
    // Matches a device line regardless of which tag ffmpeg puts in the
    // brackets - different builds print "[dshow @ 0x...]", "[in#0 @ 0x...]",
    // or similar. What is reliable across builds is the quoted name followed
    // by "(video)", "(audio)" or "(none)" at the end of the line.
    private static readonly Regex DeviceLine =
        new(@"^\[[^\]]*\]\s*""(?<name>[^""]+)""\s*\((?<kind>video|audio|none)\)\s*$",
            RegexOptions.Compiled);

    private static readonly Regex AltNameLine =
        new(@"^\[[^\]]*\]\s*Alternative name\s*""(?<alt>[^""]+)""\s*$", RegexOptions.Compiled);

    public IReadOnlyList<CaptureDevice> Cameras { get; private set; } = Array.Empty<CaptureDevice>();
    public IReadOnlyList<CaptureDevice> Microphones { get; private set; } = Array.Empty<CaptureDevice>();

    public void Refresh()
    {
        var cameras = new List<CaptureDevice>();
        var mics = new List<CaptureDevice>();

        try
        {
            // FFmpeg exits non-zero here by design; the device list is on stderr.
            var (_, output) = FfmpegRunner.Run(new[]
            {
                "-list_devices", "true", "-f", "dshow", "-i", "dummy"
            });

            string? pendingName = null;
            string? pendingKind = null;

            void Flush(string? alt)
            {
                if (pendingName is null) return;

                // "(none)" shows up for a handful of virtual cameras whose
                // driver does not report a type to dshow explicitly; every
                // microphone/line-in ffmpeg has ever been seen to report
                // says "(audio)" outright, so treating "(none)" as video is
                // the safe default rather than risking a camera being lost.
                var isAudio = pendingKind == "audio";
                var list = isAudio ? mics : cameras;

                list.Add(new CaptureDevice
                {
                    Name = pendingName,
                    AlternativeName = alt,
                    Index = list.Count
                });

                pendingName = null;
                pendingKind = null;
            }

            foreach (var raw in output.Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();

                var alt = AltNameLine.Match(line);
                if (alt.Success)
                {
                    Flush(alt.Groups["alt"].Value);
                    continue;
                }

                var dev = DeviceLine.Match(line);
                if (dev.Success)
                {
                    Flush(null);   // the previous device had no alternative-name line
                    pendingName = dev.Groups["name"].Value;
                    pendingKind = dev.Groups["kind"].Value;
                }
            }

            Flush(null);
        }
        catch (FfmpegNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Device enumeration failed.", ex);
        }

        Cameras = cameras;
        Microphones = mics;
        AppLogger.Info($"Device scan complete: {cameras.Count} camera(s), {mics.Count} microphone(s).");
    }

    /// <summary>
    /// Picks a device by remembered name, falling back to the first available
    /// one. Returns whether the remembered device was actually found so the
    /// caller can tell the user a substitution happened.
    /// </summary>
    public static (CaptureDevice? Device, bool UsedFallback) Resolve(
        IReadOnlyList<CaptureDevice> available, string? preferredName)
    {
        if (available.Count == 0) return (null, false);

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var match = available.FirstOrDefault(d =>
                string.Equals(d.Name, preferredName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return (match, false);
            return (available[0], true);
        }

        return (available[0], false);
    }
}

/// <summary>Resolution/frame-rate a camera can actually deliver.</summary>
public sealed record CameraMode(int Width, int Height, double MaxFps);

/// <summary>
/// Asks the camera what it supports so the app can choose 1080p or 720p by
/// itself. The doctor is never shown resolutions, codecs or frame rates.
/// </summary>
public static class CameraCapabilities
{
    private static readonly Regex MaxMode = new(
        @"max s=(?<w>\d+)x(?<h>\d+)\s+fps=(?<fps>[\d.]+)", RegexOptions.Compiled);

    private static readonly Dictionary<string, CameraMode?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the best mode to request, or null to let FFmpeg negotiate
    /// defaults (which is the safest option for unusual devices).
    /// </summary>
    public static CameraMode? ChooseMode(string cameraName, int desiredFps)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(cameraName, out var cached)) return cached;
        }

        CameraMode? chosen = null;

        try
        {
            var (_, output) = FfmpegRunner.Run(new[]
            {
                "-list_options", "true", "-f", "dshow", "-i", $"video={cameraName}"
            });

            var modes = new List<CameraMode>();
            foreach (Match m in MaxMode.Matches(output))
            {
                var w = int.Parse(m.Groups["w"].Value);
                var h = int.Parse(m.Groups["h"].Value);
                var fps = double.Parse(m.Groups["fps"].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                modes.Add(new CameraMode(w, h, fps));
            }

            if (modes.Count > 0)
            {
                // 1080p if the camera really offers it, else 720p, else the
                // largest mode that is not absurdly high resolution.
                chosen =
                    modes.FirstOrDefault(m => m is { Width: 1920, Height: 1080 } && m.MaxFps >= desiredFps - 1)
                    ?? modes.FirstOrDefault(m => m is { Width: 1920, Height: 1080 })
                    ?? modes.FirstOrDefault(m => m is { Width: 1280, Height: 720 })
                    ?? modes.Where(m => m.Width <= 1920)
                            .OrderByDescending(m => (long)m.Width * m.Height)
                            .FirstOrDefault();

                AppLogger.Info(chosen is null
                    ? "No usable camera mode reported; FFmpeg defaults will be used."
                    : $"Camera mode selected: {chosen.Width}x{chosen.Height} @ up to {chosen.MaxFps} fps.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Could not query camera capabilities; defaults will be used. " + ex.Message);
        }

        lock (Cache) { Cache[cameraName] = chosen; }
        return chosen;
    }

    public static void Invalidate()
    {
        lock (Cache) { Cache.Clear(); }
    }
}
