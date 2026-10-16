namespace RangeDesk.Core;

/// <summary>
/// Repeating means "start a pending queued download on the next matching
/// window", NOT re-download a completed file. A closed app cannot start tasks.
/// Missed recurring windows do not start hours or days late on next launch.
/// </summary>
public enum ScheduleRecurrence { Once = 0, Daily = 1, Weekdays = 2, Weekly = 3 }

public static class RecurringSchedulePolicy
{
    public static ScheduledDownload Configure(
        ScheduledDownload source, ScheduleRecurrence recurrence, TimeZoneInfo zone)
    {
        if (!Enum.IsDefined(recurrence)) throw new ArgumentOutOfRangeException(nameof(recurrence));
        return (source with
        {
            Recurrence = recurrence,
            TimeZoneId = recurrence == ScheduleRecurrence.Once ? null : zone.Id
        }).Validate();
    }

    public static bool ShouldStart(ScheduledDownload item, DateTimeOffset now)
    {
        item.Validate();
        if (item.Recurrence == ScheduleRecurrence.Once) return item.DueAt <= now;
        if (now < item.DueAt) return false;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(item.TimeZoneId!);
        var anchor = TimeZoneInfo.ConvertTime(item.DueAt, zone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        if (local.Date < anchor.Date) return false;
        if (item.Recurrence == ScheduleRecurrence.Weekdays &&
            local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        if (item.Recurrence == ScheduleRecurrence.Weekly &&
            local.DayOfWeek != anchor.DayOfWeek) return false;

        // The exact time recurs in the user's selected time zone (rather
        // than retaining the initial UTC offset across DST changes).
        var clock = anchor.TimeOfDay;
        var scheduledLocal = DateTime.SpecifyKind(local.Date.Add(clock), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(scheduledLocal) || zone.IsAmbiguousTime(scheduledLocal))
            return false; // skip DST transition rather than silently shifting
        var due = new DateTimeOffset(scheduledLocal, zone.GetUtcOffset(scheduledLocal));
        return now >= due && now < due.AddHours(1);
    }
}
