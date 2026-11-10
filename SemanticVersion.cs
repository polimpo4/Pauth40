using System.Globalization;

namespace Pauth40;

/// <summary>
/// Minimal Semantic Version 2.0 parser and comparer. Release tags are usually
/// written as <c>v2.1.0</c>, while the assembly version is <c>2.1.0</c>, so the
/// leading <c>v</c> and build metadata are ignored.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private readonly string[] _preRelease;

    private SemanticVersion(int major, int minor, int patch, string[] preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _preRelease = preRelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public bool IsPreRelease => _preRelease.Length > 0;

    public static bool TryParse(string? text, out SemanticVersion? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        string value = text.Trim();

        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];

        int buildIndex = value.IndexOf('+');
        if (buildIndex >= 0)
            value = value[..buildIndex];

        string core = value;
        string[] preRelease = Array.Empty<string>();

        int preIndex = value.IndexOf('-');
        if (preIndex >= 0)
        {
            core = value[..preIndex];
            preRelease = value[(preIndex + 1)..].Split('.', StringSplitOptions.None);

            if (preRelease.Length == 0 || preRelease.Any(part => part.Length == 0))
                return false;
        }

        string[] parts = core.Split('.');
        if (parts.Length is < 1 or > 3)
            return false;

        if (!TryParsePart(parts[0], out int major))
            return false;

        int minor = 0;
        int patch = 0;

        if (parts.Length > 1 && !TryParsePart(parts[1], out minor))
            return false;

        if (parts.Length > 2 && !TryParsePart(parts[2], out patch))
            return false;

        foreach (string identifier in preRelease)
        {
            if (identifier.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character != '-'))
            {
                return false;
            }
        }

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
            return 1;

        int result = Major.CompareTo(other.Major);
        if (result != 0)
            return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
            return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
            return result;

        if (_preRelease.Length == 0 && other._preRelease.Length == 0)
            return 0;

        // A release has higher precedence than any of its pre-releases.
        if (_preRelease.Length == 0)
            return 1;

        if (other._preRelease.Length == 0)
            return -1;

        int count = Math.Min(_preRelease.Length, other._preRelease.Length);

        for (int index = 0; index < count; index++)
        {
            string left = _preRelease[index];
            string right = other._preRelease[index];

            bool leftNumeric = TryParsePart(left, out int leftNumber);
            bool rightNumeric = TryParsePart(right, out int rightNumber);

            if (leftNumeric && rightNumeric)
            {
                result = leftNumber.CompareTo(rightNumber);
            }
            else if (leftNumeric)
            {
                // Numeric identifiers have lower precedence than alphanumeric ones.
                result = -1;
            }
            else if (rightNumeric)
            {
                result = 1;
            }
            else
            {
                result = string.Compare(left, right, StringComparison.Ordinal);
            }

            if (result != 0)
                return result;
        }

        return _preRelease.Length.CompareTo(other._preRelease.Length);
    }

    public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) =>
        obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Major, Minor, Patch, string.Join('.', _preRelease));

    public override string ToString()
    {
        string value = $"{Major}.{Minor}.{Patch}";

        return _preRelease.Length == 0
            ? value
            : value + "-" + string.Join('.', _preRelease);
    }

    private static bool TryParsePart(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
