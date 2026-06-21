using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ArkPlot.AudioNormalizer;

/// <summary>
/// ffmpeg 运行时下载器。
/// 根据平台自动选择下载源，支持进度回调和日志。
/// </summary>
public class FfmpegDownloader
{
    private const string DefaultVersion = "7.0.2";

    private readonly HttpClient _httpClient;
    private readonly Action<string>? _onLog;

    public FfmpegDownloader(HttpClient? httpClient = null, Action<string>? onLog = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _onLog = onLog;
    }

    /// <summary>
    /// 下载并安装 ffmpeg 到用户缓存目录。
    /// </summary>
    /// <param name="progress">进度回调（0.0 ~ 1.0）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>安装的 ffmpeg 路径</returns>
    public async Task<string> DownloadAndInstallAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var rid = FfmpegResolver.GetRuntimeIdentifier();
        var downloadUrl = GetDownloadUrl(rid);
        var archiveFormat = GetArchiveFormat(rid);

        _onLog?.Invoke($"[FfmpegDownloader] 平台: {rid}, 下载源: {downloadUrl}");

        var cacheDir = FfmpegResolver.GetCacheDirectory();
        var versionDir = Path.Combine(cacheDir, DefaultVersion, rid);

        Directory.CreateDirectory(versionDir);

        var archivePath = Path.Combine(versionDir, $"ffmpeg.{archiveFormat}");
        var ffmpegExe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
        var ffmpegPath = Path.Combine(versionDir, ffmpegExe);

        // 如果已存在则直接返回
        if (File.Exists(ffmpegPath))
        {
            _onLog?.Invoke($"[FfmpegDownloader] 已缓存: {ffmpegPath}");
            progress?.Report(1.0);
            return ffmpegPath;
        }

        try
        {
            // 1. 下载归档文件
            _onLog?.Invoke($"[FfmpegDownloader] 开始下载...");
            progress?.Report(0.0);
            await DownloadFileAsync(downloadUrl, archivePath, progress, cancellationToken);

            // 2. 解压
            _onLog?.Invoke($"[FfmpegDownloader] 解压中...");
            progress?.Report(0.9);
            await ExtractArchiveAsync(archivePath, versionDir, archiveFormat, cancellationToken);

            // 3. 查找并移动 ffmpeg 到版本目录根
            var extractedFfmpeg = FindExtractedFfmpeg(versionDir);
            if (extractedFfmpeg == null)
                throw new InvalidOperationException("解压后未找到 ffmpeg 可执行文件");

            if (extractedFfmpeg != ffmpegPath)
            {
                File.Move(extractedFfmpeg, ffmpegPath, overwrite: true);
            }

            // 4. 清理归档文件
            try { File.Delete(archivePath); } catch { }

            // 5. 设置可执行权限（Unix 系统）
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SetExecutablePermission(ffmpegPath);
            }

            _onLog?.Invoke($"[FfmpegDownloader] 安装完成: {ffmpegPath}");
            progress?.Report(1.0);
            return ffmpegPath;
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"[FfmpegDownloader] 下载失败: {ex.Message}");
            // 清理失败的下载
            try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { }
            throw;
        }
    }

    /// <summary>
    /// 检查是否需要下载 ffmpeg。
    /// </summary>
    public bool NeedsDownload()
    {
        return FfmpegResolver.FindFfmpeg(_onLog) == null;
    }

    /// <summary>
    /// 获取当前平台的下载 URL。
    /// </summary>
    private static string GetDownloadUrl(string rid)
    {
        return rid switch
        {
            "win-x64" or "win-x86" =>
                "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",

            "linux-x64" =>
                "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz",

            "linux-arm64" =>
                "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-arm64-static.tar.xz",

            "osx-x64" or "osx-arm64" =>
                // macOS 使用 evermeet.cx 的构建
                "https://evermeet.cx/ffmpeg/ffmpeg-7.0.2.zip",

            _ => throw new PlatformNotSupportedException($"不支持的平台: {rid}")
        };
    }

    /// <summary>
    /// 获取归档格式。
    /// </summary>
    private static string GetArchiveFormat(string rid)
    {
        return rid switch
        {
            "win-x64" or "win-x86" => "zip",
            "linux-x64" or "linux-arm64" => "tar.xz",
            "osx-x64" or "osx-arm64" => "zip",
            _ => "zip"
        };
    }

    /// <summary>
    /// 下载文件（带进度回调）。
    /// </summary>
    private async Task DownloadFileAsync(
        string url,
        string destPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        _onLog?.Invoke($"[FfmpegDownloader] GET {url}");
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        _onLog?.Invoke($"[FfmpegDownloader] 文件大小: {(totalBytes > 0 ? $"{totalBytes / 1024.0 / 1024.0:F1} MB" : "未知")}");
        var downloadedBytes = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        int bytesRead;
        var lastReportedPercent = -1;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                var downloadProgress = (double)downloadedBytes / totalBytes * 0.9; // 下载占 0~0.9
                progress?.Report(downloadProgress);

                // 每 10% 输出一次日志
                var percent = (int)(downloadProgress * 100);
                if (percent / 10 != lastReportedPercent / 10)
                {
                    lastReportedPercent = percent;
                    _onLog?.Invoke($"[FfmpegDownloader] 下载进度: {percent}%");
                }
            }
        }
    }

    /// <summary>
    /// 解压归档文件。
    /// </summary>
    private Task ExtractArchiveAsync(
        string archivePath,
        string destDir,
        string format,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            switch (format)
            {
                case "zip":
                    ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true);
                    break;

                case "tar.xz":
                    ExtractTarXz(archivePath, destDir);
                    break;

                default:
                    throw new NotSupportedException($"不支持的归档格式: {format}");
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 解压 tar.xz 文件（Linux）。
    /// 使用系统 tar 命令。
    /// </summary>
    private static void ExtractTarXz(string archivePath, string destDir)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-xf \"{archivePath}\" -C \"{destDir}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 tar 进程");

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"tar 解压失败: {error}");
        }
    }

    /// <summary>
    /// 在解压目录中查找 ffmpeg 可执行文件。
    /// </summary>
    private string? FindExtractedFfmpeg(string extractDir)
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

        // 递归查找
        var candidates = Directory.GetFiles(extractDir, exeName, SearchOption.AllDirectories);
        return candidates.FirstOrDefault();
    }

    /// <summary>
    /// 设置 Unix 可执行权限。
    /// </summary>
    private static void SetExecutablePermission(string filePath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{filePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit();
        }
        catch
        {
            // 忽略权限设置失败
        }
    }
}
