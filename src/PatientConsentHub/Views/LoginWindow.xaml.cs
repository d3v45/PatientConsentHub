using System.Windows;
using PatientConsentHub.Services.Logging;

namespace PatientConsentHub.Views;

public partial class LoginWindow : Window
{
    /// <summary>Set once sign-in succeeds; used by the main window header.</summary>
    public static string SignedInUser { get; private set; } = "";

    public LoginWindow()
    {
        InitializeComponent();
        ModeText.Text = App.License.GetStatus().DisplayText;
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void OnSignInClick(object sender, RoutedEventArgs e)
    {
        MessageText.Visibility = Visibility.Collapsed;
        SignInButton.IsEnabled = false;

        try
        {
            var status = App.License.GetStatus();
            if (!status.AllowsRecording)
            {
                ShowMessage(status.DisplayText);
                return;
            }

            var result = App.Auth.SignIn(UsernameBox.Text, PasswordBox.Password);

            if (!result.Success)
            {
                ShowMessage(result.Message ?? "Sign-in failed.");
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
            }

            SignedInUser = result.Username ?? UsernameBox.Text.Trim();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unexpected sign-in error.", ex);
            ShowMessage("Sign-in is unavailable. Please contact your IT administrator.");
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        MessageText.Visibility = Visibility.Visible;
    }
}
