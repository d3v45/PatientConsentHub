using System.Windows;
using System.Windows.Threading;
using PatientConsentHub.Services.Auth;
using PatientConsentHub.Services.Licensing;
using PatientConsentHub.Services.Logging;
using PatientConsentHub.Services.Settings;
using PatientConsentHub.Views;

using WpfApplication = System.Windows.Application;

namespace PatientConsentHub;

public partial class App : WpfApplication
{
    public static SettingsManager Settings { get; private set; } = null!;
    public static IAuthenticationService Auth { get; private set; } = null!;
    public static ILicenseService License { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLogger.Info("=== Patient Consent Hub starting ===");

        // Never show a raw crash to a doctor. Log it, explain it, keep running if we can.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLogger.Error("Fatal unhandled exception", args.ExceptionObject as Exception);

        try
        {
            Settings = new SettingsManager();
            Settings.Load();

            Auth = new LocalAuthenticationService();
            Auth.EnsureInitialised();

            // Testing mode: login only. Swap this implementation for a real
            // key-based service later without touching the rest of the app.
            License = new TestingModeLicenseService();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Startup initialisation failed", ex);
            MessageBox.Show(
                "Patient Consent Hub could not start correctly. Please contact your IT administrator.",
                "Patient Consent Hub", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var login = new LoginWindow();
        if (login.ShowDialog() == true)
        {
            var main = new MainWindow();
            MainWindow = main;
            main.Closed += (_, _) => Shutdown();
            main.Show();
        }
        else
        {
            AppLogger.Info("Login cancelled. Exiting.");
            Shutdown();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            "Something went wrong. The problem has been written to the application log.\n\n" +
            "If a recording was in progress, please check the patient folder before recording again.",
            "Patient Consent Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("=== Patient Consent Hub exiting ===");
        base.OnExit(e);
    }
}
