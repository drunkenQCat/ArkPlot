using System.Security.Cryptography;
using System.Text.Json;

namespace ArkPlot.Novelizer;

/// <summary>
/// 基于 MD5 hash 的章节缓存。
/// 缓存文件: <outputDir>/.novelizer-cache.json
/// </summary>
public class ChapterCache
{
    private readonly string _cacheFilePath;
    private Dictionary<string, CacheEntry> _entries;

    private record CacheEntry(string Hash, string Model, DateTime Timestamp);

    public ChapterCache(string outputDir)
    {
        _cacheFilePath = Path.Combine(outputDir, ".novelizer-cache.json");
        _entries = Load();
    }

    /// <summary>
    /// 检查源 MD 是否已有缓存且未变化。
    /// 返回 null 表示缓存未命中（需要重新生成），否则返回已有小说文件路径。
    /// </summary>
    public string? Check(string sourceMdPath, string model, bool force)
    {
        if (force) return null;

        var sourceHash = ComputeMd5(sourceMdPath);
        var key = MakeKey(sourceMdPath, model);

        if (_entries.TryGetValue(key, out var entry) && entry.Hash == sourceHash)
        {
            var novelPath = GetNovelPath(sourceMdPath, model);
            if (File.Exists(novelPath))
                return novelPath;
        }

        return null;
    }

    /// <summary>
    /// 生成完成后更新缓存
    /// </summary>
    public void Invalidate(string sourceMdPath, string model)
    {
        if (_entries.Remove(MakeKey(sourceMdPath, model))) Save();
    }

    public void Update(string sourceMdPath, string model)
    {
        var sourceHash = ComputeMd5(sourceMdPath);
        var key = MakeKey(sourceMdPath, model);
        _entries[key] = new CacheEntry(sourceHash, model, DateTime.Now);
        Save();
    }

    public static string GetNovelPath(string sourceMdPath, string model)
    {
        var dir = Path.GetDirectoryName(sourceMdPath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(sourceMdPath);
        return Path.Combine(dir, $"{baseName}_novel_{model}.md");
    }

    private static string MakeKey(string path, string model) => $"{path}::{model}";

    private Dictionary<string, CacheEntry> Load()
    {
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                var json = File.ReadAllText(_cacheFilePath);
                return JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json) ?? new();
            }
        }
        catch { /* 缓存文件损坏则重新开始 */ }
        return new();
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_cacheFilePath, json);
    }

    private static string ComputeMd5(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var hash = MD5.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}