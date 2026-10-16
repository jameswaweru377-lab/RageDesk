using System.Net.Http.Headers;
using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>
/// Read-only inspection of private transfer checkpoints. The UI uses this on
/// restart and after cancellation; it never guesses progress from a directory
/// name, trusts overlapping ranges or repairs files by deleting user data.
/// Actual download/resume validation still belongs to the transfer engines.
/// </summary>
public static class DownloadCheckpointInspector
{
    public readonly record struct Progress(long Received, long Total);

    private sealed record FixedManifest(string Url, long Length, string ETag, int Connections);
    private sealed record AdaptivePiece(int Id, long From, long To);
    private sealed record AdaptiveManifest(string Url, long Length, string ETag,
        int Connections, List<AdaptivePiece> Pieces);

    public static Progress? Read(DownloadEntry entry)
    {
        var fixedDirectory = entry.Destination + ".rdsegments";
        var adaptiveDirectory = entry.Destination + ".rdadaptive";
        bool fixedExists = Directory.Exists(fixedDirectory);
        bool adaptiveExists = Directory.Exists(adaptiveDirectory);
        if (fixedExists && adaptiveExists)
            throw new InvalidDataException("Conflicting fixed and adaptive checkpoints.");
        if (fixedExists) return ReadFixed(entry.Url, fixedDirectory);
        if (adaptiveExists) return ReadAdaptive(entry.Url, adaptiveDirectory);
        return null;
    }

    private static T ReadManifest<T>(string directory) where T : class
    {
        string path = Path.Combine(directory, "manifest.json");
        if (!File.Exists(path)) throw new InvalidDataException("Checkpoint manifest is missing.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Checkpoint manifest is empty.");
    }

    private static void ValidateIdentity(string url, string expected, long length,
        int connections, string etag)
    {
        if (!string.Equals(url, expected, StringComparison.Ordinal) ||
            length <= 0 || connections is < 2 or > ParallelRangeTransfer.MaximumConnections ||
            !EntityTagHeaderValue.TryParse(etag, out var parsed) || parsed.IsWeak)
            throw new InvalidDataException("Checkpoint identity or HTTP validator is invalid.");
    }

    private static long CheckedLength(string path, long size)
    {
        if (!File.Exists(path)) return 0;
        long found = new FileInfo(path).Length;
        if (found < 0 || found > size)
            throw new InvalidDataException("Checkpoint part exceeds its assigned range.");
        return found;
    }

    private static Progress ReadFixed(string url, string directory)
    {
        var state = ReadManifest<FixedManifest>(directory);
        ValidateIdentity(state.Url, url, state.Length, state.Connections, state.ETag);
        long received = 0;
        for (int i = 0; i < state.Connections; i++)
        {
            var (first, last) =
                ParallelRangeTransfer.SegmentBounds(state.Length, state.Connections, i);
            received = checked(received + CheckedLength(
                Path.Combine(directory, $"{i:D2}.part"), last - first + 1));
        }
        return new Progress(received, state.Length);
    }

    private static Progress ReadAdaptive(string url, string directory)
    {
        var state = ReadManifest<AdaptiveManifest>(directory);
        ValidateIdentity(state.Url, url, state.Length, state.Connections, state.ETag);
        if (state.Pieces is null || state.Pieces.Count is < 2 or > 512 ||
            state.Pieces.Select(p => p.Id).Distinct().Count() != state.Pieces.Count ||
            state.Pieces.Any(p => p.Id is < 0 or >= 512))
            throw new InvalidDataException("Adaptive piece list is invalid.");
        long covered = 0;
        long received = 0;
        foreach (var piece in state.Pieces.OrderBy(p => p.From))
        {
            if (piece.From != covered || piece.To < piece.From || piece.To >= state.Length)
                throw new InvalidDataException("Adaptive checkpoint contains a gap or overlap.");
            covered = checked(piece.To + 1);
            received = checked(received + CheckedLength(
                Path.Combine(directory, $"{piece.Id:D4}.part"), piece.To - piece.From + 1));
        }
        if (covered != state.Length)
            throw new InvalidDataException("Adaptive checkpoint omits part of the file.");
        return new Progress(received, state.Length);
    }
}
