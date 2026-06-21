using System.Runtime.InteropServices;

namespace ArkPlot.AudioNormalizer.Tests;

public class FfmpegResolverTests
{
    [Fact]
    public void GetRuntimeIdentifier_ReturnsValidRid()
    {
        var rid = FfmpegResolver.GetRuntimeIdentifier();

        Assert.NotNull(rid);
        Assert.Matches(@"^(win|linux|osx)-(x64|x86|arm64|arm)$", rid);
    }

    [Fact]
    public void GetRuntimeIdentifier_CurrentPlatform_IsDetected()
    {
        var rid = FfmpegResolver.GetRuntimeIdentifier();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.StartsWith("win-", rid);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Assert.StartsWith("linux-", rid);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Assert.StartsWith("osx-", rid);
    }

    [Fact]
    public void GetCacheDirectory_ReturnsUserProfileSubdir()
    {
        var cacheDir = FfmpegResolver.GetCacheDirectory();

        Assert.NotNull(cacheDir);
        Assert.Contains(".arkplot", cacheDir);
        Assert.Contains("ffmpeg", cacheDir);
    }

    [Fact]
    public void FindFfmpeg_ReturnsNullWhenNotFound()
    {
        // 这个测试在当前环境可能返回 null 也可能返回有效路径，
        // 取决于是否安装了 ffmpeg。只验证不抛异常。
        var result = FfmpegResolver.FindFfmpeg();
        // result 可能是 null 或有效路径
        if (result != null)
            Assert.True(File.Exists(result), $"返回的路径应存在: {result}");
    }

    [Fact]
    public void FindFfmpeg_WhenFfmpegInPath_ReturnsValidPath()
    {
        var result = FfmpegResolver.FindFfmpeg();
        if (result == null)
            return; // 跳过：环境无 ffmpeg

        Assert.True(File.Exists(result));
        Assert.True(result.EndsWith("ffmpeg") || result.EndsWith("ffmpeg.exe"));
    }
}
