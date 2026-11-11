using System.Text.RegularExpressions;

namespace Pauth40.Services;

/// <summary>
/// Splits the release notes published on GitHub into language sections. A release
/// body may contain standalone <c>[pt-PT]</c> and <c>[en-US]</c> marker lines;
/// only the section for the current interface language is displayed, with a
/// graceful fallback when the body is not sectioned.
/// </summary>
public static class ReleaseNotes
{
    private const string BaseLanguageCode = "pt-PT";
    private const string FallbackLanguageCode = "en-US";

    private static readonly Regex MarkerPattern = new(
        @"^[ \t]*\[(?<code>[A-Za-z]{2}(?:-[A-Za-z]{2})?)\][ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static string Select(string? body, string? languageCode)
    {
        string text = body?.Trim() ?? string.Empty;

        if (text.Length == 0)
            return string.Empty;

        MatchCollection markers = MarkerPattern.Matches(text);

        if (markers.Count == 0)
            return text;

        var sections = new List<(string Code, string Text)>(markers.Count);

        for (int index = 0; index < markers.Count; index++)
        {
            Match marker = markers[index];
            int start = marker.Index + marker.Length;
            int end = index + 1 < markers.Count ? markers[index + 1].Index : text.Length;

            string section = TrimDecorations(text[start..end]);

            if (section.Length > 0)
                sections.Add((marker.Groups["code"].Value, section));
        }

        if (sections.Count == 0)
            return text;

        string preferred = Normalize(languageCode);

        return FindSection(sections, preferred)
               ?? FindSection(sections, BaseLanguageCode)
               ?? FindSection(sections, FallbackLanguageCode)
               ?? sections[0].Text;
    }

    private static string? FindSection(List<(string Code, string Text)> sections, string code)
    {
        if (code.Length == 0)
            return null;

        foreach ((string sectionCode, string text) in sections)
        {
            if (string.Equals(sectionCode, code, StringComparison.OrdinalIgnoreCase))
                return text;
        }

        int separator = code.IndexOf('-');
        string primary = separator > 0 ? code[..separator] : code;

        foreach ((string sectionCode, string text) in sections)
        {
            int sectionSeparator = sectionCode.IndexOf('-');
            string sectionPrimary = sectionSeparator > 0
                ? sectionCode[..sectionSeparator]
                : sectionCode;

            if (string.Equals(sectionPrimary, primary, StringComparison.OrdinalIgnoreCase))
                return text;
        }

        return null;
    }

    private static string Normalize(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code.Trim().Replace('_', '-');

    private static string TrimDecorations(string section)
    {
        string normalized = section.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');

        int start = 0;
        int end = lines.Length - 1;

        while (start <= end && IsDecoration(lines[start]))
            start++;

        while (end >= start && IsDecoration(lines[end]))
            end--;

        if (start > end)
            return string.Empty;

        return string.Join("\n", lines, start, end - start + 1).Trim();
    }

    private static bool IsDecoration(string line)
    {
        string value = line.Trim();

        if (value.Length == 0)
            return true;

        return value.Length >= 3 &&
               (value.All(character => character == '-') ||
                value.All(character => character == '*') ||
                value.All(character => character == '_'));
    }
}
