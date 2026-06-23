using System.IO;
using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;
using SqlSugar;

namespace ArkPlot.Core.Services;

/// <summary>
/// 图片描述服务。
///
/// 生命周期：
/// 1. 检查数据库缓存 → 有则直接返回
/// 2. 无缓存 → 调用视觉模型（百炼/Ollama）生成描述
/// 3. 写入数据库缓存
/// 4. 如果使用了本地临时文件，立即清理
///
/// Debug 模式下强制跳过缓存，重新描述。
/// </summary>
/// <summary>
/// 图片描述服务的返回结果，包含散文描述和结构化视觉事实。
/// </summary>
public record PicDescResult(string Description, string? Facts);

public class PicDescService : IDisposable
{
    private readonly SqlSugarClient _db;
    private readonly Func<string, Task<string>>? _describeByUrl;
    private readonly Func<string, Task<string>>? _extractFacts;
    private readonly string _cacheDir;
    private readonly bool _debugMode;

    /// <summary>
    /// YAML 提取的系统提示词，用于将散文描述转化为结构化视觉事实。
    /// </summary>
    public const string YamlExtractionPrompt = """
你是视觉要素提取器。将以下散文描述转化为结构化视觉事实。

## 规则
1. 禁止句子。只输出关键词和短语。
2. 禁止修辞、比喻。只保留客观事实。
3. 严格按以下 YAML 格式输出，不要输出任何其他内容。
4. 每个字段 2-6 个词，每个词不超过 4 个字。
5. **只输出一个模板**：根据输入内容判断是场景还是角色，输出对应模板，**不要混合输出**。

## 判断逻辑
- 如果描述中包含人物、角色、立绘、外貌、服饰等关键词 → 使用角色模板
- 如果描述中包含环境、建筑、背景、天空、室内等关键词 → 使用场景模板
- 如果描述同时包含人物和场景 → **优先输出角色模板**（人物是焦点）

## 场景模板（仅当无人物时使用）
lighting: [光源类型, 明暗程度, 色温]
materials: [材质1, 材质2, 材质3]
objects: [显著物体1, 显著物体2, 显著物体3]
space: [空间类型, 布局特征, 规模]
colors: [主色, 辅色, 点缀色]
mood: [氛围词1, 氛围词2]

## 角色模板（有人物时使用）
hair: [颜色, 长度, 发型特征]
clothing: [上装, 下装, 鞋/配饰]
equipment: [武器/道具1, 武器/道具2]
posture: [姿态描述]
features: [其他显著外貌特征1, 显著外貌特征2]
colors: [主色, 辅色]

## 示例
输入："夜色如墨，星子稀疏地缀在天幕上，她立于废墟之巅，银发垂落至腰际..."
判断：包含"她""银发""腰际"等人物关键词 → 使用角色模板
输出：
hair: [银色, 腰际, 长发]
clothing: [衣摆, 上装, 下装]
equipment: [无, 无]
posture: [伫立, 静默]
features: [肩头微颤, 凝望方向]
colors: [银色, 黑色, 深蓝]

输入："铁栏与混凝土的冷硬在昏光里凝成一片沉默，双层牢房沿走廊对称排开..."
判断：包含"铁栏""混凝土""牢房""走廊"等环境关键词，无人物 → 使用场景模板
输出：
lighting: [昏暗, 沉默, 冷光]
materials: [铁栏, 混凝土, 金属]
objects: [牢房, 走廊, 楼梯]
space: [室内, 对称布局, 中等规模]
colors: [灰白, 暗红, 金属灰]
mood: [压抑, 冷寂]
""";

