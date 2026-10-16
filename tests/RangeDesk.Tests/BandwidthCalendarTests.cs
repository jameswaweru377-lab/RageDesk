using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class BandwidthCalendarTests
{
    private static readonly TimeZoneInfo Utc=TimeZoneInfo.Utc;

    [Fact]
    public void OvernightWindowMatchesBothSidesOfMidnight()
    {
        var rule=new BandwidthCalendarRule("Night","",new TimeOnly(22,0),new TimeOnly(6,0),256);
        var t=new DateTimeOffset(2026,10,20,23,0,0,TimeSpan.Zero);
        Assert.True(rule.Matches("example.com",t,Utc));
        Assert.True(rule.Matches("example.com",t.AddHours(5),Utc));
        Assert.False(rule.Matches("example.com",t.AddHours(9),Utc));
    }

    [Fact]
    public void HostFiltersRequireRealDomainBoundaryAndNeverRaiseOriginalLimit()
    {
        var rule=new BandwidthCalendarRule("Docs","example.com",
            new TimeOnly(9,0),new TimeOnly(17,0),128);
        var t=new DateTimeOffset(2026,10,20,12,0,0,TimeSpan.Zero);
        Assert.Equal(128,BandwidthCalendarPolicy.EffectiveLimit(0,"cdn.example.com",t,Utc,[rule]));
        Assert.Equal(64,BandwidthCalendarPolicy.EffectiveLimit(64,"example.com",t,Utc,[rule]));
        Assert.Equal(512,BandwidthCalendarPolicy.EffectiveLimit(512,"evil-example.com",t,Utc,[rule]));
        Assert.Equal(512,BandwidthCalendarPolicy.EffectiveLimit(512,"example.com.evil",t,Utc,[rule]));
        Assert.Equal(512,BandwidthCalendarPolicy.EffectiveLimit(512,"example.com",t.AddHours(8),Utc,[rule]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(1048577)]
    public void UnsafeCapsRejected(int cap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            new BandwidthCalendarRule("TooFast","",new TimeOnly(9,0),
                new TimeOnly(12,0),cap).Validate());
    }

    [Fact]
    public void RulesPersistInOrderWithoutEditingDownloads()
    {
        string root=Path.Combine(Path.GetTempPath(),"rd-band-"+Guid.NewGuid());
        string file=Path.Combine(root,"calendar.json");
        try {
            var a=new BandwidthCalendarRule("Office","",new TimeOnly(8,0),new TimeOnly(17,0),256);
            var b=new BandwidthCalendarRule("Night","example.com",new TimeOnly(17,0),new TimeOnly(8,0),512);
            BandwidthCalendarStore.Save(file,[a,b]);
            Assert.Equal(["Office","Night"],BandwidthCalendarStore.Load(file).Select(r=>r.Name));
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
