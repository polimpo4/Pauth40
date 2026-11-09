using System.IO;

namespace Pauth40;

/// <summary>
/// Central location for the local application data. The environment variable
/// PAUTH40_DATA_DIR is honored so a second vault can be used for testing.
/// </summary>
public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            string? overridden = Environment.GetEnvironmentVariable("PAUTH40_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overridden))
                return Path.GetFullPath(overridden);

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Pauth40");
        }
    }
}
