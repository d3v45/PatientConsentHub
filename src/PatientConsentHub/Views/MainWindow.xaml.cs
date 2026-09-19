using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PatientConsentHub.Models;
using PatientConsentHub.Services.Audio;
using PatientConsentHub.Services.Devices;
using PatientConsentHub.Services.History;
using PatientConsentHub.Services.Logging;
using PatientConsentHub.Services.Preview;
using PatientConsentHub.Services.Recording;
using PatientConsentHub.Services.Recovery;
using PatientConsentHub.Services.Storage;

using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace PatientConsentHub.Views;

public partial class MainWindow : Window
{
    private readonly DeviceManager _devices = new();
    private readonly CameraPreviewService _preview = new();
    private readonly MicLevelMeter _meter = new();
    private readonly IRecordingEngine _engine = new FfmpegRecordingEngine();
    private readonly HistoryService _history = new();

    private readonly DispatcherTimer _durationTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private bool _loadingDevices;
    private bool _stopInProgress;
    private DateTime _recordingStartedAt;
    private string? _lastSavedFolder;

    public MainWindow()
    {
        InitializeComponent();

        HeaderSubtitle.Text = $"Signed in as {LoginWindow.SignedInUser} - {App.License.GetStatus().DisplayText}";

        _preview.FrameReady += OnPreviewFrame;
        _preview.PreviewFailed += OnPreviewFailed;
        _meter.LevelChanged += OnMicLevel;
        _engine.CaptureFailed += OnCaptureFailed;

        _durationTimer.Tick += (_, _) => UpdateDuration();
        _blinkTimer.Tick += (_, _) =>
            RecordDot.Opacity = RecordDot.Opacity > 0.5 ? 0.15 : 1.0;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    // =====================================================================
    //  Startup
    // =====================================================================

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (!FfmpegLocator.IsAvailable)
        {
            MessageBox.Show(this,
                "The recording component is missing. Please reinstall Patient Consent Hub.",
                "Patient Consent Hub", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        LoadDevices();
        await CheckForUnfinishedRecordingAsync();
    }

    private void LoadDevices()
    {
        _loadingDevices = true;
        try
        {
            _devices.Refresh();
            var settings = App.Settings.Current;

            // --- cameras
            CameraCombo.ItemsSource = _devices.Cameras;
            var (camera, cameraFallback) = DeviceManager.Resolve(_devices.Cameras, settings.DefaultCameraName);
            CameraCombo.SelectedItem = camera;

            // --- microphones
            MicCombo.ItemsSource = _devices.Microphones;
            var (mic, micFallback) = DeviceManager.Resolve(_devices.Microphones, settings.DefaultMicrophoneName);
            MicCombo.SelectedItem = mic;

            // --- displays (hidden entirely when there is only one)
            var displays = ScreenManager.GetDisplays();
            ScreenCombo.ItemsSource = displays;
            ScreenCombo.SelectedItem = ScreenManager.Resolve(displays, settings.DefaultScreenIndex);
            UpdateScreenPanelVisibility(displays.Count);

            _loadingDevices = false;

            if (camera is null)
                ShowPreviewMessage("Camera unavailable. Please check the camera connection.");
            else
                StartPreview(camera);

            if (mic is not null) _meter.Start(mic.Name);
            MicHintText.Text = mic is null
                ? "No microphone was detected. Please connect a microphone."
                : "Speak to check the microphone.";

            if (cameraFallback || micFallback)
            {
                var which = cameraFallback && micFallback ? "camera and microphone"
                    : cameraFallback ? "camera" : "microphone";
                MessageBox.Show(this,
                    $"The saved {which} was not available, so another available device has been selected.\n\n" +
                    "Please check the preview before recording.",
                    "Patient Consent Hub", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (FfmpegNotFoundException)
        {
            _loadingDevices = false;
            ShowPreviewMessage("The recording component is missing. Please reinstall Patient Consent Hub.");
        }
        catch (Exception ex)
        {
            _loadingDevices = false;
            AppLogger.Error("Device setup failed.", ex);
            ShowPreviewMessage("Devices could not be detected. Please check the camera and microphone connections.");
        }
    }

    private void UpdateScreenPanelVisibility(int displayCount)
    {
        ScreenPanel.Visibility = displayCount > 1 && ConsentScreenRadio.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task CheckForUnfinishedRecordingAsync()
    {
        var pending = RecoveryManager.FindPending();
        if (pending is null) return;

        var answer = MessageBox.Show(this,
            "An unfinished recording was detected.\n\n" +
            $"Patient ID: {pending.PatientId}\n" +
            $"Started: {pending.StartedAt:dd MMM yyyy HH:mm}\n\n" +
            "Would you like to recover it now?\n\n" +
            "Choose No to keep the data and decide later. Nothing will be deleted.",
            "Unfinished recording", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var recovered = await RecoveryManager.RecoverAsync(pending);
            MessageBox.Show(this,
                recovered
                    ? "The recording was recovered and saved in the patient folder."
                    : "The recording could not be recovered automatically. The captured data has been kept " +
                      "in the patient folder - please contact your IT administrator.",
                "Patient Consent Hub", MessageBoxButton.OK,
                recovered ? MessageBoxImage.Information : MessageBoxImage.Warning);

            if (recovered) StorageManager.OpenFolder(pending.PatientFolder);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    // =====================================================================
    //  Preview and metering
    // =====================================================================

    private void StartPreview(CaptureDevice camera)
    {
        ShowPreviewMessage("Starting camera...");
        _preview.Start(camera);
    }

    private void OnPreviewFrame(System.Windows.Media.Imaging.BitmapSource frame)
    {
        Dispatcher.BeginInvoke(() =>
        {
            PreviewImage.Source = frame;
            if (PreviewMessage.Visibility == Visibility.Visible)
                PreviewMessage.Visibility = Visibility.Collapsed;
        }, DispatcherPriority.Render);
    }

    private void OnPreviewFailed(string message) =>
        Dispatcher.BeginInvoke(() => ShowPreviewMessage(message));

    private void ShowPreviewMessage(string message)
    {
        PreviewImage.Source = null;
        PreviewMessage.Text = message;
        PreviewMessage.Visibility = Visibility.Visible;
    }

    private void OnMicLevel(float level) =>
        Dispatcher.BeginInvoke(() => MicLevelBar.Value = Math.Round(level * 100), DispatcherPriority.Background);

    private void OnCameraChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingDevices || _engine.IsRecording) return;
        if (CameraCombo.SelectedItem is not CaptureDevice camera) return;

        _preview.Stop();
        StartPreview(camera);

        App.Settings.Current.DefaultCameraName = camera.Name;
        TrySaveSettings();
    }

    private void OnMicChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingDevices || _engine.IsRecording) return;
        if (MicCombo.SelectedItem is not CaptureDevice mic) return;

        _meter.Start(mic.Name);
        App.Settings.Current.DefaultMicrophoneName = mic.Name;
        TrySaveSettings();
    }

    private static void TrySaveSettings()
    {
        try { App.Settings.Save(); }
        catch { /* already logged; not worth interrupting the doctor */ }
    }

    // =====================================================================
    //  Patient ID and consent type
    // =====================================================================

    private void OnPatientIdChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        HideValidation();

        var raw = PatientIdBox.Text;
        var safe = StorageManager.SanitizePatientId(raw);

        PatientIdHint.Text = string.IsNullOrWhiteSpace(raw) || safe == raw.Trim()
            ? ""
            : $"This recording will be saved in the folder: {safe}";
    }

    private void OnConsentTypeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateScreenPanelVisibility(ScreenCombo.Items.Count);
    }

    // =====================================================================
    //  Start
    // =====================================================================

    private void OnStartClick(object sender, RoutedEventArgs e) => StartRecording();

    private void StartRecording()
    {
        if (_engine.IsRecording) return;

        HideValidation();
        SavedBanner.Visibility = Visibility.Collapsed;

        var patientId = StorageManager.SanitizePatientId(PatientIdBox.Text);
        if (string.IsNullOrWhiteSpace(patientId))
        {
            ShowValidation("Please enter Patient ID before starting the recording.");
            PatientIdBox.Focus();
            return;
        }

        if (CameraCombo.SelectedItem is not CaptureDevice camera)
        {
            ShowValidation("No camera is available. Please connect a camera and try again.");
            return;
        }

        if (MicCombo.SelectedItem is not CaptureDevice mic)
        {
            ShowValidation("No microphone is available. Please connect a microphone and try again.");
            return;
        }

        var settings = App.Settings.Current;
        var withScreen = ConsentScreenRadio.IsChecked == true;
        var screen = withScreen ? ScreenCombo.SelectedItem as DisplayDevice : null;

        if (withScreen && screen is null)
        {
            ShowValidation("No display was detected for screen recording. Please check the display connection.");
            return;
        }

        // ---- storage checks before anything starts
        var check = StorageManager.CheckBeforeRecording(settings.RecordingRoot, settings.LowDiskWarningGb);
        if (!check.Ok)
        {
            if (check.FreeBytes > 0)
            {
                var proceed = MessageBox.Show(this,
                    check.FriendlyMessage + "\n\nDo you want to continue anyway?",
                    "Low storage space", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (proceed != MessageBoxResult.Yes) return;
            }
            else
            {
                MessageBox.Show(this, check.FriendlyMessage, "Patient Consent Hub",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        // The preview and level meter hold the devices open, so they must let go.
        StartButton.IsEnabled = false;
        _preview.Stop();
        _meter.Stop();

        var request = new RecordingRequest
        {
            PatientId = patientId,
            Type = withScreen ? ConsentType.ConsentViewPlusScreen : ConsentType.ConsentView,
            CameraName = camera.Name,
            MicrophoneName = mic.Name,
            Screen = screen,
            RecordingRoot = settings.RecordingRoot,
            CameraFps = settings.CameraFrameRate,
            ScreenFps = settings.ScreenFrameRate
        };

        var result = _engine.Start(request);

        if (!result.Success)
        {
            StartButton.IsEnabled = true;
            RestoreDevices();
            MessageBox.Show(this, result.FriendlyError, "Patient Consent Hub",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _recordingStartedAt = DateTime.Now;
        EnterRecordingView(patientId, request.Type);
    }

    private void EnterRecordingView(string patientId, ConsentType type)
    {
        RecordingPatientText.Text = $"Patient ID: {patientId}";
        RecordingTypeText.Text = ConsentTypeText.Display(type);
        DurationText.Text = "00:00:00";
        StopStatusText.Text = "";
        StopButton.IsEnabled = true;

        SetupView.Visibility = Visibility.Collapsed;
        RecordingView.Visibility = Visibility.Visible;

        _durationTimer.Start();
        _blinkTimer.Start();
    }

    private void UpdateDuration()
    {
        var elapsed = DateTime.Now - _recordingStartedAt;
        DurationText.Text = elapsed.ToString(@"hh\:mm\:ss");
    }

    // =====================================================================
    //  Stop
    // =====================================================================

    private async void OnStopClick(object sender, RoutedEventArgs e) => await StopRecordingAsync();

    private async Task StopRecordingAsync()
    {
        if (!_engine.IsRecording || _stopInProgress) return;

        if (App.Settings.Current.ConfirmBeforeStop)
        {
            var confirm = MessageBox.Show(this,
                "Stop recording?",
                "Patient Consent Hub", MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.OK);
            if (confirm != MessageBoxResult.OK) return;
        }

        _stopInProgress = true;
        StopButton.IsEnabled = false;
        StopStatusText.Text = "Saving the recording. Please wait...";
        _blinkTimer.Stop();
        RecordDot.Opacity = 1;

        SaveResult result;
        try
        {
            result = await _engine.StopAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Stopping the recording failed.", ex);
            result = SaveResult.Fail("Recording could not be saved. Please check the storage location and try again.");
        }

        _durationTimer.Stop();
        _stopInProgress = false;

        LeaveRecordingView();

        if (result is { Success: true, Session: not null })
        {
            _history.Add(result.Session);
            _lastSavedFolder = result.Session.FolderPath;

            SavedDetailText.Text =
                $"Patient ID {result.Session.PatientId} - {result.Session.DurationText} - " +
                $"{result.Session.TypeText}";
            SavedBanner.Visibility = Visibility.Visible;
        }
        else
        {
            MessageBox.Show(this,
                result.FriendlyError ?? "Recording could not be saved.",
                "Patient Consent Hub", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LeaveRecordingView()
    {
        RecordingView.Visibility = Visibility.Collapsed;
        SetupView.Visibility = Visibility.Visible;
        StartButton.IsEnabled = true;
        PatientIdBox.Clear();
        PatientIdHint.Text = "";
        RestoreDevices();
        PatientIdBox.Focus();
    }

    /// <summary>Brings the preview and level meter back once the devices are free.</summary>
    private void RestoreDevices()
    {
        if (CameraCombo.SelectedItem is CaptureDevice camera) StartPreview(camera);
        if (MicCombo.SelectedItem is CaptureDevice mic) _meter.Start(mic.Name);
    }

    /// <summary>Capture died on its own - a cable was pulled, a drive vanished.</summary>
    private void OnCaptureFailed(string friendlyMessage)
    {
        // Raised on FFmpeg's process-exit thread, so hop to the UI thread first.
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (!RecordingView.IsVisible || _stopInProgress) return;

            _durationTimer.Stop();
            _blinkTimer.Stop();
            _stopInProgress = true;
            StopButton.IsEnabled = false;
            StopStatusText.Text = "Saving what was recorded...";

            SaveResult result;
            try { result = await _engine.StopAsync(); }
            catch (Exception ex)
            {
                AppLogger.Error("Failure handling could not finalise the recording.", ex);
                result = SaveResult.Fail("The recording could not be saved.");
            }

            _stopInProgress = false;
            LeaveRecordingView();

            if (result is { Success: true, Session: not null })
            {
                _history.Add(result.Session);
                _lastSavedFolder = result.Session.FolderPath;
                MessageBox.Show(this,
                    friendlyMessage + "\n\nThe part of the recording that was captured has been saved.",
                    "Recording interrupted", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(this, friendlyMessage, "Recording interrupted",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            LoadDevices();
        }));
    }

    // =====================================================================
    //  Navigation, shortcuts, shutdown
    // =====================================================================

    private void OnWindowKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.F9 && !_engine.IsRecording && SetupView.IsVisible)
        {
            StartRecording();
            e.Handled = true;
        }
        else if (e.Key == Key.F10 && _engine.IsRecording)
        {
            _ = StopRecordingAsync();
            e.Handled = true;
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRecording) return;

        _preview.Stop();
        _meter.Stop();

        var window = new SettingsWindow { Owner = this };
        window.ShowDialog();

        CameraCapabilities.Invalidate();
        LoadDevices();
    }

    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        var window = new HistoryWindow { Owner = this };
        window.ShowDialog();
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var status = App.License.GetStatus();
        MessageBox.Show(this,
            "Patient Consent Hub\n" +
            "Version 1.0\n\n" +
            "A&T\n\n" +
            $"Licence: {status.DisplayText}\n" +
            $"Device ID: {App.License.GetDeviceId()}\n\n" +
            $"Recordings: {App.Settings.Current.RecordingRoot}\n" +
            $"Logs: {AppLogger.LogDirectory}",
            "About Patient Consent Hub", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnOpenLastFolderClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastSavedFolder))
            StorageManager.OpenFolder(_lastSavedFolder);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_engine.IsRecording)
        {
            var answer = MessageBox.Show(this,
                "A recording is still in progress.\n\nClose the application and save the recording now?",
                "Recording in progress", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;
            try { _engine.StopAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { AppLogger.Error("Shutdown save failed.", ex); }
            finally { Mouse.OverrideCursor = null; }
        }

        _durationTimer.Stop();
        _blinkTimer.Stop();
        _preview.Dispose();
        _meter.Dispose();
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private void HideValidation() => ValidationText.Visibility = Visibility.Collapsed;
}
