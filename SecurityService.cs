using System.IO;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pauth40.Localization;

namespace Pauth40;

public sealed class SmtpSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string UserName { get; set; } = "";
    public string From { get; set; } = "";
    public string PasswordProtected { get; set; } = "";
}

public sealed class SecuritySettings
{
    public int Version { get; set; } = 1;
    public string Email { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string PasswordVerifier { get; set; } = "";
    public int PasswordIterations { get; set; } = SecurityService.DefaultIterations;
    public string WrappedVaultKey { get; set; } = "";
    public string DpapiWrappedVaultKey { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset PasswordUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public SmtpSettings Smtp { get; set; } = new();
}

/// <summary>
/// Owns the master-password verifier, the wrapped vault key and the recovery
/// settings. The vault key itself is random; the password only wraps it, so a
/// password change does not need to re-encrypt the whole vault.
/// </summary>
public sealed class SecurityService
{
    public const int DefaultIterations = 310_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int TagSize = 16;
    private const int NonceSize = 12;
    private const int ResetCodeMinutes = 10;
    private const int ResetCodeMaxAttempts = 5;

    private static readonly byte[] VaultKeyAad = Encoding.UTF8.GetBytes("Pauth40.VaultKey.v1");
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("Pauth40.VaultKey.v1");
    private static readonly byte[] SmtpEntropy = Encoding.UTF8.GetBytes("Pauth40.Smtp.v1");

    private readonly AccountStore _store;
    private readonly string _directory;
    private readonly string _settingsPath;
    private readonly object _resetGate = new();

    private SecuritySettings _settings = new();
    private byte[]? _vaultKey;
    private byte[]? _resetCodeHash;
    private DateTimeOffset _resetCodeExpires;
    private int _resetAttempts;

    public SecurityService(AccountStore store)
        : this(store, null)
    {
    }

    public SecurityService(AccountStore store, string? directory)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _directory = string.IsNullOrWhiteSpace(directory)
            ? AppPaths.DataDirectory
            : Path.GetFullPath(directory);
        _settingsPath = Path.Combine(_directory, "security.json");
        LoadSettings();
    }

    public string SettingsPath => _settingsPath;

    public string Email => _settings.Email;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.Email) &&
        !string.IsNullOrWhiteSpace(_settings.PasswordSalt) &&
        !string.IsNullOrWhiteSpace(_settings.PasswordVerifier) &&
        !string.IsNullOrWhiteSpace(_settings.WrappedVaultKey);

    public bool IsUnlocked => _vaultKey is { Length: KeySize };

    public bool HasSmtp => !string.IsNullOrWhiteSpace(_settings.Smtp.Host);

    public bool HasDpapiRecovery => !string.IsNullOrWhiteSpace(_settings.DpapiWrappedVaultKey);

