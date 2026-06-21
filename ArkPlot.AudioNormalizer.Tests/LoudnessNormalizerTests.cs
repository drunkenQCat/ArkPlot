namespace ArkPlot.AudioNormalizer.Tests;

public class LoudnessNormalizerTests
{
    [Fact]
    public void Constructor_InvalidExplicitPath_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() =>
            new LoudnessNormalizer("/nonexistent/path/to/ffmpeg"));
    }

    [Fact]
    public void Constructor_WithValidFfmpegPath_DoesNotThrow()
    {
        var ffmpegPath = FfmpegResolver.FindFfmpeg();
        if (ffmpegPath == null)
            return;

        var normalizer = new LoudnessNormalizer(ffmpegPath);
        Assert.NotNull(normalizer);
    }

    [Fact]
    public void DefaultProperties_HaveCorrectValues()
    {
        var ffmpegPath = FfmpegResolver.FindFfmpeg();
        if (ffmpegPath == null)
            return;

        var normalizer = new LoudnessNormalizer(ffmpegPath);

        Assert.Equal(-16.0, normalizer.TargetLufs);
        Assert.Equal(-1.5, normalizer.TruePeak);
        Assert.Equal(11.0, normalizer.LoudnessRange);
    }

    [Fact]
    public async Task NormalizeAsync_InputFileNotFound_ThrowsFileNotFoundException()
    {
        var ffmpegPath = FfmpegResolver.FindFfmpeg();
        if (ffmpegPath == null)
            return;

        var normalizer = new LoudnessNormalizer(ffmpegPath);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            normalizer.NormalizeAsync("/nonexistent/input.wav", "/tmp/output.wav"));
    }

    [Fact]
    public async Task MeasureAsync_WithRealFfmpeg_ReturnsMeasurement()
    {
        var ffmpegPath = FfmpegResolver.FindFfmpeg();
        if (ffmpegPath == null)
            return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"loudnorm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var testWav = Path.Combine(tempDir, "test_tone.wav");
            var genEngine = new FFmpeg.NET.Engine(ffmpegPath);
            await genEngine.ExecuteAsync(
                $"-f lavfi -i \"sine=frequency=440:duration=2\" -ac 1 -ar 44100 \"{testWav}\"",
                CancellationToken.None);

            Assert.True(File.Exists(testWav), "ffmpeg 应生成测试 WAV 文件");

            var normalizer = new LoudnessNormalizer(ffmpegPath);
            var measurement = await normalizer.MeasureAsync(testWav);

            Assert.True(measurement.InputI < 0, $"InputI 应为负值: {measurement.InputI}");
            Assert.True(measurement.InputLra >= 0, $"InputLra 应非负: {measurement.InputLra}");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task NormalizeAsync_WithRealFfmpeg_ProducesOutput()
    {
        var ffmpegPath = FfmpegResolver.FindFfmpeg();
        if (ffmpegPath == null)
            return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"loudnorm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var testWav = Path.Combine(tempDir, "loud_input.wav");
            var outputWav = Path.Combine(tempDir, "loud_output.wav");
            var genEngine = new FFmpeg.NET.Engine(ffmpegPath);
            await genEngine.ExecuteAsync(
                $"-f lavfi -i \"sine=frequency=1000:duration=3\" -ac 1 -ar 44100 \"{testWav}\"",
                CancellationToken.None);

            var normalizer = new LoudnessNormalizer(ffmpegPath);
            await normalizer.NormalizeAsync(testWav, outputWav);

            Assert.True(File.Exists(outputWav), "应产生输出文件");
            Assert.True(new FileInfo(outputWav).Length > 0, "输出文件应非空");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
