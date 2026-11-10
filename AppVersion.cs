using System.Reflection;

namespace Pauth40;

/// <summary>
/// Version information read from the assembly metadata. It is not hardcoded, so
/// every build/publish automatically exposes the version configured in the
/// project file.
/// </summary>
public static class AppVersion
{
    public static string Current { get; } = ResolveCurrent();

    public static string Display => "v" + Current;

    public static SemanticVersion? Semantic { get; } = ResolveSemantic();

    private static string ResolveCurrent()
    {
        Assembly assembly = typeof(AppVersion).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            string value = informational.Trim();

            int buildIndex = value.IndexOf('+');
            if (buildIndex >= 0)
                value = value[..buildIndex];

            if (SemanticVersion.TryParse(value, out SemanticVersion? parsed) && parsed is not null)
                return parsed.ToString();
        }

        Version? version = assembly.GetName().Version;

        if (version is null)
            return "0.0.0";

        return $"{Math.Max(0, version.Major)}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}";
    }

    private static SemanticVersion? ResolveSemantic() =>
        SemanticVersion.TryParse(Current, out SemanticVersion? parsed) ? parsed : null;
}
