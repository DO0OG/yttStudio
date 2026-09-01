using System;
using System.Linq;

namespace YttStudio.App;

internal readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private readonly string major;
    private readonly string minor;
    private readonly string patch;
    private readonly string[] prereleaseIdentifiers;
    private readonly string? buildMetadata;

    private SemanticVersion(
        string major,
        string minor,
        string patch,
        string[] prereleaseIdentifiers,
        string? buildMetadata)
    {
        this.major = major;
        this.minor = minor;
        this.patch = patch;
        this.prereleaseIdentifiers = prereleaseIdentifiers;
        this.buildMetadata = buildMetadata;
    }

    internal static bool TryParse(string? value, out SemanticVersion version)
        => TryParse(value, requireVPrefix: false, out version);

    internal static bool TryParseTag(string? value, out SemanticVersion version)
        => TryParse(value, requireVPrefix: true, out version);

    internal static SemanticVersion Parse(string value)
        => TryParse(value, out SemanticVersion version)
            ? version
            : throw new FormatException($"유효하지 않은 SemVer다: {value}");

    public int CompareTo(SemanticVersion other)
    {
        int result = CompareNumericIdentifier(major, other.major);
        if (result != 0)
        {
            return result;
        }

        result = CompareNumericIdentifier(minor, other.minor);
        if (result != 0)
        {
            return result;
        }

        result = CompareNumericIdentifier(patch, other.patch);
        if (result != 0)
        {
            return result;
        }

        bool hasPrerelease = prereleaseIdentifiers is { Length: > 0 };
        bool otherHasPrerelease = other.prereleaseIdentifiers is { Length: > 0 };
        if (!hasPrerelease && !otherHasPrerelease)
        {
            return 0;
        }

        if (!hasPrerelease)
        {
            return 1;
        }

        if (!otherHasPrerelease)
        {
            return -1;
        }

        int count = Math.Min(prereleaseIdentifiers.Length, other.prereleaseIdentifiers.Length);
        for (int index = 0; index < count; index++)
        {
            result = ComparePrereleaseIdentifier(
                prereleaseIdentifiers[index],
                other.prereleaseIdentifiers[index]);
            if (result != 0)
            {
                return result;
            }
        }

        return prereleaseIdentifiers.Length.CompareTo(other.prereleaseIdentifiers.Length);
    }

    public bool Equals(SemanticVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj)
        => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(major, StringComparer.Ordinal);
        hash.Add(minor, StringComparer.Ordinal);
        hash.Add(patch, StringComparer.Ordinal);
        foreach (string identifier in prereleaseIdentifiers)
        {
            hash.Add(identifier, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        string value = $"{major}.{minor}.{patch}";
        if (prereleaseIdentifiers.Length > 0)
        {
            value += $"-{string.Join('.', prereleaseIdentifiers)}";
        }

        if (buildMetadata is not null)
        {
            value += $"+{buildMetadata}";
        }

        return value;
    }

    public static bool operator ==(SemanticVersion left, SemanticVersion right)
        => left.Equals(right);

    public static bool operator !=(SemanticVersion left, SemanticVersion right)
        => !left.Equals(right);

    public static bool operator <(SemanticVersion left, SemanticVersion right)
        => left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right)
        => left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right)
        => left.CompareTo(right) <= 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right)
        => left.CompareTo(right) >= 0;

    private static bool TryParse(
        string? value,
        bool requireVPrefix,
        out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        string normalized = value;
        bool hasVPrefix = normalized[0] == 'v';
        if (hasVPrefix)
        {
            normalized = normalized[1..];
        }
        else if (requireVPrefix)
        {
            return false;
        }

        if (normalized.Length == 0)
        {
            return false;
        }

        int plusIndex = normalized.IndexOf('+');
        string versionAndPrerelease = plusIndex >= 0
            ? normalized[..plusIndex]
            : normalized;
        string? build = plusIndex >= 0
            ? normalized[(plusIndex + 1)..]
            : null;
        if (plusIndex >= 0 &&
            (build!.Length == 0 || build.IndexOf('+') >= 0 || !AreIdentifiersValid(build, allowNumericLeadingZeros: true)))
        {
            return false;
        }

        int hyphenIndex = versionAndPrerelease.IndexOf('-');
        string core = hyphenIndex >= 0
            ? versionAndPrerelease[..hyphenIndex]
            : versionAndPrerelease;
        string[] prerelease = hyphenIndex >= 0
            ? versionAndPrerelease[(hyphenIndex + 1)..].Split('.')
            : [];

        if (!TryParseCore(core, out string? major, out string? minor, out string? patch) ||
            (hyphenIndex >= 0 &&
                (prerelease.Length == 0 ||
                 prerelease.Any(identifier => !IsValidIdentifier(identifier, allowNumericLeadingZeros: false)))))
        {
            return false;
        }

        version = new(major!, minor!, patch!, prerelease, build);
        return true;
    }

    private static bool TryParseCore(
        string core,
        out string? major,
        out string? minor,
        out string? patch)
    {
        major = null;
        minor = null;
        patch = null;
        string[] parts = core.Split('.');
        if (parts.Length != 3 ||
            parts.Any(part => !IsValidNumericCoreIdentifier(part)))
        {
            return false;
        }

        major = parts[0];
        minor = parts[1];
        patch = parts[2];
        return true;
    }

    private static bool AreIdentifiersValid(string value, bool allowNumericLeadingZeros)
        => value.Split('.').All(identifier =>
            IsValidIdentifier(identifier, allowNumericLeadingZeros));

    private static bool IsValidNumericCoreIdentifier(string value)
        => value.Length > 0 &&
            value.All(IsAsciiDigit) &&
            (value.Length == 1 || value[0] != '0');

    private static bool IsValidIdentifier(string value, bool allowNumericLeadingZeros)
    {
        if (value.Length == 0 ||
            !value.All(character => IsAsciiDigit(character) ||
                (character is >= 'A' and <= 'Z') ||
                (character is >= 'a' and <= 'z') ||
                character == '-'))
        {
            return false;
        }

        return allowNumericLeadingZeros ||
            !value.All(IsAsciiDigit) ||
            value.Length == 1 ||
            value[0] != '0';
    }

    private static bool IsAsciiDigit(char character)
        => character is >= '0' and <= '9';

    private static int CompareNumericIdentifier(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return left.Length.CompareTo(right.Length);
        }

        return string.CompareOrdinal(left, right);
    }

    private static int ComparePrereleaseIdentifier(string left, string right)
    {
        bool leftNumeric = left.All(IsAsciiDigit);
        bool rightNumeric = right.All(IsAsciiDigit);
        if (leftNumeric && rightNumeric)
        {
            return CompareNumericIdentifier(left, right);
        }

        if (leftNumeric)
        {
            return -1;
        }

        if (rightNumeric)
        {
            return 1;
        }

        return string.CompareOrdinal(left, right);
    }
}
