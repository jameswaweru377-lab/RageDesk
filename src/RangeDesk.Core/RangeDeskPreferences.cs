using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>
/// Local-only defaults for NEW downloads. Existing transfer entries retain
/// their own settings and resumable checkpoints.
/// </summary>
public sealed record RangeDeskPreferences
{
    public string DownloadFolder { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public int DefaultConnections { get; init; } = 8;
    public int DefaultBandwidthKiBps { get; init; }
    public bool StartImmediately { get; init; } = true;
    public int MaxConcurrentDownloads { get; init; } = 2;

    public RangeDeskPreferences Validate()
    {
        if (string.IsNullOrWhiteSpace(DownloadFolder) ||
            !Path.IsPathFullyQualified(DownloadFolder))
            throw new ArgumentException("Choose a full local download folder.");

        string folder;
        try { folder = Path.GetFullPath(DownloadFolder.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("The default download folder is not a valid path.", ex);
        }

        if (MaxConcurrentDownloads is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentDownloads),
                "Simultaneous downloads must be between 1 and 4.");
        if (DefaultConnections is not (1 or 2 or 4 or 8 or 16 or 32))
            throw new ArgumentOutOfRangeException(nameof(DefaultConnections),
                "Connections must be 1, 2, 4, 8, 16 or 32.");
        if (DefaultBandwidthKiBps is < 0 or > 1048576)
            throw new ArgumentOutOfRangeException(nameof(DefaultBandwidthKiBps),
                "The default speed limit must be from 0 to 1,048,576 KiB/s.");

        return this with { DownloadFolder = folder };
    }
}

/// <summary>
/// Durable, atomic, version-independent preferences file, separate from
/// the download catalog. No URLs, headers or credentials are stored here.
/// </summary>
public static class RangeDeskPreferencesStore
{
    public static RangeDeskPreferences Load(string filePath)
    {
        if (!File.Exists(filePath)) return new RangeDeskPreferences().Validate();
        var settings = JsonSerializer.Deserialize<RangeDeskPreferences>(File.ReadAllText(filePath))
            ?? throw new InvalidDataException("The preferences file is empty.");
        return settings.Validate();
    }

    public static void Save(string filePath, RangeDeskPreferences preferences)
    {
        var valid = preferences.Validate();
        string destination = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(valid,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
