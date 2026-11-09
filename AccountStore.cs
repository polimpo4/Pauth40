using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pauth40;

/// <summary>
/// Stores the account vault. Existing DPAPI vaults remain readable. Once the
/// application is protected by a password, the vault is encrypted with a random
/// 256-bit key that is wrapped by the password-derived key (see SecurityService).
/// </summary>
public sealed class AccountStore
{
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("Pauth40.v2.local.vault");
    private static readonly byte[] KeyedMagic = Encoding.ASCII.GetBytes("P40VAULT2");
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _directory;
    private readonly string _accountsPath;
    private byte[]? _key;

    public AccountStore()
        : this(null)
    {
    }

    public AccountStore(string? directory)
    {
        _directory = string.IsNullOrWhiteSpace(directory)
            ? AppPaths.DataDirectory
            : Path.GetFullPath(directory);
        _accountsPath = Path.Combine(_directory, "accounts.dat");
    }

    public string DirectoryPath => _directory;

    public string AccountsPath => _accountsPath;

    public bool VaultExists => File.Exists(_accountsPath);

    public bool HasKey => _key is { Length: 32 };

    /// <summary>Uses a password-derived vault key for all subsequent reads/writes.</summary>
    public void SetKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32)
 throw new ArgumentException("The vault key must be 32 bytes.", nameof(key));
        _key = key.ToArray();
    }

    public void ClearKey() => _key = null;

    public List<Account> Load()
    {
        try
        {
            Directory.CreateDirectory(_directory);

            if (!File.Exists(_accountsPath))
                return new List<Account>();

            byte[] raw = File.ReadAllBytes(_accountsPath);
            byte[] jsonBytes;

            if (IsKeyed(raw))
            {
                if (_key is null)
                    throw new InvalidOperationException("The account vault is locked.");

                jsonBytes = DecryptKeyed(raw);
            }
            else
            {
                try
                {
                    jsonBytes = ProtectedData.Unprotect(
                        raw, DpapiEntropy, DataProtectionScope.CurrentUser);
                }
                catch
                {
                    // Allow a plain JSON file for development / manual recovery.
                    jsonBytes = raw;
                }
            }

            var accounts = JsonSerializer.Deserialize<List<Account>>(jsonBytes);
            return accounts ?? new List<Account>();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            return new List<Account>();
        }
    }

    public void Save(IEnumerable<Account> accounts)
    {
        Directory.CreateDirectory(_directory);

        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(accounts.ToList());
        byte[] payload = _key is null
            ? ProtectedData.Protect(jsonBytes, DpapiEntropy, DataProtectionScope.CurrentUser)
            : EncryptKeyed(jsonBytes);

        string tempPath = _accountsPath + ".tmp";
        File.WriteAllBytes(tempPath, payload);
        File.Move(tempPath, _accountsPath, true);
    }

    public void Remove(Account account)
    {
        var accounts = Load();
        var existing = accounts.FirstOrDefault(a => a.Id == account.Id);
        if (existing != null)
        {
            accounts.Remove(existing);
            Save(accounts);
        }
    }

    public bool IsKeyedVault()
    {
        try
        {
            if (!File.Exists(_accountsPath))
                return false;

            using var stream = File.OpenRead(_accountsPath);
            Span<byte> header = stackalloc byte[KeyedMagic.Length];
            return stream.Read(header) == header.Length && header.SequenceEqual(KeyedMagic);
        }
        catch
        {
            return false;
        }
    }

    public void BackupLegacyVault()
    {
        if (!VaultExists || IsKeyedVault())
            return;

        try
        {
            File.Copy(_accountsPath, _accountsPath + ".legacy.bak", true);
        }
        catch
        {
            // A backup is best-effort; migration can still continue.
        }
    }

    private bool IsKeyed(byte[] raw) =>
        raw.Length >= KeyedMagic.Length &&
        raw.AsSpan(0, KeyedMagic.Length).SequenceEqual(KeyedMagic);

    private byte[] EncryptKeyed(byte[] plaintext)
    {
        using var aes = new AesGcm(_key!, TagSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];
        aes.Encrypt(nonce, plaintext, ciphertext, tag, KeyedMagic);

        var payload = new byte[KeyedMagic.Length + NonceSize + TagSize + ciphertext.Length];
        int offset = 0;
        KeyedMagic.CopyTo(payload, offset);
        offset += KeyedMagic.Length;
        nonce.CopyTo(payload, offset);
        offset += NonceSize;
        tag.CopyTo(payload, offset);
        offset += TagSize;
        ciphertext.CopyTo(payload, offset);
        return payload;
    }

    private byte[] DecryptKeyed(byte[] raw)
    {
        int minimum = KeyedMagic.Length + NonceSize + TagSize;
        if (raw.Length < minimum)
            throw new InvalidDataException("The account vault is damaged.");

        ReadOnlySpan<byte> nonce = raw.AsSpan(KeyedMagic.Length, NonceSize);
        ReadOnlySpan<byte> tag = raw.AsSpan(KeyedMagic.Length + NonceSize, TagSize);
        ReadOnlySpan<byte> ciphertext = raw.AsSpan(minimum);

        byte[] plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key!, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, KeyedMagic);
        return plaintext;
    }
}
