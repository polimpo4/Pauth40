using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Pauth40;

/// <summary>
/// Cross-platform replacement for the Windows DPAPI copy of the vault key.
/// A random 32-byte device key is created next to the vault and used to wrap
/// the real vault key with AES-GCM. On Unix the key file is restricted to the
/// current user (0600). Every platform therefore keeps a local recovery copy
/// that works only on that device / user profile.
/// </summary>
public static class DeviceSecretStore
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("P40DEV1");
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private static readonly Lazy<byte[]> LazyKey = new(LoadOrCreateKey, true);

    public static bool IsProtected(byte[] payload) =>
        payload.Length >= Magic.Length + NonceSize + TagSize &&
        payload.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    public static byte[] Protect(byte[] plaintext, byte[] associatedData)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using (var aes = new AesGcm(LazyKey.Value, TagSize))
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        var payload = new byte[Magic.Length + NonceSize + TagSize + ciphertext.Length];
        int offset = 0;
        Magic.CopyTo(payload, offset);
        offset += Magic.Length;
        nonce.CopyTo(payload, offset);
        offset += NonceSize;
        tag.CopyTo(payload, offset);
        offset += TagSize;
        ciphertext.CopyTo(payload, offset);
        return payload;
    }

    public static byte[] Unprotect(byte[] payload, byte[] associatedData)
    {
        ArgumentNullException.ThrowIfNull(payload);

        int minimum = Magic.Length + NonceSize + TagSize;
        if (payload.Length < minimum || !payload.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("The device protected payload is invalid.");

        ReadOnlySpan<byte> nonce = payload.AsSpan(Magic.Length, NonceSize);
        ReadOnlySpan<byte> tag = payload.AsSpan(Magic.Length + NonceSize, TagSize);
        ReadOnlySpan<byte> ciphertext = payload.AsSpan(minimum);

        byte[] plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(LazyKey.Value, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }

    private static string KeyPath => Path.Combine(AppPaths.DataDirectory, "device.key");

    private static byte[] LoadOrCreateKey()
    {
        try
        {
            string path = KeyPath;

            if (File.Exists(path))
            {
                byte[] existing = File.ReadAllBytes(path);
                if (existing.Length >= KeySize)
                    return existing.AsSpan(0, KeySize).ToArray();
            }

            Directory.CreateDirectory(AppPaths.DataDirectory);

            byte[] key = RandomNumberGenerator.GetBytes(KeySize);
            File.WriteAllBytes(path, key);
            RestrictPermissions(path);
            return key;
        }
        catch
        {
            // In-memory fallback for sandboxes without a writable file system.
            return RandomNumberGenerator.GetBytes(KeySize);
        }
    }

    private static void RestrictPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // The platform does not expose Unix permissions; the app sandbox
            // already limits access to the profile.
        }
    }
}
