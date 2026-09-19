using System.Diagnostics;
using System.IO;
using System.Text;
using PatientConsentHub.Models;
using PatientConsentHub.Services.Devices;
using PatientConsentHub.Services.Logging;
using PatientConsentHub.Services.Recovery;
using PatientConsentHub.Services.Storage;

namespace PatientConsentHub.Services.Recording;

/// <summary>
/// Capture is delegated to FFmpeg: DirectShow for camera + microphone, GDI for
/// the screen. Both outputs come from one process, so they start together and
/// share the same audio stream - that is what keeps them in sync.
///
/// Data-safety design: capture writes fragmented MP4 ".partial" files, which
/// remain playable even if Windows or the app dies mid-session. A normal stop
/// remuxes them (stream copy, no re-encode) into clean, seekable MP4s.
/// </summary>
public sealed class FfmpegRecordingEngine : IRecordingEngine
{
    private Process? _proc;
    private RecordingRequest? _request;
    private string _cameraPartial = "";
    private string _cameraFinal = "";
    private string? _screenPartial;
    private string? _screenFinal;
    private string _patientFolder = "";
    private DateTime _startedAt;
    private volatile bool _stopRequested;
    private readonly StringBuilder _recentErrors = new();
    private readonly object _gate = new();

    public bool IsRecording => _proc is { HasExited: false };
    public DateTime? StartedAt => IsRecording ? _startedAt : null;

    public event Action<string>? CaptureFailed;

    public RecordingStartResult Start(RecordingRequest request)
    {
        if (IsRecording) return RecordingStartResult.Fail("A recording is already in progress.");

        try
        {
            _request = request;
            _stopRequested = false;
            _recentErrors.Clear();
            _startedAt = DateTime.Now;

            _patientFolder = StorageManager.GetPatientFolder(request.RecordingRoot, request.PatientId);

            _cameraFinal = StorageManager.EnsureUniquePath(Path.Combine(
                _patientFolder, StorageManager.BuildFileName("Consent", _startedAt)));
            _cameraPartial = _cameraFinal + ".partial";

            if (request.Type == ConsentType.ConsentViewPlusScreen)
            {
                _screenFinal = StorageManager.EnsureUniquePath(Path.Combine(
                    _patientFolder, StorageManager.BuildFileName("ConsentScreen", _startedAt)));
                _screenPartial = _screenFinal + ".partial";
            }
            else
            {
                _screenFinal = null;
                _screenPartial = null;
            }

            var args = BuildArguments(request);

            var psi = new ProcessStartInfo
            {
                FileName = FfmpegLocator.Path,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,   // needed to send 'q' for a clean stop
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.ErrorDataReceived += OnFfmpegOutput;
            _proc.OutputDataReceived += (_, _) => { };
            _proc.Exited += OnProcessExited;

            _proc.Start();
            _proc.BeginErrorReadLine();
            _proc.BeginOutputReadLine();

            // If the camera or microphone cannot be opened at all, FFmpeg dies
            // within a second or two. Catch that here rather than showing a
            // "recording" screen that is recording nothing.
            if (_proc.WaitForExit(1500))
            {
                // Suppress the Exited handler: this path reports the failure itself.
                _stopRequested = true;
                var reason = InterpretFailure(_recentErrors.ToString());
                AppLogger.Error("Recording failed to start. FFmpeg exited immediately.");
                Cleanup();
                return RecordingStartResult.Fail(reason);
            }

            RecoveryManager.WriteMarker(new RecoveryInfo
            {
                PatientId = request.PatientId,
                StartedAt = _startedAt,
                ConsentType = request.Type,
                PatientFolder = _patientFolder,
                CameraPartial = _cameraPartial,
                CameraFinal = _cameraFinal,
                ScreenPartial = _screenPartial,
                ScreenFinal = _screenFinal
            });

            AppLogger.Info($"Recording started ({ConsentTypeText.Display(request.Type)}).");
            return RecordingStartResult.Ok();
        }
        catch (FfmpegNotFoundException ex)
        {
            AppLogger.Error("Recording component missing.", ex);
            Cleanup();
            return RecordingStartResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Recording could not be started.", ex);
            Cleanup();
            return RecordingStartResult.Fail(
                "Something went wrong while starting the recording. " +
                "Please check the camera and microphone and try again.");
        }
    }

    private List<string> BuildArguments(RecordingRequest r)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-y"
        };

        // ---- Input 0: camera + microphone (one DirectShow input keeps A/V aligned)
        var mode = CameraCapabilities.ChooseMode(r.CameraName, r.CameraFps);

