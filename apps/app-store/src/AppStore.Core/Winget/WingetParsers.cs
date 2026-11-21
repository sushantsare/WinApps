using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AppStore.Core.Models;

namespace AppStore.Core.Winget;

/// <summary>Reads winget.exe's text output (tables, "show" details, progress bars).</summary>
public static partial class WingetParsers
{
    /// <summary>Parses every table in the output. Each row maps header → cell text.</summary>
    public static List<List<Dictionary<string, string>>> ParseTables(string output)
    {
        var tables = new List<List<Dictionary<string, string>>>();
        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            var dashes = lines[i].Trim();
            if (dashes.Length < 10 || dashes.Any(c => c != '-'))
            {
                continue;
            }

            var header = CleanLine(lines[i - 1]);
            var cols = HeaderColumns(header);
            if (cols.Count < 2)
            {
                continue;
            }

            var rows = new List<Dictionary<string, string>>();
            for (var j = i + 1; j < lines.Length; j++)
            {
                var line = CleanLine(lines[j]);
                if (line.Trim().Length == 0)
                {
                    break;
                }

                if (DisplayWidth(line) < cols[1].Start)
                {
                    // Footer text such as "3 upgrades available."
                    break;
                }

                var row = SplitRow(line, cols);
                if (row.Values.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                rows.Add(row);
            }

            tables.Add(rows);
            i += rows.Count;
        }

        return tables;
    }

    public static List<PackageItem> ParseUpgradeTable(string output)
    {
        var tables = ParseTables(output);
        var list = new List<PackageItem>();
        if (tables.Count == 0)
        {
            return list;
        }

        foreach (var r in tables[0])
        {
            var id = Get(r, "Id");
            var avail = Get(r, "Available");
            if (id.Length == 0 || avail.Length == 0)
            {
                continue;
            }

            list.Add(new PackageItem(id, Get(r, "Name"))
            {
                Version = Get(r, "Version"),
                Available = avail,
                Source = Get(r, "Source"),
            });
        }

        return list;
    }

    public static List<PackageItem> ParseListTable(string output)
    {
        var list = new List<PackageItem>();
        foreach (var table in ParseTables(output))
        {
            foreach (var r in table)
            {
                var id = Get(r, "Id");
                if (id.Length == 0)
                {
                    continue;
                }

                list.Add(new PackageItem(id, Get(r, "Name"))
                {
                    Version = Get(r, "Version"),
                    Available = Get(r, "Available"),
                    Source = Get(r, "Source"),
                });
            }
        }

        return list;
    }

    public static List<PackageItem> ParseSearchTable(string output)
    {
        var list = new List<PackageItem>();
        var tables = ParseTables(output);
        if (tables.Count == 0)
        {
            return list;
        }

        foreach (var r in tables[0])
        {
            var id = Get(r, "Id");
            if (id.Length == 0)
            {
                continue;
            }

            list.Add(new PackageItem(id, Get(r, "Name"))
            {
                Version = Get(r, "Version"),
                Source = Get(r, "Source"),
            });
        }

        return list;
    }

