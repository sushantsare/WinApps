using System.Globalization;

namespace AppStore.Core;

/// <summary>Compares winget version strings ("131.0.6778.86", "3.0.21", "24.09", "1.2.3-beta").</summary>
public static class VersionCompare
{
    public static int Compare(string? a, string? b)
    {
        var pa = Split(a);
        var pb = Split(b);
        var n = Math.Max(pa.Count, pb.Count);
        for (var i = 0; i < n; i++)
        {
            var x = i < pa.Count ? pa[i] : "0";
            var y = i < pb.Count ? pb[i] : "0";
            var xNum = long.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var xi);
            var yNum = long.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out var yi);
            int c = xNum && yNum ? xi.CompareTo(yi) : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            if (c != 0)
            {
                return c;
            }
        }

        return 0;
    }

    /// <summary>The first number of a version ("3" in "3.0.21"), or null if there is none.</summary>
    public static long? Major(string? version)
    {
        var parts = Split(version);
        return parts.Count > 0 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : null;
    }

    /// <summary>True when the update moves to a new major version (3.x → 4.x).</summary>
    public static bool IsMajorJump(string? from, string? to)
    {
        var a = Major(from);
        var b = Major(to);
        return a is not null && b is not null && b > a;
    }

    private static List<string> Split(string? v)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(v))
        {
            return parts;
        }

        var s = v.Trim().TrimStart('v', 'V', '<', '>', '=', ' ');
        foreach (var p in s.Split(['.', '-', '+', '_', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            parts.Add(p);
        }

        return parts;
    }
}
