using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>
/// One-time local-start request. It is not an operating-system background
/// task; overdue queued items start only while RangeDesk is running.
/// Time is captured with its chosen UTC offset to avoid silent DST shifts.
/// </summary>
public sealed record ScheduledDownload(string Destination, DateTimeOffset DueAt)
{
    public ScheduledDownload Validate()
    {
        if (string.IsNullOrWhiteSpace(Destination) || !Path.IsPathFullyQualified(Destination))
            throw new ArgumentException("Scheduled download needs an absolute destination path.");
        if (DueAt == default) throw new ArgumentException("Scheduled start needs a valid time.");
        return this with { Destination = Path.GetFullPath(Destination) };
    }
}
public static class ScheduledQueueStore
{
    public static IReadOnlyList<ScheduledDownload> Load(string file)
    {
        if (!File.Exists(file)) return [];
        var records = JsonSerializer.Deserialize<List<ScheduledDownload>>(File.ReadAllText(file))
            ?? throw new InvalidDataException("The schedule file is empty.");
        if (records.Count > 10000) throw new InvalidDataException("Too many scheduled downloads.");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            var valid = record.Validate();
            if (!unique.Add(valid.Destination)) throw new InvalidDataException("Duplicate scheduled destination.");
        }
        return records;
    }

    public static void Save(string file, IEnumerable<ScheduledDownload> items)
    {
        var records = items.Select(x => x.Validate()).ToArray();
        if (records.Length > 10000) throw new InvalidDataException("Too many scheduled downloads.");
        if (records.Select(x => x.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() != records.Length)
            throw new InvalidDataException("Duplicate scheduled destination.");
        var path = Path.GetFullPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static bool IsDue(ScheduledDownload item, DateTimeOffset now) => item.DueAt <= now;

    /// <summary>Reject the DST gap and ambiguous clock times; never silently change the user's intent.</summary>
    public static DateTimeOffset FromLocal(DateTime date, TimeSpan clock, TimeZoneInfo zone, DateTimeOffset now)
    {
        if (clock < TimeSpan.Zero || clock >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(clock));
        var local = DateTime.SpecifyKind(date.Date.Add(clock), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new ArgumentException("That local time is affected by daylight saving; choose another time.");
        var due = new DateTimeOffset(local, zone.GetUtcOffset(local));
        if (due <= now) throw new ArgumentException("Choose a future scheduled start.");
        return due;
    }
}
