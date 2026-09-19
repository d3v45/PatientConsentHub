namespace PatientConsentHub.Services.Licensing;

public enum LicenseState
{
    TestingMode,
    Trial,
    Licensed,
    Expired,
    NotActivated,
    DeviceLimitReached
}

public sealed record LicenseStatus(
    LicenseState State,
    string DisplayText,
    DateTime? ExpiresOn = null,
    string? HospitalName = null,
    int? DevicesUsed = null,
    int? DevicesAllowed = null)
{
    public bool AllowsRecording => State is LicenseState.TestingMode or LicenseState.Trial or LicenseState.Licensed;
}

/// <summary>
/// Licensing contract, defined now so it can be implemented later without
/// reworking the application.
///
/// A future KeyBasedLicenseService would implement exactly this surface:
/// product key entry, device-bound activation, expiry, hospital licence,
/// allowed device count, and online/offline activation - and the only change
/// elsewhere would be one line in App.xaml.cs plus a licence screen.
/// </summary>
public interface ILicenseService
{
    LicenseStatus GetStatus();

    /// <summary>Device fingerprint used for device-bound activation.</summary>
    string GetDeviceId();

    /// <summary>Activate with a product key. Returns the resulting status.</summary>
    LicenseStatus Activate(string productKey);

    void Deactivate();
}

/// <summary>
/// First release: no key required. Everything is permitted, and the status
/// string is surfaced in the About box so testers know which mode they are in.
/// </summary>
public sealed class TestingModeLicenseService : ILicenseService
{
    public LicenseStatus GetStatus() =>
        new(LicenseState.TestingMode, "Testing mode - no product key required");

    public string GetDeviceId()
    {
        // Stable-enough machine fingerprint for future device-bound licensing.
        var raw = $"{Environment.MachineName}|{Environment.UserDomainName}|{Environment.ProcessorCount}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..16];
    }

    public LicenseStatus Activate(string productKey) => GetStatus();

    public void Deactivate() { }
}