    /// <summary>
    /// 创建 PicDescService 实例。
    /// </summary>
    /// <param name="describeByUrl">可选的图片描述函数，接收图片 URL 返回描述文本。为 null 时使用占位符模式。</param>
    /// <param name="extractFacts">可选的 YAML 提取函数，接收散文描述返回结构化视觉事实。为 null 时不提取。</param>
    /// <param name="debugMode">Debug 模式：强制跳过数据库缓存，重新生成描述并清理。</param>
    public PicDescService(
        Func<string, Task<string>>? describeByUrl = null,
        Func<string, Task<string>>? extractFacts = null,
        bool debugMode = false)
    {
        _describeByUrl = describeByUrl;
        _extractFacts = extractFacts;
        _debugMode = debugMode;

        _db = DbFactory.GetClient();

        // 临时图片缓存目录
        _cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "PicCache");
        if (!Directory.Exists(_cacheDir))
            Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// 获取或创建图片描述。
    /// 非图片 URL（如 MP3 音频）直接返回空字符串，不入库。
    /// </summary>
    /// <param name="imageUrl">图片 URL，传给视觉模型读图</param>
    /// <param name="characterCode">角色去重键，传null时用 imageUrl 自身去重</param>
    /// <summary>
    /// 已知的干扰/占位图片 URL，直接返回空字符串，不描述、不入库。
    /// </summary>
    private static readonly HashSet<string> SkipUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        "https://media.prts.wiki/8/8a/Avg_bg_bg_black.png",
        "https://media.prts.wiki/b/bf/Avg_char_empty.png",
    };

    public async Task<string> GetOrCreatePicDescAsync(string imageUrl, string? characterCode = null)
    {
        var result = await GetOrCreatePicDescWithFactsAsync(imageUrl, characterCode);
        return result.Description;
    }

    /// <summary>
    /// 获取或创建图片描述，同时返回散文描述和结构化视觉事实。
    /// </summary>
    public async Task<PicDescResult> GetOrCreatePicDescWithFactsAsync(string imageUrl, string? characterCode = null)
    {
        if (!IsImageUrl(imageUrl))
            return new PicDescResult("", null);

        // 干扰图片直接跳过
        if (SkipUrls.Contains(imageUrl))
            return new PicDescResult("", null);

        // 立绘的 DedupKey 必须是纯 CharacterCode，不能带回 imageUrl fallback
        // characterCode 为 null 时表示场景图片，用 imageUrl 自身去重
        var dedupKey = characterCode ?? imageUrl;
        // 确保 CharacterCode 不带 # 后缀
        if (characterCode != null)
        {
            var hashIdx = dedupKey.IndexOf('#');
            if (hashIdx >= 0) dedupKey = dedupKey[..hashIdx];
        }

        try
        {
            // Debug 模式：强制重新生成
            if (_debugMode)
                return await GenerateAndCacheAsync(imageUrl, dedupKey);

            // 第一级：DB 按 DedupKey 查（characterCode 或 URL）
            var existing = await GetPicDescRecordByDedupKeyAsync(dedupKey);
            if (existing != null)
                return new PicDescResult(existing.PicDesc, existing.PicFacts);

            // 第二级：如果有 characterCode，DB 按 ImageUrl 查
            //         同一 URL 可能对应多个 characterCode，只要 URL 已描述过就复用
            if (characterCode != null)
            {
                var byUrl = await GetPicDescRecordByUrlAsync(imageUrl);
                if (byUrl != null)
                    return new PicDescResult(byUrl.PicDesc, byUrl.PicFacts);
            }

            // 都没命中，调 API 并写入 DB
            return await GenerateAndCacheAsync(imageUrl, dedupKey);
        }
        catch
        {
            return new PicDescResult("", null); // 网络失败，不写 DB，下次重试
        }
    }

    /// <summary>
    /// 清理数据库中已存在的非图片 URL 记录（如误入库的 MP3）。
    /// </summary>
    public int CleanNonImageRecords()
    {
        var allRecords = _db.Queryable<PicDescription>().ToList();
        var toDelete = allRecords.Where(r => !IsImageUrl(r.ImageUrl)).ToList();

        foreach (var record in toDelete)
        {
            _db.Deleteable<PicDescription>().In(record.Id).ExecuteCommand();
        }

        return toDelete.Count;
    }

    /// <summary>
    /// 批量获取或创建图片描述。
    /// 自动过滤非图片 URL（如 MP3 音频），只处理图片格式。
    /// </summary>
    public async Task<Dictionary<string, string>> GetOrCreatePicDescsAsync(IEnumerable<string> imageUrls)
    {
        var result = new Dictionary<string, string>();
        foreach (var url in imageUrls)
        {
            if (!IsImageUrl(url))
            {
                result[url] = "";
                continue;
            }
            result[url] = await GetOrCreatePicDescAsync(url);
        }
        return result;
    }

    /// <summary>
    /// 判断 URL 是否是图片。
    /// 支持的格式：png, jpg, jpeg, gif, webp, bmp, svg, apng, avif。
    /// </summary>
    private static bool IsImageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var cleanUrl = url.Split('?')[0].ToLowerInvariant();
        var ext = Path.GetExtension(cleanUrl);

        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp"
                or ".bmp" or ".svg" or ".apng" or ".avif" => true,
            _ => false
        };
    }

    /// <summary>
    /// 生成图片描述并缓存到数据库。
    /// 如果配置了 _extractFacts，还会提取 YAML 结构化事实。
    /// 网络失败/异常时不写库，下次重试。
    /// </summary>
    private async Task<PicDescResult> GenerateAndCacheAsync(string imageUrl, string dedupKey)
    {
        string description;
        if (_describeByUrl != null)
        {
            description = await _describeByUrl(imageUrl);
        }
        else
        {
            description = GeneratePlaceholder(imageUrl);
        }

        // 散文 → YAML 事实提取
        string? facts = null;
        if (_extractFacts != null && !string.IsNullOrWhiteSpace(description)
            && !description.StartsWith("[PIC_DESC:"))
        {
            try
            {
                facts = await _extractFacts(description);
            }
            catch
            {
                // YAML 提取失败不影响散文
            }
        }

        UpsertPicDesc(dedupKey, imageUrl, description, facts);
        return new PicDescResult(description, facts);
    }


    /// <summary>
    /// 清理临时图片缓存目录中的所有文件。
    /// </summary>
    public void CleanCacheDirectory()
    {
        if (!Directory.Exists(_cacheDir)) return;

        var files = Directory.GetFiles(_cacheDir);
        foreach (var file in files)
        {
            try { File.Delete(file); }
            catch { /* 被占用的文件跳过 */ }
        }
    }

    /// <summary>
    /// 获取缓存目录的当前大小（字节）。
    /// </summary>
    public long GetCacheDirectorySize()
    {
        if (!Directory.Exists(_cacheDir)) return 0;

        long size = 0;
        foreach (var file in Directory.GetFiles(_cacheDir))
        {
            try { size += new FileInfo(file).Length; }
            catch { }
        }
        return size;
    }

    /// <summary>
    /// 按 DedupKey 查缓存记录，仅 Vision 来源视为有效缓存。
    /// Placeholder/Error 下次重试。
    /// </summary>
    private async Task<PicDescription?> GetPicDescRecordByDedupKeyAsync(string dedupKey)
    {
        var record = await _db.Queryable<PicDescription>()
            .FirstAsync(it => it.DedupKey == dedupKey);
        if (record == null) return null;
        if (record.Source != "Vision") return null; // Placeholder/Error 重试
        return record;
    }

    /// <summary>
    /// 按 ImageUrl 查缓存记录（用于两级查找：characterCode 没命中时，按 URL 再查一次）。
    /// 仅 Vision 来源视为有效缓存。
    /// </summary>
    private async Task<PicDescription?> GetPicDescRecordByUrlAsync(string imageUrl)
    {
        var record = await _db.Queryable<PicDescription>()
            .FirstAsync(it => it.ImageUrl == imageUrl);
        if (record == null) return null;
        if (record.Source != "Vision") return null; // Placeholder/Error 重试
        return record;
    }

    private void UpsertPicDesc(string dedupKey, string imageUrl, string desc, string? facts = null)
    {
        var now = DateTime.UtcNow;
        var existing = _db.Queryable<PicDescription>()
            .First(it => it.DedupKey == dedupKey);

        if (existing != null)
        {
            _db.Updateable<PicDescription>()
                .SetColumns(it => it.PicDesc == desc)
                .SetColumns(it => it.Source == "Vision")
                .SetColumns(it => it.ImageUrl == imageUrl)
                .SetColumns(it => it.PicFacts == facts)
                .SetColumns(it => it.UpdatedAt == now)
                .Where(it => it.DedupKey == dedupKey)
                .ExecuteCommand();
        }
        else
        {
            _db.Insertable(new PicDescription
            {
                DedupKey = dedupKey,
                ImageUrl = imageUrl,
                PicDesc = desc,
                PicFacts = facts,
                Source = "Vision",
                CreatedAt = now,
                UpdatedAt = now
            }).ExecuteCommand();
        }
    }

    private static string GeneratePlaceholder(string imageUrl)
    {
        var fileName = imageUrl;
        try
        {
            var uri = new Uri(imageUrl);
            fileName = Path.GetFileName(uri.LocalPath);
        }
        catch
        {
            var parts = imageUrl.Split('?')[0];
            fileName = parts.Split('/').LastOrDefault() ?? imageUrl;
        }

        return $"[PIC_DESC: {fileName}]";
    }

    /// <summary>
    /// 获取数据库和缓存统计信息。
    /// </summary>
    public (int DbCount, int CacheFileCount, long CacheSizeBytes) GetStats()
    {
        var dbCount = _db.Queryable<PicDescription>().Count();
        var cacheFileCount = Directory.Exists(_cacheDir) ? Directory.GetFiles(_cacheDir).Length : 0;
        var cacheSize = GetCacheDirectorySize();

        return (dbCount, cacheFileCount, cacheSize);
    }

    /// <summary>
    /// 初始化时自动清理非图片记录。
    /// </summary>
    public void InitializeCleanup()
    {
        var deleted = CleanNonImageRecords();
        if (deleted > 0)
            Console.WriteLine($"[PicDesc] 已清理 {deleted} 条非图片记录（MP3 等）");
    }

    public void Dispose()
    {
        CleanCacheDirectory();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 字符串扩展方法。
/// </summary>
internal static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
