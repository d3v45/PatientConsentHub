using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using PatientConsentHub.Models;
using PatientConsentHub.Services.Logging;

namespace PatientConsentHub.Services.Preview;

/// <summary>
/// Shows what the selected camera sees, every time the app is opened, before
/// anything is recorded.
///
/// The preview releases the camera the moment recording starts, because
/// DirectShow devices cannot be opened twice.
/// </summary>
public sealed class CameraPreviewService : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly object _gate = new();

    /// <summary>A frozen frame, safe to assign straight to an Image control.</summary>
    public event Action<BitmapSource>? FrameReady;

    /// <summary>Friendly message when the camera cannot be shown.</summary>
    public event Action<string>? PreviewFailed;

    public bool IsRunning { get; private set; }

    public void Start(CaptureDevice camera)
    {
        Stop();

        lock (_gate)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var index = camera.Index;
            IsRunning = true;

            _loop = Task.Run(() => RunLoop(index, token), token);
        }
    }

    private void RunLoop(int index, CancellationToken token)
    {
        VideoCapture? capture = null;
        try
        {
            capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);

            if (!capture.IsOpened())
            {
                AppLogger.Warn($"Preview could not open camera index {index}.");
                PreviewFailed?.Invoke("Camera unavailable. Please check the camera connection.");
                return;
            }

            // A modest preview size keeps CPU low; recording resolution is chosen separately.
            capture.Set(VideoCaptureProperties.FrameWidth, 1280);
            capture.Set(VideoCaptureProperties.FrameHeight, 720);

            using var frame = new Mat();
            var consecutiveFailures = 0;

            while (!token.IsCancellationRequested)
            {
                if (!capture.Read(frame) || frame.Empty())
                {
                    if (++consecutiveFailures > 40)
                    {
                        AppLogger.Warn("Preview lost the camera feed.");
                        PreviewFailed?.Invoke(
                            "The camera is no longer sending a picture. Please check the camera connection.");
                        return;
                    }
                    Thread.Sleep(50);
                    continue;
                }

                consecutiveFailures = 0;

                try
                {
                    var bitmap = frame.ToBitmapSource();
                    bitmap.Freeze();
                    FrameReady?.Invoke(bitmap);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("A preview frame could not be displayed: " + ex.Message);
                }

                Thread.Sleep(33);   // roughly 30 fps
            }
        }
        catch (OperationCanceledException) { /* expected on stop */ }
        catch (Exception ex)
        {
            AppLogger.Error("Camera preview failed.", ex);
            PreviewFailed?.Invoke("Camera unavailable. Please check the camera connection.");
        }
        finally
        {
            try { capture?.Release(); capture?.Dispose(); } catch { /* ignore */ }
            IsRunning = false;
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? loop;

        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        if (cts is null) return;

        try
        {
            cts.Cancel();
            // Wait for the device handle to be released before anything else claims it.
            loop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch { /* ignore */ }
        finally
        {
            cts.Dispose();
            IsRunning = false;
        }
    }

    public void Dispose() => Stop();
}
