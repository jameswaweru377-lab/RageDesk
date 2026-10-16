namespace RangeDesk.Core;

/// <summary>
/// Explicitly selected, non-executable post-download actions. Scripts, shell
/// commands, automatic file opening and website callbacks are unsupported.
/// </summary>
public enum SafePostDownloadAction
{
    None = 0,
    PlayNotificationSound = 1,
    OpenContainingFolder = 2
}

public static class PostDownloadActionPolicy
{
    public static SafePostDownloadAction Validate(SafePostDownloadAction action)
    {
        if (!Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(action),
                "Select a supported, non-executable post-download action.");
        return action;
    }

    public static bool ShouldRun(SafePostDownloadAction action, DownloadStatus previous,
        DownloadStatus current, bool finalFileExists)
    {
        Validate(action);
        return action != SafePostDownloadAction.None &&
            previous != DownloadStatus.Completed &&
            current == DownloadStatus.Completed && finalFileExists;
    }
}