        args.AddRange(new[] { "-f", "dshow" });
        args.AddRange(new[] { "-thread_queue_size", "1024" });
        args.AddRange(new[] { "-rtbufsize", "512M" });
        if (mode is not null)
        {
            args.AddRange(new[] { "-video_size", $"{mode.Width}x{mode.Height}" });
            var fps = Math.Min(r.CameraFps, (int)Math.Round(mode.MaxFps));
            if (fps >= 5) args.AddRange(new[] { "-framerate", fps.ToString() });
        }
        args.AddRange(new[] { "-i", $"video={r.CameraName}:audio={r.MicrophoneName}" });

        // ---- Input 1: screen, only when requested
        var withScreen = r.Type == ConsentType.ConsentViewPlusScreen && r.Screen is not null;
        if (withScreen)
        {
            var s = r.Screen!;
            var w = s.Width % 2 == 0 ? s.Width : s.Width - 1;
            var h = s.Height % 2 == 0 ? s.Height : s.Height - 1;

            args.AddRange(new[] { "-f", "gdigrab" });
            args.AddRange(new[] { "-thread_queue_size", "1024" });
            args.AddRange(new[] { "-framerate", r.ScreenFps.ToString() });
            args.AddRange(new[] { "-offset_x", s.X.ToString() });
            args.AddRange(new[] { "-offset_y", s.Y.ToString() });
            args.AddRange(new[] { "-video_size", $"{w}x{h}" });
            args.AddRange(new[] { "-i", "desktop" });
        }

