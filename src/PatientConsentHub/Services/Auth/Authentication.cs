using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PatientConsentHub.Services.Logging;
using PatientConsentHub.Services.Settings;

namespace PatientConsentHub.Services.Auth;

public sealed record AuthResult(bool Success, string? Message, string? Username);

/// <summary>
/// Authentication contract. The UI only ever talks to this interface, so the
/// local implementation below can be replaced by domain/LDAP/hospital SSO
/// later without touching any window.
/// </summary>
public interface IAuthenticationService
{
    void EnsureInitialised();
    AuthResult SignIn(string username, string password);
    bool ChangePassword(string username, string currentPassword, string newPassword);
}

internal sealed class StoredUser
{
    public string Username { get; set; } = "";
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
    public int Iterations { get; set; } = 120_000;
}

/// <summary>
/// Offline credential store. Passwords are never stored in plain text -
/// PBKDF2-SHA256 with a per-user random salt.
/// </summary>
public sealed class LocalAuthenticationService : IAuthenticationService
{
    private const string DefaultUser = "admin";
    private const string DefaultPassword = "admin";
    private const int Iterations = 120_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private string UsersFile => Path.Combine(SettingsManager.ConfigDirectory, "users.json");

    public void EnsureInitialised()
    {
        Directory.CreateDirectory(SettingsManager.ConfigDirectory);
        if (File.Exists(UsersFile)) return;

        var users = new List<StoredUser> { CreateUser(DefaultUser, DefaultPassword) };
        WriteUsers(users);
        AppLogger.Info("Credential store created with the default account.");
    }

    public AuthResult SignIn(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return new AuthResult(false, "Please enter your username and password.", null);

        try
        {
            var user = ReadUsers()
                .FirstOrDefault(u => string.Equals(u.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));

            if (user is null || !Verify(password, user))
            {
                AppLogger.Warn("Failed sign-in attempt.");
                return new AuthResult(false, "Incorrect username or password.", null);
            }

            AppLogger.Info("User signed in.");
            return new AuthResult(true, null, user.Username);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Sign-in failed unexpectedly.", ex);
            return new AuthResult(false, "Sign-in is unavailable. Please contact your IT administrator.", null);
        }
    }

    public bool ChangePassword(string username, string currentPassword, string newPassword)
    {
        var users = ReadUsers();
        var user = users.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        if (user is null || !Verify(currentPassword, user)) return false;

        var replacement = CreateUser(user.Username, newPassword);
        users.Remove(user);
        users.Add(replacement);
        WriteUsers(users);
        AppLogger.Info("Password changed.");
        return true;
    }

    private static StoredUser CreateUser(string username, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return new StoredUser
        {
            Username = username,
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(hash),
            Iterations = Iterations
        };
    }

    private static bool Verify(string password, StoredUser user)
    {
        var salt = Convert.FromBase64String(user.Salt);
        var expected = Convert.FromBase64String(user.Hash);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, user.Iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private List<StoredUser> ReadUsers()
    {
        if (!File.Exists(UsersFile)) return new List<StoredUser>();
        var json = File.ReadAllText(UsersFile);
        return JsonSerializer.Deserialize<List<StoredUser>>(json) ?? new List<StoredUser>();
    }

    private void WriteUsers(List<StoredUser> users) =>
        File.WriteAllText(UsersFile, JsonSerializer.Serialize(users, JsonOpts));
}
