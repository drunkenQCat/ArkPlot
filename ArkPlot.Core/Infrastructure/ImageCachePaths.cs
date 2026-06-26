using System.IO;

namespace ArkPlot.Core.Infrastructure;

/// <summary>
/// 网络图片缓存路径：统一管理从 media.prts.wiki 等下载的图片。
/// 缓存位于 {AppRoot}/image-cache/{storyName}/ 下，与用户输出目录分离。
/// </summary>
public static class ImageCachePaths
{
    /// <summary>从 URL 计算本地缓存绝对路径（用于下载写入和存在性检查）。</summary>
    public static string GetAbsolutePath(string storyName, string url) =>
        Path.Combine(OutputPaths.ActRootAbsolute(storyName), GetRelativePathFromUrl(url));

    /// <summary>
    /// 从 URL 计算 Typst 相对路径（相对于 AppRoot，用于 Typst image() 调用）。
    /// 例：https://media.prts.wiki/8/82/Avg.png → image-cache/孤星/media.prts.wiki/8/82/Avg.png
    /// </summary>
    public static string GetTypstRelativePath(string storyName, string url) =>
        Path.Combine(OutputPaths.ActRootRelative(storyName), GetRelativePathFromUrl(url))
            .Replace('\\', '/');

    /// <summary>URL → 相对路径（剥协议，保留 host/path）。复用 PrtsResLoader 的约定。</summary>
    public static string GetRelativePathFromUrl(string url)
    {
        var normalized = NormalizeUrl(url);
        var uri = new Uri(normalized);
        return Path.Combine(uri.Host, uri.AbsolutePath.TrimStart('/'));
    }

    /// <summary>判断是否为网络 URL（含协议或首段像域名）。</summary>
    public static bool IsNetworkUrl(string path) =>
        path.StartsWith("http://")
        || path.StartsWith("https://")
        || (path.Contains('/') && path.Split('/')[0].Contains('.') && !path.StartsWith("pics/"));

    /// <summary>URL 归一化：无协议且首段像域名的加 https://。</summary>
    public static string NormalizeUrl(string url)
    {
        if (url.StartsWith("http://") || url.StartsWith("https://"))
            return url;
        var firstSegment = url.Split('/')[0];
        return firstSegment.Contains('.') && url.Contains('/') ? "https://" + url : url;
    }
}