    public static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email) &&
        MailAddress.TryCreate(email.Trim(), out var address) &&
        !string.IsNullOrWhiteSpace(address.Address);

    public static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ArgumentException(Loc.T("error_password_min"));
    }

    /// <summary>
    /// Creates the protected vault. Any existing DPAPI vault is read first and
    /// re-saved with the new password-derived vault key.
    /// </summary>
    public void CreateAccount(string email, string password, SmtpInput? smtp = null)
    {
        if (IsConfigured)
            throw new InvalidOperationException(Loc.T("error_already_configured"));

        if (!IsValidEmail(email))
            throw new ArgumentException(Loc.T("error_invalid_email"));

        ValidatePassword(password);

        // Read the legacy DPAPI vault before switching to the keyed format.
        List<Account> accounts = _store.Load();
        _store.BackupLegacyVault();

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        var (verifier, kek) = DerivePasswordKeys(password, salt, DefaultIterations);
        byte[] vaultKey = RandomNumberGenerator.GetBytes(KeySize);

        var settings = new SecuritySettings
        {
            Email = email.Trim(),
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordVerifier = Convert.ToBase64String(verifier),
            PasswordIterations = DefaultIterations,
            WrappedVaultKey = Convert.ToBase64String(WrapVaultKey(vaultKey, kek)),
            DpapiWrappedVaultKey = Convert.ToBase64String(
                ProtectedData.Protect(vaultKey, DpapiEntropy, DataProtectionScope.CurrentUser)),
            CreatedUtc = DateTimeOffset.UtcNow,
            PasswordUpdatedUtc = DateTimeOffset.UtcNow
        };

        if (smtp is not null && !string.IsNullOrWhiteSpace(smtp.Host))
            ApplySmtp(settings, smtp, smtp.Password);

        _settings = settings;
        SaveSettings();

        // Settings are on disk first: if saving the keyed vault fails, the
        // legacy DPAPI file is still there and can be migrated on next unlock.
        _vaultKey = vaultKey;
        _store.SetKey(vaultKey);
        _store.Save(accounts);
    }

    public bool Unlock(string password)
    {
        if (!IsConfigured)
            return false;

        try
        {
            byte[] salt = Convert.FromBase64String(_settings.PasswordSalt);
            var (verifier, kek) = DerivePasswordKeys(
                password, salt, _settings.PasswordIterations <= 0
                    ? DefaultIterations
                    : _settings.PasswordIterations);

            byte[] stored = Convert.FromBase64String(_settings.PasswordVerifier);
            if (!CryptographicOperations.FixedTimeEquals(verifier, stored))
                return false;

            byte[] vaultKey = UnwrapVaultKey(
                Convert.FromBase64String(_settings.WrappedVaultKey), kek);
            if (vaultKey.Length != KeySize)
                return false;

            _vaultKey = vaultKey;
            _store.SetKey(vaultKey);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void LockKey()
    {
        if (_vaultKey is not null)
            CryptographicOperations.ZeroMemory(_vaultKey);
        _vaultKey = null;
        _store.ClearKey();
    }

    public void ChangePassword(string currentPassword, string newPassword)
    {
        ValidatePassword(newPassword);

        if (!Unlock(currentPassword) || _vaultKey is null)
            throw new InvalidOperationException(Loc.T("error_current_password"));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        var (verifier, kek) = DerivePasswordKeys(newPassword, salt, DefaultIterations);

        _settings.PasswordSalt = Convert.ToBase64String(salt);
        _settings.PasswordVerifier = Convert.ToBase64String(verifier);
        _settings.PasswordIterations = DefaultIterations;
        _settings.WrappedVaultKey = Convert.ToBase64String(WrapVaultKey(_vaultKey, kek));
        _settings.PasswordUpdatedUtc = DateTimeOffset.UtcNow;
        SaveSettings();
    }

    /// <summary>
    /// Recovers the vault key through the copy protected by Windows DPAPI and
    /// wraps it again with a new password. The e-mail code is checked by the
    /// view before this method is called.
    /// </summary>
    public void ResetPasswordWithDpapi(string newPassword)
    {
        ValidatePassword(newPassword);

        byte[] vaultKey;
        try
        {
            vaultKey = ProtectedData.Unprotect(
                Convert.FromBase64String(_settings.DpapiWrappedVaultKey),
                DpapiEntropy,
                DataProtectionScope.CurrentUser);
        }
        catch
        {
            throw new InvalidOperationException(
                Loc.T("error_dpapi_recover"));
        }

        if (vaultKey.Length != KeySize)
            throw new InvalidOperationException(Loc.T("error_recovery_key_invalid"));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        var (verifier, kek) = DerivePasswordKeys(newPassword, salt, DefaultIterations);

        _settings.PasswordSalt = Convert.ToBase64String(salt);
        _settings.PasswordVerifier = Convert.ToBase64String(verifier);
        _settings.PasswordIterations = DefaultIterations;
        _settings.WrappedVaultKey = Convert.ToBase64String(WrapVaultKey(vaultKey, kek));
        _settings.DpapiWrappedVaultKey = Convert.ToBase64String(
            ProtectedData.Protect(vaultKey, DpapiEntropy, DataProtectionScope.CurrentUser));
        _settings.PasswordUpdatedUtc = DateTimeOffset.UtcNow;
        SaveSettings();

        _vaultKey = vaultKey;
        _store.SetKey(vaultKey);
    }

    public void UpdateSmtp(SmtpInput input)
    {
        ApplySmtp(_settings, input, input.Password);
        SaveSettings();
    }

    public SmtpInput GetSmtpInput()
    {
        return new SmtpInput(
            _settings.Smtp.Host,
            _settings.Smtp.Port <= 0 ? 587 : _settings.Smtp.Port,
            _settings.Smtp.UseSsl,
            _settings.Smtp.UserName,
            _settings.Smtp.From,
            UnprotectSmtpPassword());
    }

    public bool TryGetSmtpOptions(out SmtpOptions options)
    {
        options = default!;
        if (string.IsNullOrWhiteSpace(_settings.Smtp.Host))
            return false;

        string from = string.IsNullOrWhiteSpace(_settings.Smtp.From)
            ? _settings.Email
            : _settings.Smtp.From;

        options = new SmtpOptions(
            _settings.Smtp.Host.Trim(),
            _settings.Smtp.Port <= 0 ? 587 : _settings.Smtp.Port,
            _settings.Smtp.UseSsl,
            _settings.Smtp.UserName.Trim(),
            UnprotectSmtpPassword(),
            from.Trim());
        return true;
    }

    /// <summary>Creates a six digit code that is valid for ten minutes.</summary>
    public string GenerateResetCode()
    {
        string code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        lock (_resetGate)
        {
            _resetCodeHash = SHA256.HashData(Encoding.UTF8.GetBytes(code));
            _resetCodeExpires = DateTimeOffset.UtcNow.AddMinutes(ResetCodeMinutes);
            _resetAttempts = 0;
        }

        return code;
    }

    public bool VerifyResetCode(string code)
    {
        string trimmed = (code ?? "").Trim();
        if (trimmed.Length != 6 || !trimmed.All(char.IsAsciiDigit))
            return false;

        lock (_resetGate)
        {
            if (_resetCodeHash is null || DateTimeOffset.UtcNow > _resetCodeExpires)
                return false;

            byte[] candidate = SHA256.HashData(Encoding.UTF8.GetBytes(trimmed));
            bool valid = CryptographicOperations.FixedTimeEquals(candidate, _resetCodeHash);

            if (!valid)
            {
                _resetAttempts++;
                if (_resetAttempts >= ResetCodeMaxAttempts)
                {
                    _resetCodeHash = null;
                    _resetCodeExpires = default;
                }

                return false;
            }

            _resetCodeHash = null;
            _resetCodeExpires = default;
            _resetAttempts = 0;
            return true;
        }
    }

    public string MaskedEmail()
    {
        if (string.IsNullOrWhiteSpace(_settings.Email))
            return "";

        int at = _settings.Email.IndexOf('@');
        if (at <= 0)
            return _settings.Email;

        string local = _settings.Email[..at];
        string domain = _settings.Email[(at + 1)..];
        string visible = local.Length <= 2 ? local[..1] : local[..2];
        return $"{visible}{new string('*', Math.Max(2, local.Length - visible.Length))}@{domain}";
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return;

            string json = File.ReadAllText(_settingsPath, Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize<SecuritySettings>(json);
            if (loaded is not null)
                _settings = loaded;
        }
        catch
        {
            _settings = new SecuritySettings();
        }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(_directory);
        string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        string temp = _settingsPath + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, _settingsPath, true);
    }

    private void ApplySmtp(SecuritySettings settings, SmtpInput input, string? plainPassword)
    {
        settings.Smtp.Host = (input.Host ?? "").Trim();
        settings.Smtp.Port = input.Port <= 0 ? 587 : input.Port;
        settings.Smtp.UseSsl = input.UseSsl;
        settings.Smtp.UserName = (input.UserName ?? "").Trim();
        settings.Smtp.From = string.IsNullOrWhiteSpace(input.From)
            ? settings.Email
            : input.From.Trim();

        if (!string.IsNullOrEmpty(plainPassword))
        {
            settings.Smtp.PasswordProtected = Convert.ToBase64String(
                ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plainPassword),
                    SmtpEntropy,
                    DataProtectionScope.CurrentUser));
        }
    }

    private string UnprotectSmtpPassword()
    {
        if (string.IsNullOrWhiteSpace(_settings.Smtp.PasswordProtected))
            return "";

        try
        {
            byte[] raw = ProtectedData.Unprotect(
                Convert.FromBase64String(_settings.Smtp.PasswordProtected),
                SmtpEntropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(raw);
        }
        catch
        {
            return "";
        }
    }

    private static (byte[] Verifier, byte[] Kek) DerivePasswordKeys(
        string password, byte[] salt, int iterations)
    {
        byte[] material = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Math.Max(10_000, iterations),
            HashAlgorithmName.SHA256,
            64);

        return (material[..32], material[32..64]);
    }

    private static byte[] WrapVaultKey(byte[] vaultKey, byte[] kek)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[vaultKey.Length];
        byte[] tag = new byte[TagSize];

        using var aes = new AesGcm(kek, TagSize);
        aes.Encrypt(nonce, vaultKey, ciphertext, tag, VaultKeyAad);

        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        ciphertext.CopyTo(result, NonceSize + TagSize);
        return result;
    }

    private static byte[] UnwrapVaultKey(byte[] wrapped, byte[] kek)
    {
        int minimum = NonceSize + TagSize;
        if (wrapped.Length < minimum)
            throw new InvalidDataException(Loc.T("error_wrapped_key_damaged"));

        ReadOnlySpan<byte> nonce = wrapped.AsSpan(0, NonceSize);
        ReadOnlySpan<byte> tag = wrapped.AsSpan(NonceSize, TagSize);
        ReadOnlySpan<byte> ciphertext = wrapped.AsSpan(minimum);

        byte[] vaultKey = new byte[ciphertext.Length];
        using var aes = new AesGcm(kek, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, vaultKey, VaultKeyAad);
        return vaultKey;
    }
}

