using System.Diagnostics;
using System.IO;
using System.Text;
using PatientConsentHub.Services.Logging;

namespace PatientConsentHub.Services.Devices;

public sealed class FfmpegNotFoundException : Exception
{
    public FfmpegNotFoundException()
        : base("The recording component was not found. Please reinstall Patient Consent Hub.") { }
}

/// <summary>
/// Finds the FFmpeg binary that ships inside the installation folder.
/// The doctor never installs anything - it is packaged with the app.
/// </summary>
public static class FfmpegLocator
{
    private static string? _cached;

    public static string Path
    {
        get
        {
            if (_cached is not null) return _cached;

            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                System.IO.Path.Combine(baseDir, "Tools", "ffmpeg", "ffmpeg.exe"),
                System.IO.Path.Combine(baseDir, "ffmpeg.exe")
            };

            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    _cached = c;
                    AppLogger.Info($"Recording component located at: {c}");
                    return _cached;
                }
            }

            AppLogger.Error("ffmpeg.exe was not found beside the application.");
            throw new FfmpegNotFoundException();
        }
    }

    public static bool IsAvailable
    {
        get
        {
            try { _ = Path; return true; }
            catch { return false; }
        }
    }
}

/// <summary>Runs short-lived FFmpeg commands and captures their output.</summary>
public static class FfmpegRunner
{
    /// <summary>
    /// Runs FFmpeg to completion. FFmpeg writes its informational output to
    /// stderr, which is what the device/capability parsers read.
    /// </summary>
    public static (int ExitCode, string StdErr) Run(IEnumerable<string> args, int timeoutMs = 20000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FfmpegLocator.Path,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8
        };

        psi.ArgumentList.Add("-hide_banner");
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        var sb = new StringBuilder();

        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        proc.OutputDataReceived += (_, _) => { };

        proc.Start();
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();

        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            AppLogger.Warn("A device query timed out and was cancelled.");
        }

        return (proc.HasExited ? proc.ExitCode : -1, sb.ToString());
    }
}
