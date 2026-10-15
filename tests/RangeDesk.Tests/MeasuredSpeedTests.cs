using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class MeasuredSpeedTests
{
    [Fact]
    public void SpeedHistoryRetainsOnlyRealMeasurementsAndHasBoundedMemory()
    {
        var download = new DownloadEntry("https://example.test/file.bin", @"C:\Downloads\file.bin");
        Assert.Empty(download.SpeedSamples);
        for (int i = 0; i < 95; i++)
            download.Report(DownloadStatus.Downloading, i * 2, 1_000, i * 128.5, 1);
        Assert.Equal(60, download.SpeedSamples.Count);
        Assert.Equal(35 * 128.5, download.SpeedSamples[0]);
        Assert.Equal(94 * 128.5, download.SpeedSamples[^1]);
        download.Report(DownloadStatus.Paused, 190, 1000);
        Assert.Equal(60, download.SpeedSamples.Count);
    }

    [Fact]
    public void PercentAndEtaNeverInventMissingSizeOrSpeed()
    {
        var download = new DownloadEntry("https://example.test/unknown", @"C:\Downloads\unknown");
        download.Report(DownloadStatus.Downloading, 2_000, null, 2048, 1);
        Assert.Null(download.Percent);
        Assert.Equal("—", download.EtaLabel);
        download.Report(DownloadStatus.Downloading, 200, 1000, 0, 0);
        Assert.Equal("—", download.EtaLabel);
        Assert.Equal(0, download.Connections);
        download.Report(DownloadStatus.Downloading, 200, 1000, 100, 1);
        Assert.Equal(20, download.Percent);
        Assert.Equal("8s", download.EtaLabel);
    }
}
