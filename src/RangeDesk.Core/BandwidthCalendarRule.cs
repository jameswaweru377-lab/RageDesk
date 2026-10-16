using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>
/// Repeating local-time bandwidth cap. Windows start/end times are evaluated
/// in the current PC time zone. No schedule may increase a user's existing
/// per-download speed cap.
/// </summary>
public sealed record BandwidthCalendarRule(
    string Name, string HostSuffix, TimeOnly Start, TimeOnly End, int CapKiBps)
{
    public BandwidthCalendarRule Validate()
    {
        string name=(Name??"").Trim();
        string host=(HostSuffix??"").Trim().TrimEnd('.').ToLowerInvariant();
        if (name.Length is < 1 or > 48 || name.Any(char.IsControl))
            throw new ArgumentException("Bandwidth rule name must be 1–48 printable characters.");
        if (host.Length>0 && (host.Length>253 || !host.Contains('.') ||
            Uri.CheckHostName(host)!=UriHostNameType.Dns))
            throw new ArgumentException("Enter a valid host suffix or leave blank for all sites.");
        if (Start==End)
            throw new ArgumentException("Start and end times must differ.");
        if (CapKiBps is < 32 or > 1048576)
            throw new ArgumentOutOfRangeException(nameof(CapKiBps),
                "Choose a bandwidth cap between 32 and 1,048,576 KiB/s.");
        return this with {Name=name,HostSuffix=host};
    }

    public bool Matches(string host, DateTimeOffset now, TimeZoneInfo zone)
    {
        Validate();
        if (HostSuffix.Length>0 &&
            !host.Equals(HostSuffix,StringComparison.OrdinalIgnoreCase) &&
            !host.EndsWith("."+HostSuffix,StringComparison.OrdinalIgnoreCase))
            return false;
        var local = TimeOnly.FromTimeSpan(TimeZoneInfo.ConvertTime(now,zone).TimeOfDay);
        return Start<End ? local>=Start && local<End :
            local>=Start || local<End;
    }
}

public static class BandwidthCalendarPolicy
{
    public static int EffectiveLimit(int originalKiBps, string host,
        DateTimeOffset now, TimeZoneInfo zone, IEnumerable<BandwidthCalendarRule> rules)
    {
        if (originalKiBps < 0 || originalKiBps > 1048576)
            throw new ArgumentOutOfRangeException(nameof(originalKiBps));
        foreach(var rule in rules)
        {
            var valid=rule.Validate();
            if (!valid.Matches(host,now,zone)) continue;
            return originalKiBps==0 ? valid.CapKiBps : Math.Min(originalKiBps,valid.CapKiBps);
        }
        return originalKiBps;
    }
}

public static class BandwidthCalendarStore
{
    public static IReadOnlyList<BandwidthCalendarRule> Load(string file)
    {
        if (!File.Exists(file)) return [];
        var rules=JsonSerializer.Deserialize<List<BandwidthCalendarRule>>(File.ReadAllText(file))
            ?? throw new InvalidDataException("The bandwidth calendar is empty.");
        ValidateRules(rules);
        return rules.Select(r=>r.Validate()).ToArray();
    }

    public static void Save(string file,IEnumerable<BandwidthCalendarRule> input)
    {
        var rules=input.Select(r=>r.Validate()).ToArray();
        ValidateRules(rules);
        var path=Path.GetFullPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary=path+".tmp";
        try {
            File.WriteAllText(temporary,JsonSerializer.Serialize(rules,
                new JsonSerializerOptions{WriteIndented=true}));
            File.Move(temporary,path,overwrite:true);
        }
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static void ValidateRules(IReadOnlyList<BandwidthCalendarRule> rules)
    {
        if(rules.Count>50)throw new InvalidDataException("Too many bandwidth windows.");
        var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var rule in rules)
            if(!unique.Add(rule.Validate().Name))
                throw new InvalidDataException("Bandwidth window names must be unique.");
    }
}
