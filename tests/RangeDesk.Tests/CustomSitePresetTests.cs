using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class CustomSitePresetTests
{
    [Fact]
    public void HostMatchesOnlyExactDomainAndSubdomains()
    {
        var p = new CustomSitePreset("My Docs", "example.com").Validate();
        Assert.True(p.MatchesHost("example.com"));
        Assert.True(p.MatchesHost("dl.example.com"));
        Assert.False(p.MatchesHost("notexample.com"));
        Assert.False(p.MatchesHost("example.com.evil"));
    }

    [Theory]
    [InlineData("https://example.com/path")]
    [InlineData("example.com/path")]
    [InlineData("*.example.com")]
    [InlineData("localhost")]
    public void RejectInvalidHostSuffixes(string host)
    {
        Assert.Throws<ArgumentException>(() => new CustomSitePreset("Test",host).Validate());
    }

    [Fact]
    public void BuiltinNameCannotBeOverridden()
    {
        Assert.Throws<ArgumentException>(() =>
            new CustomSitePreset("GitHub","evil.example").Validate());
    }

    [Fact]
    public void PresetsPersistAndCanBeDeleted()
    {
        string root=Path.Combine(Path.GetTempPath(),"rd-sites-"+Guid.NewGuid());
        string file=Path.Combine(root,"sites-v5.json");
        try
        {
            var p=new CustomSitePreset("Docs","example.com");
            CustomSitePresetStore.Save(file,[p]);
            Assert.Equal(p,Assert.Single(CustomSitePresetStore.Load(file)));
            CustomSitePresetStore.Save(file,[]);
            Assert.Empty(CustomSitePresetStore.Load(file));
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
}
