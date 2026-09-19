using System.IO;
using System.Windows;
using PatientConsentHub.Models;
using PatientConsentHub.Services.History;
using PatientConsentHub.Services.Storage;

namespace PatientConsentHub.Views;

public partial class HistoryWindow : Window
{
    private readonly HistoryService _history = new();

    public HistoryWindow()
    {
        InitializeComponent();
        Load();
    }

    private void Load()
    {
        var sessions = _history.Load();
        HistoryGrid.ItemsSource = sessions;
        EmptyText.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (sessions.Count > 0) HistoryGrid.SelectedIndex = 0;
    }

    private RecordingSession? Selected => HistoryGrid.SelectedItem as RecordingSession;

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var session = Selected;
        if (session is null)
        {
            MessageBox.Show(this, "Please select a recording first.", "Recordings",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!Directory.Exists(session.FolderPath))
        {
            MessageBox.Show(this,
                "That patient folder could not be found. It may have been moved or the storage location changed.",
                "Recordings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StorageManager.OpenFolder(session.FolderPath);
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        var session = Selected;
        if (session is null)
        {
            MessageBox.Show(this, "Please select a recording first.", "Recordings",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!File.Exists(session.CameraFile))
        {
            MessageBox.Show(this,
                "That recording file could not be found. It may have been moved or deleted.",
                "Recordings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StorageManager.OpenFile(session.CameraFile);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
