using System.IO;
using System.Text;

namespace PatientConsentHub.Services.Logging;

/// <summary>
/// Minimal, dependency-free application log for troubleshooting.
///
/// Deliberately stores technical events only. Patient IDs are NEVER written -
/// recordings and logs are kept apart so the log is safe to send to support.
/// </summary>
public static class AppLogger
{
    private static readonly object Gate = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "A&T", "Patient Consent Hub", "Logs");

    private static string CurrentFile =>
        Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO ", message, null);
    public static void Warn(string message) => Write("WARN ", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);

                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append(" [").Append(level).Append("] ")
                  .Append(message);

                if (ex is not null)
                {
                    sb.AppendLine();
                    sb.Append("    ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                    if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                        sb.AppendLine().Append(ex.StackTrace);
                }

                File.AppendAllText(CurrentFile, sb.AppendLine().ToString(), Encoding.UTF8);
                PruneOldLogs();
            }
        }
        catch
        {
            // Logging must never take the application down.
        }
    }

    private static DateTime _lastPrune = DateTime.MinValue;

    private static void PruneOldLogs()
    {
        if ((DateTime.Now - _lastPrune).TotalHours < 12) return;
        _lastPrune = DateTime.Now;
        try
        {
            var cutoff = DateTime.Now.AddDays(-60);
            foreach (var f in Directory.GetFiles(LogDirectory, "app-*.log"))
                if (File.GetLastWriteTime(f) < cutoff)
                    File.Delete(f);
        }
        catch { /* ignore */ }
    }
}
