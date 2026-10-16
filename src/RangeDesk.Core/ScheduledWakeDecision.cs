namespace RangeDesk.Core;

/// <summary>
/// Read-only decision for an opt-in Windows Scheduled Task to wake the GUI.
/// It never starts a network connection or writes a download file itself.
/// </summary>
public static class ScheduledWakeDecision
{
    public static bool ShouldWake(IEnumerable<ScheduledDownload> schedules,
        ISet<string> savedDownloadDestinations, Func<string,bool> finalFileExists,
        DateTimeOffset now, bool rangeDeskAlreadyRunning)
    {
        if (rangeDeskAlreadyRunning) return false;
        foreach (var schedule in schedules)
        {
            // Ignore invalid or stale tasks: only tracked downloads with a
            // missing completed file are eligible for an automatic wake.
            if (!savedDownloadDestinations.Contains(schedule.Destination) ||
                finalFileExists(schedule.Destination))
                continue;
            try
            {
                if (ScheduledQueueStore.IsDue(schedule, now)) return true;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidTimeZoneException or TimeZoneNotFoundException)
            {
                // A malformed schedule must not prevent other scheduled jobs.
            }
        }
        return false;
    }
}