public sealed record SmtpInput(
    string Host,
    int Port,
    bool UseSsl,
    string UserName,
    string From,
    string? Password);

/// <summary>Password protected backup file for the account database.</summary>
public static class BackupService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PAUTH40BK1");
    private const byte Version = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Iterations = SecurityService.DefaultIterations;

    public static void Export(string path, IEnumerable<Account> accounts, string password)
    {
        SecurityService.ValidatePassword(password);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(accounts.ToList());
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = DeriveBackupKey(password, salt);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[json.Length];
        byte[] tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, json, ciphertext, tag, Magic);

        var payload = new byte[Magic.Length + 1 + SaltSize + NonceSize + TagSize + ciphertext.Length];
        int offset = 0;
        Magic.CopyTo(payload, offset);
        offset += Magic.Length;
        payload[offset++] = Version;
        salt.CopyTo(payload, offset);
        offset += SaltSize;
        nonce.CopyTo(payload, offset);
        offset += NonceSize;
        tag.CopyTo(payload, offset);
        offset += TagSize;
        ciphertext.CopyTo(payload, offset);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, payload);
        File.Move(temp, path, true);
    }

    public static List<Account> Import(string path, string password)
    {
        byte[] payload = File.ReadAllBytes(path);
        int header = Magic.Length + 1 + SaltSize;
        int minimum = header + NonceSize + TagSize;

        if (payload.Length < minimum ||
            !payload.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException(Loc.T("error_backup_invalid"));
        }

        if (payload[Magic.Length] != Version)
            throw new InvalidDataException(Loc.T("error_backup_version"));

        int offset = Magic.Length + 1;
        byte[] salt = payload.AsSpan(offset, SaltSize).ToArray();
        offset += SaltSize;
        byte[] nonce = payload.AsSpan(offset, NonceSize).ToArray();
        offset += NonceSize;
        byte[] tag = payload.AsSpan(offset, TagSize).ToArray();
        offset += TagSize;
        byte[] ciphertext = payload.AsSpan(offset).ToArray();

        byte[] key = DeriveBackupKey(password, salt);
        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Magic);
        }
        catch (CryptographicException)
        {
            throw new InvalidDataException(Loc.T("error_backup_password"));
        }

        var accounts = JsonSerializer.Deserialize<List<Account>>(plaintext);
        if (accounts is null)
            throw new InvalidDataException(Loc.T("error_backup_no_accounts"));

        foreach (var account in accounts)
        {
            if (string.IsNullOrWhiteSpace(account.Secret))
                throw new InvalidDataException(Loc.T("error_backup_invalid_account"));
        }

        return accounts;
    }

    private static byte[] DeriveBackupKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            32);
}