    /// <summary>Parses "winget show" output into details.</summary>
    public static PackageDetails ParseShow(string output)
    {
        var d = new PackageDetails();
        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(CleanLine).ToList();
        string? key = null;
        var values = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        var inInstaller = false;
        foreach (var raw in lines)
        {
            if (raw.StartsWith("Found ", StringComparison.Ordinal))
            {
                var m = FoundRegex().Match(raw);
                if (m.Success)
                {
                    d.Name = m.Groups["name"].Value.Trim();
                    d.Id = m.Groups["id"].Value.Trim();
                }

                continue;
            }

            if (raw.Trim().Length == 0)
            {
                continue;
            }

            var indented = raw.StartsWith(' ');
            var line = raw.Trim();
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var looksLikeKey = colon > 0 && colon < 40 && !line.StartsWith("http", StringComparison.OrdinalIgnoreCase) && KeyRegex().IsMatch(line[..colon]);

            if (!indented && looksLikeKey)
            {
                key = line[..colon].Trim();
                inInstaller = key.Equals("Installer", StringComparison.OrdinalIgnoreCase);
                values[key] = new StringBuilder(line[(colon + 1)..].Trim());
            }
            else if (indented && inInstaller && looksLikeKey)
            {
                key = "Installer " + line[..colon].Trim();
                values[key] = new StringBuilder(line[(colon + 1)..].Trim());
            }
            else if (key is not null)
            {
                var sb = values[key];
                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(line);
            }
        }

        string V(string k) => values.TryGetValue(k, out var sb) ? sb.ToString().Trim() : string.Empty;
        d.Version = V("Version");
        d.Publisher = V("Publisher");
        d.Description = V("Description");
        if (d.Description.Length == 0)
        {
            d.Description = V("Short Description");
        }

        d.Homepage = V("Homepage");
        if (d.Homepage.Length == 0)
        {
            d.Homepage = V("Publisher Url");
        }

        d.License = V("License");
        d.ReleaseNotes = V("Release Notes");
        d.ReleaseNotesUrl = V("Release Notes Url");
        d.InstallerType = V("Installer Installer Type");
        d.InstallerUrl = V("Installer Installer Url");
        d.InstallerScope = V("Installer Installer Scope");
        d.Tags = V("Tags").Replace('\n', ',').Replace(",,", ",", StringComparison.Ordinal);
        return d;
    }

