using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class DownloadAutomationRuleTests
{
    [Fact]
    public void MatchingDomainAndExtensionAppliesOnlyToNewDownload()
    {
        string root = Path.Combine(Path.GetTempPath(), "RangeDeskAuto-" + Guid.NewGuid());
        var rule = new DownloadAutomationRule("Archives", "example.com", ".zip",
            "Archives", "Research", "ZIP files").Validate();
        var d = DownloadAutomationPolicy.Apply("https://cdn.example.com/file.zip",
            Path.Combine(root, "file.zip"), "Other", "Work", root, [rule]);
        Assert.Equal(Path.Combine(root,"ZIP files","file.zip"),d.Destination);
        Assert.Equal("Archives", d.Category);
        Assert.Equal("Work, Research",d.Labels);
        Assert.Equal("Archives",d.MatchedRule);
        Assert.Null(DownloadAutomationPolicy.Apply("https://example.com.evil/file.zip",
            Path.Combine(root,"file.zip"),"Other","",root,[rule]).MatchedRule);
    }

    [Fact]
    public void AManuallyChosenDownloadFolderIsNeverMoved()
    {
        var root = Path.Combine(Path.GetTempPath(),"rd-default");
        var manual = Path.Combine(Path.GetTempPath(),"rd-manual", "file.pdf");
        var rule = new DownloadAutomationRule("Docs", "", ".pdf", "Documents", "", "PDFs");
        var d = DownloadAutomationPolicy.Apply("https://example.com/file.pdf",manual,"Other","",root,[rule]);
        Assert.Equal(manual,d.Destination);
        Assert.Equal("Documents",d.Category);
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("foo/bar")]
    [InlineData("foo\\bar")]
    public void UnsafeFolderNamesAreRejected(string path)
    {
        Assert.Throws<ArgumentException>(() =>
            new DownloadAutomationRule("Rule", "example.com", "", "Other", "", path).Validate());
    }

    [Fact]
    public void OrderAndPersistenceSurviveRestart()
    {
        var root=Path.Combine(Path.GetTempPath(),"rd-auto-"+Guid.NewGuid());
        var file=Path.Combine(root,"rules.json");
        try
        {
            var first=new DownloadAutomationRule("Rule A","site.example",".pdf","Documents","","");
            var second=new DownloadAutomationRule("Rule B","site.example",".pdf","Images","","");
            DownloadAutomationRuleStore.Save(file,[first,second]);
            var loaded=DownloadAutomationRuleStore.Load(file);
            Assert.Equal(["Rule A","Rule B"],loaded.Select(r=>r.Name));
            var d=DownloadAutomationPolicy.Apply("https://site.example/x.pdf",
                Path.Combine(root,"x.pdf"),"Other","",root,loaded);
            Assert.Equal("Rule A",d.MatchedRule);
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
