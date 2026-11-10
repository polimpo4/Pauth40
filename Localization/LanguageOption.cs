namespace Pauth40.Localization;

/// <summary>
/// One language available in the <c>langs</c> folder. The native name is shown
/// in the selector so users always recognize their own language.
/// </summary>
public sealed record LanguageOption(string Code, string Name, string NativeName)
{
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(NativeName) ? NativeName :
        !string.IsNullOrWhiteSpace(Name) ? Name :
        Code;

    public override string ToString() => DisplayName;
}
