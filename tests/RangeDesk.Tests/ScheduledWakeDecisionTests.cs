using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class ScheduledWakeDecisionTests
{
    private static ScheduledDownload Job(DateTimeOffset due)
        => new(Path.Combine(Path.GetTempPath(), "RangeDeskWake-test.zip"), due);

    [Fact]
    public void DueTrackedIncompleteOneTimeJobTriggersWake()
    {
        var now=new DateTimeOffset(2026,10,20,10,0,0,TimeSpan.Zero);
        var job=Job(now.AddMinutes(-2));
        var tracked=new HashSet<string>(StringComparer.OrdinalIgnoreCase){job.Destination};
        Assert.True(ScheduledWakeDecision.ShouldWake([job],tracked,_=>false,now,false));
        Assert.False(ScheduledWakeDecision.ShouldWake([job],tracked,_=>false,now,true));
        Assert.False(ScheduledWakeDecision.ShouldWake([job],tracked,_=>true,now,false));
        Assert.False(ScheduledWakeDecision.ShouldWake([job],new HashSet<string>(),_=>false,now,false));
        Assert.False(ScheduledWakeDecision.ShouldWake([Job(now.AddHours(1))],tracked,_=>false,now,false));
    }

    [Fact]
    public void MissedRecurringWindowDoesNotWakeTheApplication()
    {
        var zone=TimeZoneInfo.Utc;
        var first = new DateTimeOffset(2026,10,19,9,0,0,TimeSpan.Zero);
        var weekly = RecurringSchedulePolicy.Configure(Job(first),ScheduleRecurrence.Weekly,zone);
        var tracked=new HashSet<string>(StringComparer.OrdinalIgnoreCase){weekly.Destination};
        Assert.False(ScheduledWakeDecision.ShouldWake([weekly],tracked,_=>false,
            first.AddDays(7).AddHours(2),false));
        Assert.True(ScheduledWakeDecision.ShouldWake([weekly],tracked,_=>false,
            first.AddDays(7).AddMinutes(4),false));
    }

    [Fact]
    public void NoSchedulesMeansNoBackgroundLaunch()
    {
        Assert.False(ScheduledWakeDecision.ShouldWake([],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), _=>false,
            DateTimeOffset.UtcNow,false));
    }
}
