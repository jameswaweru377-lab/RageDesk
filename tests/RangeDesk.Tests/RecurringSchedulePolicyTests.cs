using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class RecurringSchedulePolicyTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static ScheduledDownload Scheduled(DateTimeOffset first,
        ScheduleRecurrence recurrence) =>
        RecurringSchedulePolicy.Configure(
            new ScheduledDownload(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "recur-test.bin")), first),
            recurrence, Utc);

    [Fact]
    public void DailyWindowStartsPendingJobOnlyDuringOneHour()
    {
        var first = new DateTimeOffset(2026,10,20,9,0,0,TimeSpan.Zero);
        var item = Scheduled(first, ScheduleRecurrence.Daily);
        Assert.False(ScheduledQueueStore.IsDue(item,first.AddMinutes(-1)));
        Assert.True(ScheduledQueueStore.IsDue(item,first));
        Assert.True(ScheduledQueueStore.IsDue(item,first.AddDays(3).AddMinutes(30)));
        Assert.False(ScheduledQueueStore.IsDue(item,first.AddDays(3).AddHours(2)));
        Assert.False(ScheduledQueueStore.IsDue(item,first.AddDays(2).AddHours(-1)));
    }

    [Fact]
    public void WeekdaysSkipWeekendsAndWeeklyUsesAnchorDay()
    {
        var monday = new DateTimeOffset(2026,10,19,10,0,0,TimeSpan.Zero);
        var weekdays = Scheduled(monday, ScheduleRecurrence.Weekdays);
        Assert.True(ScheduledQueueStore.IsDue(weekdays,monday.AddDays(1)));
        Assert.False(ScheduledQueueStore.IsDue(weekdays,monday.AddDays(5)));
        Assert.True(ScheduledQueueStore.IsDue(weekdays,monday.AddDays(7)));
        var weekly = Scheduled(monday,ScheduleRecurrence.Weekly);
        Assert.False(ScheduledQueueStore.IsDue(weekly,monday.AddDays(1)));
        Assert.True(ScheduledQueueStore.IsDue(weekly,monday.AddDays(7)));
    }

    [Fact]
    public void ClosedAppMissedWindowDoesNotStartDaysLate()
    {
        var first = new DateTimeOffset(2026,10,20,14,0,0,TimeSpan.Zero);
        var item = Scheduled(first, ScheduleRecurrence.Weekly);
        Assert.False(ScheduledQueueStore.IsDue(item,first.AddDays(7).AddHours(5)));
        Assert.True(ScheduledQueueStore.IsDue(item,first.AddDays(14).AddMinutes(2)));
    }

    [Fact]
    public void ExistingOneTimeSchedulesStillStartWhenOverdue()
    {
        var item = new ScheduledDownload(Path.Combine(Path.GetTempPath(),"one-time.bin"),
            new DateTimeOffset(2026,10,16,10,0,0,TimeSpan.Zero));
        Assert.True(ScheduledQueueStore.IsDue(item,item.DueAt.AddDays(5)));
    }

    [Fact]
    public void RecurrenceAndTimezoneSurviveRestart()
    {
        var root=Path.Combine(Path.GetTempPath(),"rd-recurring-"+Guid.NewGuid());
        var file=Path.Combine(root,"queue.json");
        try
        {
            var item=Scheduled(new DateTimeOffset(2026,10,20,11,0,0,TimeSpan.Zero),
                ScheduleRecurrence.Weekdays);
            ScheduledQueueStore.Save(file,[item]);
            Assert.Equal(item,Assert.Single(ScheduledQueueStore.Load(file)));
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    [Fact]
    public void RecurrenceRejectsMissingTimezone()
    {
        var item=new ScheduledDownload(Path.Combine(Path.GetTempPath(),"invalid.bin"),
            DateTimeOffset.UtcNow.AddHours(3)){ Recurrence=ScheduleRecurrence.Daily };
        Assert.Throws<ArgumentException>(()=>item.Validate());
    }
}
