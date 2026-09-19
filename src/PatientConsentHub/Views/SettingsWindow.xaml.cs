using System.IO;
using System.Windows;
using PatientConsentHub.Models;
using PatientConsentHub.Services.Devices;
using PatientConsentHub.Services.Logging;
using PatientConsentHub.Services.Storage;
using WinForms = System.Windows.Forms;

namespace PatientConsentHub.Views;

public partial class SettingsWindow : Window
{
    private readonly DeviceManager _devices = new();

    public SettingsWindow()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var s = App.Settings.Current;

        FolderBox.Text = s.RecordingRoot;
        ConfirmStopCheck.IsChecked = s.ConfirmBeforeStop;
        LowSpaceBox.Text = s.LowDiskWarningGb.ToString();

        try
        {
            _devices.Refresh();

            CameraCombo.ItemsSource = _devices.Cameras;
            CameraCombo.SelectedItem = DeviceManager.Resolve(_devices.Cameras, s.DefaultCameraName).Device;

            MicCombo.ItemsSource = _devices.Microphones;
            MicCombo.SelectedItem = DeviceManager.Resolve(_devices.Microphones, s.DefaultMicrophoneName).Device;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Settings could not list devices.", ex);
        }

        var displays = ScreenManager.GetDisplays();
        ScreenCombo.ItemsSource = displays;
        ScreenCombo.SelectedItem = ScreenManager.Resolve(displays, s.DefaultScreenIndex);
        ScreenPanel.Visibility = displays.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChangeFolderClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select where patient consent recordings should be saved",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        try
        {
            if (Directory.Exists(FolderBox.Text)) dialog.SelectedPath = FolderBox.Text;
        }
        catch { /* ignore an unusable saved path */ }

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            FolderBox.Text = dialog.SelectedPath;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(folder))
        {
            MessageBox.Show(this, "Please choose a recording location.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Prove the location works before accepting it, rather than failing later
        // at the moment a doctor presses Start.
        var check = StorageManager.CheckBeforeRecording(folder, 0);
        if (!check.Ok)
        {
            MessageBox.Show(this,
                "That location cannot be used for recordings. Please choose another folder.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(LowSpaceBox.Text.Trim(), out var lowGb) || lowGb < 0 || lowGb > 1000)
        {
            MessageBox.Show(this, "Please enter a whole number of GB for the storage warning.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var s = App.Settings.Current;
        s.RecordingRoot = folder;
        s.ConfirmBeforeStop = ConfirmStopCheck.IsChecked == true;
        s.LowDiskWarningGb = lowGb;
        s.DefaultCameraName = (CameraCombo.SelectedItem as CaptureDevice)?.Name;
        s.DefaultMicrophoneName = (MicCombo.SelectedItem as CaptureDevice)?.Name;
        s.DefaultScreenIndex = (ScreenCombo.SelectedItem as DisplayDevice)?.Index ?? 0;

        try
        {
            App.Settings.Save();
            AppLogger.Info("Settings updated.");
            DialogResult = true;
            Close();
        }
        catch
        {
            MessageBox.Show(this,
                "The settings could not be saved. Please contact your IT administrator.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e) =>
        StorageManager.OpenFolder(AppLogger.LogDirectory);
}
