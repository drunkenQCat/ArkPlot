using System.Security.Cryptography;
using System.Text;

namespace ArkPlot.Tts;

/// <summary>
/// TTS 音频缓存服务。
/// 对 (text, voice, rate, volume) 取 SHA256 作为 key，命中则复用已有 MP3 文件，避免重复网络请求。
/// </summary>
public class TtsCacheService
{
    private readonly string _cacheDir;

    /// <summary>
    /// 创建缓存服务。
    /// </summary>
    /// <param name="cacheDir">缓存目录。默认为输出目录下的 _tts_cache/。</param>
    public TtsCacheService(string cacheDir)
    {
        _cacheDir = cacheDir;
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>缓存目录路径。</summary>
    public string CacheDir => _cacheDir;

    /// <summary>
    /// 计算缓存 key。
    /// </summary>
    public static string GetCacheKey(string text, string voice, string rate = "+0%", string volume = "+0%")
    {
        var composite = $"{text}|{voice}|{rate}|{volume}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(composite));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 查找缓存。
    /// </summary>
    /// <returns>(是否命中, 缓存文件路径)</returns>
    public (bool Hit, string? Path) TryGetCachedAudio(string cacheKey)
    {
        var path = GetCachePath(cacheKey);
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return (true, path);
        return (false, null);
    }

    /// <summary>
    /// 将合成结果保存到缓存。
    /// </summary>
    public void SaveToCache(string cacheKey, string sourcePath)
    {
        var destPath = GetCachePath(cacheKey);
        File.Copy(sourcePath, destPath, overwrite: true);
    }

    /// <summary>缓存目录中的文件数量。</summary>
    public int CachedFileCount =>
        Directory.Exists(_cacheDir) ? Directory.GetFiles(_cacheDir, "*.mp3").Length : 0;

    public string GetCachePath(string cacheKey) =>
        Path.Combine(_cacheDir, $"{cacheKey}.mp3");
}
