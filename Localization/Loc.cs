namespace Pauth40.Localization;

/// <summary>Short helpers for code-behind and service messages.</summary>
public static class Loc
{
    public static string T(string key) => LocalizationManager.Instance[key];

    public static string Format(string key, params object?[] args) =>
        LocalizationManager.Instance.Format(key, args);
}
