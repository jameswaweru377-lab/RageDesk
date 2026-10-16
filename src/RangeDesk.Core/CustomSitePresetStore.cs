using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>Host-only filter; never connects to the site or captures browser data.</summary>
public sealed record CustomSitePreset(string Name, string HostSuffix)
{
    public CustomSitePreset Validate()
    {
        string name = (Name ?? "").Trim();
        string suffix = (HostSuffix ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        if (name.Length is < 1 or > 40 || name.Any(char.IsControl))
            throw new ArgumentException("The site name must be 1–40 printable characters.");
        if (SitePresets.Names.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("That preset name is already reserved.");
        if (suffix.Length is < 3 or > 253 || !suffix.Contains('.') ||
            Uri.CheckHostName(suffix) != UriHostNameType.Dns)
            throw new ArgumentException("Enter a DNS host such as example.com, without a scheme or path.");
        return this with { Name = name, HostSuffix = suffix };
    }

    public bool MatchesHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        host = host.TrimEnd('.');
        return host.Equals(HostSuffix, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + HostSuffix, StringComparison.OrdinalIgnoreCase);
    }
}

public static class CustomSitePresetStore
{
    public static IReadOnlyList<CustomSitePreset> Load(string file)
    {
        if (!File.Exists(file)) return [];
        var items = JsonSerializer.Deserialize<List<CustomSitePreset>>(File.ReadAllText(file))
            ?? throw new InvalidDataException("The site presets file is empty.");
        ValidateAll(items);
        return items.Select(x => x.Validate()).ToArray();
    }

    public static void Save(string file, IEnumerable<CustomSitePreset> input)
    {
        var items = input.Select(x => x.Validate()).ToArray();
        ValidateAll(items);
        var path = Path.GetFullPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(items,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static void ValidateAll(IReadOnlyList<CustomSitePreset> items)
    {
        if (items.Count > 100) throw new InvalidDataException("Too many site presets.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var valid = item.Validate();
            if (!names.Add(valid.Name)) throw new InvalidDataException("Duplicate site preset name.");
        }
    }
}
