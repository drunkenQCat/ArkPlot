using ArkPlot.Core.Infrastructure;

namespace ArkPlot.Video;

/// <summary>
/// 网络图片缓存管理器：收集 URL、下载缺失项、提供 URL→路径映射。
/// 所有路径通过 ImageCachePaths 计算，不手动拼接。
/// </summary>
public class NetworkImageCache
{
    private readonly string _storyName;
    private readonly Action<string>? _log;

    public NetworkImageCache(string storyName, Action<string>? onLog = null)
    {
        _storyName = storyName;
        _log = onLog;
    }

    /// <summary>
    /// 确保所有网络图片已缓存。返回 URL → Typst 相对路径 映射。
    /// </summary>
    public async Task<Dictionary<string, string>> EnsureAllCachedAsync(
        IEnumerable<ArkPlot.Core.Model.FormattedTextEntry> entries,
        CancellationToken ct = default)
    {
        var urlToRelativePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            CollectUrl(entry.Bg, urlToRelativePath, urls);

            if (entry.Portraits != null)
                foreach (var url in entry.Portraits)
                    CollectUrl(url, urlToRelativePath, urls);

            if (!string.IsNullOrEmpty(entry.TypText))
                foreach (System.Text.RegularExpressions.Match match in
                    System.Text.RegularExpressions.Regex.Matches(
                        entry.TypText, @"image\(""((?:https?://)?[^""]*\.[^""]+)"""))
                    CollectUrl(match.Groups[1].Value, urlToRelativePath, urls);
        }

        if (urls.Count == 0)
            return urlToRelativePath;

        var missing = urls.Where(url => !File.Exists(ImageCachePaths.GetAbsolutePath(_storyName, url))).ToList();

        if (missing.Count == 0)
        {
            _log?.Invoke($"📥 {urls.Count} 张图片全部命中缓存");
            return urlToRelativePath;
        }

        _log?.Invoke($"📥 下载 {missing.Count}/{urls.Count} 张网络图片...");
        await DownloadParallelAsync(missing, ct);
        _log?.Invoke("📥 下载完成");

        return urlToRelativePath;
    }

    /// <summary>将 Typst 代码中的网络 URL 替换为本地相对路径。</summary>
    public string RewriteUrls(string typstCode, Dictionary<string, string> urlToRelativePath)
    {
        var result = typstCode;
        foreach (var (url, relativePath) in urlToRelativePath)
            result = result.Replace(url, relativePath.Replace('\\', '/'));
        return result;
    }

    /// <summary>URL → 本地 Typst 相对路径。未收录的网络 URL 实时计算路径而非返回透明占位。</summary>
    public string MapUrl(string url, Dictionary<string, string> urlToRelativePath)
    {
        if (urlToRelativePath.TryGetValue(url, out var relativePath))
            return relativePath.Replace('\\', '/');

        // 网络 URL 未在字典中 → 实时计算缓存路径（保证 output/{story}/ 前缀）
        if (ImageCachePaths.IsNetworkUrl(url))
        {
            var computed = ImageCachePaths.GetTypstRelativePath(_storyName, url);
            _log?.Invoke($"  ⚠ 图片未收录，实时计算路径: {url[..Math.Min(60, url.Length)]}... → {computed}");
            return computed;
        }

        return url;
    }

    private void CollectUrl(string url, Dictionary<string, string> urlToRelativePath, HashSet<string> urls)
    {
        if (string.IsNullOrEmpty(url)) return;

        // 过滤哨兵值：PrtsPreloader 用 https://pics/transparent.png 表示"无立绘"
        if (url.Contains("pics/transparent.png") || url.Contains("pics\\transparent.png"))
            return;

        var normalizedUrl = ImageCachePaths.NormalizeUrl(url);
        if (normalizedUrl == url && !ImageCachePaths.IsNetworkUrl(url))
            return; // 本地路径，跳过

        if (urlToRelativePath.ContainsKey(normalizedUrl)) return;

        var typstRelative = ImageCachePaths.GetTypstRelativePath(_storyName, normalizedUrl);
        urlToRelativePath[normalizedUrl] = typstRelative;

        // 同时存储无协议版本，以便 RewriteUrls 匹配 TypText 中的无协议 URL
        if (normalizedUrl != url)
            urlToRelativePath[url] = typstRelative;

        urls.Add(normalizedUrl);
    }

    private async Task DownloadParallelAsync(List<string> urls, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        await Parallel.ForEachAsync(urls, new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = ct
        }, async (url, token) =>
        {
            var absPath = ImageCachePaths.GetAbsolutePath(_storyName, url);
            var dir = Path.GetDirectoryName(absPath);
            if (dir != null) Directory.CreateDirectory(dir);

            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var bytes = await client.GetByteArrayAsync(url, token);
                    await File.WriteAllBytesAsync(absPath, bytes, token);
                    _log?.Invoke($"  ✓ {Path.GetFileName(absPath)} ({bytes.Length / 1024}KB)");
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    _log?.Invoke($"  ⚠ {Path.GetFileName(absPath)} 第{attempt}次失败，重试中... ({ex.Message})");
                    await Task.Delay(500 * attempt, token);
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"  ✗ {Path.GetFileName(absPath)} 下载失败 ({maxRetries}次尝试): {ex.Message}");
                }
            }
        });
    }
}
