using System;
using System.IO;

namespace Pauth40;

/// <summary>
/// Central location for the local application data across every Uno Platform
/// target. The PAUTH40_DATA_DIR environment variable is honored so a second
/// vault can be used for testing.
/// </summary>
public static class AppPaths
{
    private static string? _dataDirectory;

    public static string DataDirectory
    {
        get
        {
            if (_dataDirectory is not null)
                return _dataDirectory;

            string? overridden = Environment.GetEnvironmentVariable("PAUTH40_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overridden))
                return _dataDirectory = Path.GetFullPath(overridden);

            _dataDirectory = ResolveDataDirectory();
            return _dataDirectory;
        }
    }

    /// <summary>Directory used by the self-updater for downloaded files.</summary>
    public static string UpdatesDirectory =>
        Path.Combine(DataDirectory, "updates");

    private static string ResolveDataDirectory()
    {
#if __ANDROID__ || __IOS__ || __TVOS__ || __WASM__
        try
        {
            return Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path,
                "Pauth40");
        }
        catch
        {
            // Fall through to the desktop-friendly location.
        }
#endif
        string local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(local))
            local = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(local, "Pauth40");
    }
}
