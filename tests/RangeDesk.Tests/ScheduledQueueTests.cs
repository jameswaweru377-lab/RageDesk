using System.Text.Json;
using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class ScheduledQueueTests
{
    [Fact]
    public void DueTimeWorksWithUtcOffsets()
    {
        var d = new ScheduledDownload(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "queued.bin")),
            new DateTimeOffset(2026, 10, 20, 19, 0, 0, TimeSpan.FromHours(3)));
        Assert.False(ScheduledQueueStore.IsDue(d, new DateTimeOffset(2026,10,20,15,59,59,TimeSpan.Zero)));
        Assert.True(ScheduledQueueStore.IsDue(d, new DateTimeOffset(2026,10,20,16,0,0,TimeSpan.Zero)));
    }

    [Fact]
    public void SchedulingMustBeFuture()
    {
        var now = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() =>
            ScheduledQueueStore.FromLocal(new DateTime(2026,10,16),new TimeSpan(9,0,0),TimeZoneInfo.Utc,now));
    }

    [Fact]
    public void SchedulePersistsAndCanBeRemoved()
    {
        var root = Path.Combine(Path.GetTempPath(), "rangedesk-schedule-" + Guid.NewGuid());
        var file = Path.Combine(root, "schedules-v5.json");
        try
        {
            var item = new ScheduledDownload(Path.Combine(root, "download.bin"),
                new DateTimeOffset(2026,10,25,10,0,0,TimeSpan.Zero));
            ScheduledQueueStore.Save(file,[item]);
            Assert.Equal(item, Assert.Single(ScheduledQueueStore.Load(file)));
            ScheduledQueueStore.Save(file,[]);
            Assert.Empty(ScheduledQueueStore.Load(file));
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }

    [Fact]
    public void DuplicatesAreNotAllowed()
    {
        var f = Path.Combine(Path.GetTempPath(), "dup.bin");
        var s = new ScheduledDownload(f, DateTimeOffset.UtcNow.AddHours(1));
        Assert.Throws<InvalidDataException>(() =>
            ScheduledQueueStore.Save(Path.Combine(Path.GetTempPath(),"schedule-test-" + Guid.NewGuid(),"schedules.json"),[s,s]));
    }


    [Fact]
    public void LoadedLegacyPathAliasesMatchTrackedClosedAppSchedules()
    {
        var root = Path.Combine(Path.GetTempPath(), "rangedesk-schedule-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var alias = Path.Combine(root, "old-folder", "..", "download.bin");
            var canonical = Path.GetFullPath(alias);
            var due = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
            var file = Path.Combine(root, "legacy-schedules.json");
            File.WriteAllText(file, JsonSerializer.Serialize(new[] { new ScheduledDownload(alias, due) }));

            var loaded = Assert.Single(ScheduledQueueStore.Load(file));
            Assert.Equal(canonical, loaded.Destination);
            var tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { canonical };
            Assert.True(ScheduledWakeDecision.ShouldWake([loaded], tracked,
                _ => false, due.AddMinutes(2), false));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LoadedLegacyAliasesCannotBypassDuplicateScheduleProtection()
    {
        var root = Path.Combine(Path.GetTempPath(), "rangedesk-schedule-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var canonical = Path.Combine(root, "duplicate.bin");
            var alias = Path.Combine(root, "old-folder", "..", "duplicate.bin");
            var due = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
            var file = Path.Combine(root, "legacy-schedules.json");
            File.WriteAllText(file, JsonSerializer.Serialize(new[]
            {
                new ScheduledDownload(canonical, due),
                new ScheduledDownload(alias, due)
            }));

            Assert.Throws<InvalidDataException>(() => ScheduledQueueStore.Load(file));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AmbiguousDaylightSavingTimeIsRejected()
    {
        // Eastern US clock repeats 01:30 when DST ends.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        Assert.Throws<ArgumentException>(() =>
            ScheduledQueueStore.FromLocal(new DateTime(2026,11,1),new TimeSpan(1,30,0),
                zone, new DateTimeOffset(2026,10,20,10,0,0,TimeSpan.Zero)));
    }
}
