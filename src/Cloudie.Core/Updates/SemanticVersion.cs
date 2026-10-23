using System.Globalization;

namespace Cloudie.Core.Updates;

/// <summary>
/// Just enough of SemVer 2.0 to order release tags and the informational version baked into the build:
/// "v1.2.3", "1.2.3-beta.1+sha" and "1.2" all parse; build metadata after "+" is ignored.
/// </summary>
public sealed record SemanticVersion(int Major, int Minor, int Patch, string? Prerelease = null) : IComparable<SemanticVersion>
{
    public bool IsPrerelease => Prerelease is { Length: > 0 };

    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var span = text.Trim();
        if (span.StartsWith('v') || span.StartsWith('V')) span = span[1..];
        var plus = span.IndexOf('+');
        if (plus >= 0) span = span[..plus];
        string? prerelease = null;
        var dash = span.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = span[(dash + 1)..];
            span = span[..dash];
        }
        var parts = span.Split('.');
        if (parts.Length is < 1 or > 4) return false;
        var numbers = new int[3];
        for (var i = 0; i < Math.Min(3, parts.Length); i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }
        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], string.IsNullOrEmpty(prerelease) ? null : prerelease);
        return true;
    }

    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"Not a version: '{text}'");

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        var order = Major.CompareTo(other.Major);
        if (order != 0) return order;
        order = Minor.CompareTo(other.Minor);
        if (order != 0) return order;
        order = Patch.CompareTo(other.Patch);
        if (order != 0) return order;
        // A pre-release sorts before the release it precedes (1.0.0-rc.1 < 1.0.0).
        if (IsPrerelease != other.IsPrerelease) return IsPrerelease ? -1 : 1;
        return IsPrerelease ? ComparePrerelease(Prerelease!, other.Prerelease!) : 0;
    }

    private static int ComparePrerelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            if (i >= a.Length) return -1;
            if (i >= b.Length) return 1;
            var aNumeric = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var aNumber);
            var bNumeric = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bNumber);
            var order = aNumeric && bNumeric ? aNumber.CompareTo(bNumber)
                : aNumeric ? -1
                : bNumeric ? 1
                : string.CompareOrdinal(a[i], b[i]);
            if (order != 0) return order;
        }
        return 0;
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";
}