    /// <summary>Parses "winget show --versions" output (one version per line after the dashes).</summary>
    public static List<string> ParseVersions(string output)
    {
        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => CleanLine(l).Trim()).ToList();
        var start = lines.FindIndex(l => l.Length >= 3 && l.All(c => c == '-'));
        return start < 0 ? [] : lines.Skip(start + 1).Where(l => l.Length > 0 && !l.Contains(' ', StringComparison.Ordinal)).ToList();
    }

    /// <summary>Turns one line of install output into progress (percent and/or phase), or null.</summary>
    public static OpProgress? ParseProgress(string line)
    {
        var s = CleanLine(line).Trim();
        if (s.Length == 0)
        {
            return null;
        }

        var m = SizeProgressRegex().Match(s);
        if (m.Success)
        {
            var done = ToBytes(m.Groups["a"].Value, m.Groups["au"].Value);
            var total = ToBytes(m.Groups["b"].Value, m.Groups["bu"].Value);
            if (total > 0)
            {
                var pct = Math.Clamp(done / total * 100, 0, 100);
                return new OpProgress { Percent = pct, Phase = string.Create(CultureInfo.CurrentCulture, $"Downloading… {pct:0}% · {m.Groups["a"].Value} {m.Groups["au"].Value} of {m.Groups["b"].Value} {m.Groups["bu"].Value}") };
            }
        }

        m = PercentRegex().Match(s);
        if (m.Success && s.Contains('█', StringComparison.Ordinal))
        {
            var pct = double.Parse(m.Groups["p"].Value, CultureInfo.InvariantCulture);
            return new OpProgress { Percent = pct, Phase = string.Create(CultureInfo.CurrentCulture, $"Working… {pct:0}%") };
        }

        if (s.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase))
        {
            return new OpProgress { Percent = 0, Phase = "Downloading…" };
        }

        if (s.Contains("Starting package install", StringComparison.OrdinalIgnoreCase) || s.Contains("Installing", StringComparison.OrdinalIgnoreCase) && s.EndsWith("...", StringComparison.Ordinal))
        {
            return new OpProgress { Percent = -1, Phase = "Installing…" };
        }

        if (s.Contains("Starting package uninstall", StringComparison.OrdinalIgnoreCase))
        {
            return new OpProgress { Percent = -1, Phase = "Uninstalling…" };
        }

        if (s.Contains("verified installer hash", StringComparison.OrdinalIgnoreCase))
        {
            return new OpProgress { Percent = 100, Phase = "Download verified" };
        }

        return null;
    }

    /// <summary>The last meaningful lines of output, for error details.</summary>
    public static string Tail(string output, int lines = 12)
    {
        var all = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(l => CleanLine(l).Trim())
            .Where(l => l.Length > 1 && !l.Contains('█', StringComparison.Ordinal) && !l.Contains('▒', StringComparison.Ordinal))
            .ToList();
        return string.Join(Environment.NewLine, all.Skip(Math.Max(0, all.Count - lines)));
    }

    /// <summary>Removes ANSI escapes and the spinner characters winget prints before real output.</summary>
    public static string CleanLine(string s)
    {
        s = AnsiRegex().Replace(s, string.Empty);
        var t = s.TrimStart();
        while (t.Length > 0 && t[0] is '-' or '\\' or '|' or '/' && (t.Length == 1 || t[1] == ' ' || t.Length > 1 && t[1] is '-' or '\\' or '|' or '/'))
        {
            if (t.All(c => c == '-'))
            {
                return s; // a dashes line, not a spinner
            }

            t = t[1..].TrimStart();
            s = t;
        }

        return s;
    }

    private static string Get(Dictionary<string, string> r, string key) => r.TryGetValue(key, out var v) ? v.Trim() : string.Empty;

    private sealed record Column(string Name, int Start);

    private static List<Column> HeaderColumns(string header)
    {
        var cols = new List<Column>();
        var width = 0;
        var inWord = false;
        var start = 0;
        var word = new StringBuilder();
        foreach (var c in header)
        {
            if (c != ' ')
            {
                if (!inWord)
                {
                    inWord = true;
                    start = width;
                    word.Clear();
                }

                word.Append(c);
            }
            else if (inWord)
            {
                inWord = false;
                cols.Add(new Column(word.ToString(), start));
            }

            width += CharWidth(c);
        }

        if (inWord)
        {
            cols.Add(new Column(word.ToString(), start));
        }

        return cols;
    }

    private static Dictionary<string, string> SplitRow(string line, List<Column> cols)
    {
        var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder[cols.Count];
        for (var k = 0; k < cols.Count; k++)
        {
            sb[k] = new StringBuilder();
        }

        var width = 0;
        var col = 0;
        foreach (var c in line)
        {
            while (col + 1 < cols.Count && width >= cols[col + 1].Start)
            {
                col++;
            }

            sb[col].Append(c);
            width += CharWidth(c);
        }

        for (var k = 0; k < cols.Count; k++)
        {
            cells[cols[k].Name] = sb[k].ToString().Trim();
        }

        return cells;
    }

    internal static int DisplayWidth(string s) => s.Sum(CharWidth);

    /// <summary>East Asian wide characters take two console columns.</summary>
    private static int CharWidth(char c) =>
        c >= 0x1100 && (c <= 0x115F || c is >= (char)0x2E80 and <= (char)0xA4CF || c is >= (char)0xAC00 and <= (char)0xD7A3 ||
                        c is >= (char)0xF900 and <= (char)0xFAFF || c is >= (char)0xFE30 and <= (char)0xFE4F ||
                        c is >= (char)0xFF00 and <= (char)0xFF60 || c is >= (char)0xFFE0 and <= (char)0xFFE6) ? 2 : 1;

    private static double ToBytes(string n, string unit)
    {
        var v = double.Parse(n, CultureInfo.InvariantCulture);
        return unit.ToUpperInvariant() switch
        {
            "KB" => v * 1024,
            "MB" => v * 1024 * 1024,
            "GB" => v * 1024 * 1024 * 1024,
            _ => v,
        };
    }

    [GeneratedRegex(@"^Found (?<name>.*) \[(?<id>[^\]]+)\]")]
    private static partial Regex FoundRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z ]{0,38}$")]
    private static partial Regex KeyRegex();

    [GeneratedRegex(@"(?<a>\d+(\.\d+)?)\s*(?<au>[KMG]?B)\s*/\s*(?<b>\d+(\.\d+)?)\s*(?<bu>[KMG]?B)", RegexOptions.IgnoreCase)]
    private static partial Regex SizeProgressRegex();

    [GeneratedRegex(@"(?<p>\d{1,3})\s*%")]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiRegex();
}
