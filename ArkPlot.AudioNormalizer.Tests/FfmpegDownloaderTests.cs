namespace ArkPlot.AudioNormalizer.Tests;

public class FfmpegDownloaderTests
{
    [Fact]
    public void NeedsDownload_ReturnsFalseWhenFfmpegExists()
    {
        var ffmpegPath = FfmpegResolver.FindFfmpeg();
        if (ffmpegPath == null)
            return; // 跳过：环境无 ffmpeg

        var downloader = new FfmpegDownloader();
        Assert.False(downloader.NeedsDownload());
    }

    [Fact]
    public async Task CreateAsync_WhenFfmpegExists_DoesNotDownload()
    {
        var ffmpegPath = FfmpegResolver.FindFfmpeg();
        if (ffmpegPath == null)
            return;

        var progressReported = false;
        var progress = new Progress<double>(_ => progressReported = true);

        var normalizer = await LoudnessNormalizer.CreateAsync(progress);
        Assert.NotNull(normalizer);

        // 等一下让异步进度回调有机会执行
        await Task.Delay(100);
        Assert.False(progressReported, "ffmpeg 已存在时不应触发下载进度回调");
    }
}