        // ---- Output 1: the consent video (camera + audio)
        args.AddRange(new[] { "-map", "0:v:0", "-map", "0:a:0" });
        args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "23" });
        args.AddRange(new[] { "-pix_fmt", "yuv420p" });
        args.AddRange(new[] { "-g", (r.CameraFps * 2).ToString() });
        args.AddRange(new[] { "-c:a", "aac", "-b:a", "128k", "-ar", "48000", "-ac", "1" });
        args.AddRange(new[] { "-movflags", "+frag_keyframe+empty_moov+default_base_is_moof" });
        args.AddRange(new[] { "-f", "mp4" });   // extension is ".partial", so the format must be explicit
        args.Add(_cameraPartial);

        // ---- Output 2: the screen video, sharing the same audio track
        if (withScreen)
        {
            args.AddRange(new[] { "-map", "1:v:0", "-map", "0:a:0" });
            args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "26" });
            args.AddRange(new[] { "-pix_fmt", "yuv420p" });
            args.AddRange(new[] { "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2" });
            args.AddRange(new[] { "-g", (r.ScreenFps * 2).ToString() });
            args.AddRange(new[] { "-c:a", "aac", "-b:a", "128k", "-ar", "48000", "-ac", "1" });
            args.AddRange(new[] { "-movflags", "+frag_keyframe+empty_moov+default_base_is_moof" });
            args.AddRange(new[] { "-f", "mp4" });
            args.Add(_screenPartial!);
        }

        return args;
    }

    private void OnFfmpegOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;

        lock (_gate)
        {
            _recentErrors.AppendLine(e.Data);
            if (_recentErrors.Length > 8000) _recentErrors.Remove(0, 4000);
        }

        // Paths contain the patient ID, so they are stripped before logging.
        AppLogger.Warn("Capture: " + Redact(e.Data));
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_stopRequested) return;

        // Capture ended without anyone pressing Stop.
        var reason = InterpretFailure(_recentErrors.ToString());
        AppLogger.Error("Capture stopped unexpectedly.");
        CaptureFailed?.Invoke(reason);
    }

    public async Task<SaveResult> StopAsync()
    {
        var proc = _proc;
        var req = _request;
        if (proc is null || req is null)
            return SaveResult.Fail("There was no recording in progress.");

        _stopRequested = true;
        var duration = (int)Math.Max(0, (DateTime.Now - _startedAt).TotalSeconds);

        try
        {
            if (!proc.HasExited)
            {
                // 'q' asks FFmpeg to finish writing properly rather than being killed.
                try { await proc.StandardInput.WriteAsync("q"); await proc.StandardInput.FlushAsync(); }
                catch { /* the stream may already be gone */ }

                if (!await WaitForExitAsync(proc, TimeSpan.FromSeconds(20)))
                {
                    AppLogger.Warn("Capture did not finish in time; ending it directly.");
                    try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    await WaitForExitAsync(proc, TimeSpan.FromSeconds(5));
                }
            }

            AppLogger.Info($"Recording stopped after {duration} seconds.");

            // Finalise: stream-copy the crash-safe partials into clean MP4s.
            if (!await FinaliseAsync(_cameraPartial, _cameraFinal))
                return Failed();

            if (_screenPartial is not null && _screenFinal is not null)
            {
                if (!await FinaliseAsync(_screenPartial, _screenFinal))
                    return Failed();
            }

            RecoveryManager.ClearMarker();

            var session = new RecordingSession
            {
                PatientId = req.PatientId,
                StartedAt = _startedAt,
                DurationSeconds = duration,
                Type = req.Type,
                CameraFile = _cameraFinal,
                ScreenFile = _screenFinal,
                FolderPath = _patientFolder
            };

            AppLogger.Info("Recording saved and verified.");
            Cleanup();
            return SaveResult.Ok(session);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Finalising the recording failed.", ex);
            return Failed();
        }

        SaveResult Failed()
        {
            Cleanup();
            return SaveResult.Fail(
                "Recording could not be saved. Please check the storage location and try again.\n\n" +
                "The captured data has been kept in the patient folder so it can be recovered.");
        }
    }

    /// <summary>
    /// Remuxes a partial file into its final name and verifies the result
    /// before the partial is removed. If anything looks wrong, the partial is
    /// deliberately left behind rather than deleted.
    /// </summary>
    private static async Task<bool> FinaliseAsync(string partial, string final)
    {
        if (!StorageManager.VerifySaved(partial, out var partialSize) || partialSize == 0)
        {
            AppLogger.Error("The captured file is missing or empty.");
            return false;
        }

        var ok = await Task.Run(() =>
        {
            try
            {
                var (exit, _) = FfmpegRunner.Run(new[]
                {
                    "-nostdin", "-y", "-i", partial,
                    "-c", "copy", "-movflags", "+faststart", final
                }, timeoutMs: 180_000);
                return exit == 0;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Remux step failed.", ex);
                return false;
            }
        });

        if (!ok || !StorageManager.VerifySaved(final, out var finalSize) || finalSize == 0)
        {
            AppLogger.Error("The final recording could not be verified; the captured file has been kept.");
            return false;
        }

        try { File.Delete(partial); } catch { /* keep it if it will not delete */ }
        return true;
    }

    public void AbortQuietly()
    {
        try
        {
            _stopRequested = true;
            if (_proc is { HasExited: false })
            {
                try { _proc.StandardInput.Write("q"); _proc.StandardInput.Flush(); } catch { }
                if (!_proc.WaitForExit(8000))
                {
                    try { _proc.Kill(entireProcessTree: true); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Abort failed.", ex);
        }
    }

    private static async Task<bool> WaitForExitAsync(Process proc, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void Cleanup()
    {
        try
        {
            if (_proc is not null)
            {
                _proc.ErrorDataReceived -= OnFfmpegOutput;
                _proc.Exited -= OnProcessExited;
                _proc.Dispose();
            }
        }
        catch { /* ignore */ }
        _proc = null;
    }

    /// <summary>Turns FFmpeg's technical output into something a doctor can act on.</summary>
    private static string InterpretFailure(string stderr)
    {
        var s = stderr.ToLowerInvariant();

        if (s.Contains("could not find video device") || s.Contains("could not open video device") ||
            s.Contains("i/o error") && s.Contains("video="))
            return "The selected camera is no longer available. Please reconnect the camera and try again.";

        if (s.Contains("could not find audio device") || s.Contains("could not open audio device"))
            return "The selected microphone is no longer available. Please reconnect the microphone and try again.";

        if (s.Contains("device or resource busy") || s.Contains("in use"))
            return "The camera or microphone is being used by another application. " +
                   "Please close the other application and try again.";

        if (s.Contains("permission denied") || s.Contains("access is denied"))
            return "Windows blocked access to the camera or microphone. " +
                   "Please check the privacy settings in Windows and try again.";

        if (s.Contains("unable to choose an output format") || s.Contains("error initializing the muxer"))
            return "The recording file could not be created. Please check the storage location and try again.";

        if (s.Contains("no space left"))
            return "The storage location ran out of space. Please free up space and try again.";

        if (s.Contains("unable to find a suitable output format") || s.Contains("no such file or directory"))
            return "The recording location could not be used. Please check the folder in Settings.";

        if ((s.Contains("gdigrab") || s.Contains("desktop")) && s.Contains("error"))
            return "The screen could not be recorded. Please check the display connection and try again.";

        return "The recording stopped unexpectedly. Please check the camera, microphone and storage, then try again.";
    }

    private static string Redact(string line)
    {
        // Remove anything that looks like a file path so patient IDs stay out of logs.
        return System.Text.RegularExpressions.Regex.Replace(
            line, @"[A-Za-z]:\\[^\s""]+", "<recording path>");
    }
}
