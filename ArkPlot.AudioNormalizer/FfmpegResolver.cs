using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ArkPlot.AudioNormalizer;

/// <summary>
/// ffmpeg 可执行文件解析器。
/// 按优先级查找：捆绑目录 → 用户缓存目录 → 系统 PATH。
/// </summary>
public static class FfmpegResolver
{
    private static readonly string FfmpegExecutable =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>
    /// 查找 ffmpeg 可执行文件路径。
    /// 优先级：
    /// 1. 捆绑版（与应用程序同目录）
    /// 2. 用户缓存版（~/.arkplot/ffmpeg/）
    /// 3. 系统 PATH
    /// </summary>
    /// <param name="onLog">可选日志回调。</param>
    /// <returns>ffmpeg 路径，未找到返回 null</returns>
    public static string? FindFfmpeg(Action<string>? onLog = null)
    {
        // 1. 捆绑版（与 DLL/EXE 同目录）
        var bundled = Path.Combine(AppContext.BaseDirectory, FfmpegExecutable);
        if (File.Exists(bundled))
        {
            onLog?.Invoke($"[FfmpegResolver] 找到捆绑版: {bundled}");
            return bundled;
        }

        // 2. 用户缓存版（~/.arkplot/ffmpeg/<version>/<rid>/ffmpeg）
        var cached = FindInCache(onLog);
        if (cached != null)
            return cached;

        // 3. 系统 PATH
        var pathResult = FindInPath(onLog);
        return pathResult;
    }

    /// <summary>
    /// 获取 ffmpeg 缓存目录（~/.arkplot/ffmpeg/）。
    /// </summary>
    public static string GetCacheDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".arkplot", "ffmpeg");
    }

    /// <summary>
    /// 在缓存目录中查找 ffmpeg。
    /// 返回最新安装的版本。
    /// </summary>
    private static string? FindInCache(Action<string>? onLog = null)
    {
        var cacheDir = GetCacheDirectory();
        if (!Directory.Exists(cacheDir))
        {
            onLog?.Invoke($"[FfmpegResolver] 缓存目录不存在: {cacheDir}");
            return null;
        }

        // 递归查找 ffmpeg 可执行文件
        try
        {
            var candidates = Directory.GetFiles(cacheDir, FfmpegExecutable, SearchOption.AllDirectories);
            if (candidates.Length == 0)
            {
                onLog?.Invoke($"[FfmpegResolver] 缓存目录中未找到 ffmpeg");
                return null;
            }

            // 返回最新写入的版本
            var result = candidates
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .First();
            onLog?.Invoke($"[FfmpegResolver] 找到缓存版: {result}");
            return result;
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"[FfmpegResolver] 读取缓存目录失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 在系统 PATH 中查找 ffmpeg。
    /// </summary>
    private static string? FindInPath(Action<string>? onLog = null)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            onLog?.Invoke("[FfmpegResolver] PATH 环境变量为空");
            return null;
        }

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var paths = pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in paths)
        {
            var candidate = Path.Combine(dir, FfmpegExecutable);
            if (File.Exists(candidate))
            {
                onLog?.Invoke($"[FfmpegResolver] 在 PATH 中找到: {candidate}");
                return candidate;
            }
        }

        onLog?.Invoke("[FfmpegResolver] 未找到 ffmpeg");
        return null;
    }

    /// <summary>
    /// 获取当前平台的 RID（Runtime Identifier）。
    /// 例如：win-x64, linux-x64, osx-x64, osx-arm64。
    /// </summary>
    public static string GetRuntimeIdentifier()
    {
        string os;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            os = "win";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            os = "linux";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            os = "osx";
        else
            throw new PlatformNotSupportedException("不支持的操作系统");

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "x64" // 默认
        };

        return $"{os}-{arch}";
    }
}
