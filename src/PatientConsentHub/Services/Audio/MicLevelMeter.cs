using NAudio.Wave;
using PatientConsentHub.Services.Logging;

namespace PatientConsentHub.Services.Audio;

/// <summary>
/// Drives the microphone level bar so staff can confirm the right microphone
/// is live before recording. No gain, no filters, no audio settings exposed.
/// </summary>
public sealed class MicLevelMeter : IDisposable
{
    private WaveInEvent? _waveIn;
    private float _smoothed;

    /// <summary>Level from 0 to 1, already smoothed for display.</summary>
    public event Action<float>? LevelChanged;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// Starts metering the microphone whose DirectShow name is given.
    /// WaveIn reports names truncated to 31 characters, so matching is done on
    /// a prefix basis rather than exact equality.
    /// </summary>
    public void Start(string directShowName)
    {
        Stop();

        try
        {
            var deviceNumber = FindWaveInDevice(directShowName);
            if (deviceNumber < 0)
            {
                AppLogger.Warn("The selected microphone could not be matched for level metering.");
                return;
            }

            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null)
                    AppLogger.Warn("Microphone metering stopped: " + e.Exception.Message);
            };

            _waveIn.StartRecording();
            IsRunning = true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Microphone level indicator unavailable: " + ex.Message);
            Stop();
        }
    }

    private static int FindWaveInDevice(string directShowName)
    {
        var target = directShowName.Trim();

        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var product = WaveInEvent.GetCapabilities(i).ProductName.Trim();
            if (product.Length == 0) continue;

            if (target.StartsWith(product, StringComparison.OrdinalIgnoreCase) ||
                product.StartsWith(target, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return WaveInEvent.DeviceCount > 0 ? 0 : -1;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var peak = 0f;

        for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            var sample = BitConverter.ToInt16(e.Buffer, i) / 32768f;
            var abs = Math.Abs(sample);
            if (abs > peak) peak = abs;
        }

        // Fast attack, slow release: the bar reacts instantly to speech but
        // does not flicker between syllables.
        _smoothed = peak > _smoothed ? peak : _smoothed * 0.75f + peak * 0.25f;

        LevelChanged?.Invoke(Math.Clamp(_smoothed, 0f, 1f));
    }

    public void Stop()
    {
        try
        {
            if (_waveIn is not null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                try { _waveIn.StopRecording(); } catch { /* ignore */ }
                _waveIn.Dispose();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Could not stop microphone metering: " + ex.Message);
        }
        finally
        {
            _waveIn = null;
            _smoothed = 0;
            IsRunning = false;
            LevelChanged?.Invoke(0f);
        }
    }

    public void Dispose() => Stop();
}
