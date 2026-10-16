using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class BatchDownloadPlannerTests
{
    [Fact]
    public void DifferentLinksWithSameFilenameGetDistinctPaths()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-batch-" + Guid.NewGuid());
        var planned = BatchDownloadPlanner.Plan("https://one.example/file.zip\nhttps://two.example/file.zip",folder,[]);
        Assert.Equal(2,planned.Count);
        Assert.EndsWith("file.zip",planned[0].Destination);
        Assert.EndsWith("file (2).zip",planned[1].Destination);
        Assert.NotEqual(planned[0].Destination,planned[1].Destination);
    }

    [Fact]
    public void DuplicateUrlIsImportedOnce()
    {
        var folder = Path.Combine(Path.GetTempPath(),"rd-batch-" + Guid.NewGuid());
        Assert.Single(BatchDownloadPlanner.Plan("https://example.com/one.zip\r\nhttps://example.com/one.zip",folder,[]));
    }

    [Fact]
    public void UnsafeAndUnsupportedBatchIsRejectedAsAWhole()
    {
        var folder = Path.Combine(Path.GetTempPath(),"rd-batch-" + Guid.NewGuid());
        Assert.Throws<ArgumentException>(() =>
            BatchDownloadPlanner.Plan("https://example.com/file\nfile:///etc/passwd",folder,[]));
        Assert.Throws<ArgumentException>(() =>
            BatchDownloadPlanner.Plan("https://user:password@example.com/file",folder,[]));
    }

    [Fact]
    public void PlannedPathDoesNotOverwriteAnExistingCatalogEntry()
    {
        var folder = Path.Combine(Path.GetTempPath(),"rd-batch-" + Guid.NewGuid());
        var planned = BatchDownloadPlanner.Plan("https://example.com/file.bin",folder,[Path.Combine(folder,"file.bin")]);
        Assert.EndsWith("file (2).bin",Assert.Single(planned).Destination);
    }

    [Fact]
    public void BatchSizeIsBounded()
    {
        var folder = Path.Combine(Path.GetTempPath(),"rd-batch-" + Guid.NewGuid());
        Assert.Throws<ArgumentException>(() =>
            BatchDownloadPlanner.Plan(string.Join("\n",Enumerable.Range(1,101).Select(i=>$"https://example.com/{i}")),folder,[]));
    }
}
