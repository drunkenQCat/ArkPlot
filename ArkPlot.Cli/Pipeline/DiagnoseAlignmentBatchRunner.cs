using ArkPlot.Core.Infrastructure;
using ArkPlot.Tts.Alignment;

namespace ArkPlot.Cli.Pipeline;

/// <summary>
/// diagnose-alignment-batch 命令：批量检测对齐异常并汇总问题。
/// 用法: diagnose-alignment-batch <novel_file.md> [--db <db_path>] [--threshold <ratio>]
/// </summary>
public static class DiagnoseAlignmentBatchRunner
{
    public static async Task RunAsync(string novelFilePath, string? dbPath = null, double threshold = 0.3)
    {
        if (!File.Exists(novelFilePath))
        {
            Console.Error.WriteLine($"❌ 文件不存在: {novelFilePath}");
            return;
        }

        if (dbPath != null)
        {
            if (!File.Exists(dbPath))
            {
                Console.Error.WriteLine($"❌ DB 文件不存在: {dbPath}");
                return;
            }
            DbFactory.ConfigureForTesting($"Data Source={dbPath}");
        }

        Console.WriteLine("╔══════════════════════════════════════════╗");
        Console.WriteLine("║     Alignment Batch Diagnostic          ║");
        Console.WriteLine("╚══════════════════════════════════════════╝");
        Console.WriteLine();

        // 运行对齐
        Console.WriteLine("正在运行对齐...");
        var aligner = new NovelAligner();
        var (entries, stats) = await aligner.AlignByFileNameAsync(novelFilePath);

        Console.WriteLine($"对齐完成: {stats.AlignedDialogs}/{stats.TotalDialogs} 对话已对齐");
        Console.WriteLine();

        // 加载 DB 数据用于分析
        var actName = NovelAligner.ExtractActName(Path.GetFileNameWithoutExtension(novelFilePath));
        var db = DbFactory.GetClient();
        var act = await db.Queryable<ArkPlot.Core.Model.Act>()
            .FirstAsync(a => a.Name == actName && a.Lang == "zh_CN");

        if (act == null)
        {
            Console.Error.WriteLine($"❌ 活动 '{actName}' 未找到");
            return;
        }

        var plots = await db.Queryable<ArkPlot.Core.Model.Plot>()
            .Where(p => p.ActId == act.Id && p.StoryChapterId > 0)
            .ToListAsync();

        var plotIds = plots.Select(p => p.Id).ToList();
        var allEntries = await db.Queryable<ArkPlot.Core.Model.FormattedTextEntry>()
            .Where(e => plotIds.Contains(e.PlotId))
            .ToListAsync();

        var dbEntriesByPlot = allEntries.GroupBy(e => e.PlotId)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Index).ToList());

        // 异常检测
        var anomalies = new List<AlignmentAnomaly>();

        foreach (var entry in entries.Where(e => e.IsDialog))
        {
            if (entry.EntryIndex < 0) continue;

            // 查找对应的 DB 条目
            var plot = plots.FirstOrDefault(p => p.Title.Contains(entry.ChapterTitle) || entry.ChapterTitle.Contains(p.Title));
            if (plot == null || !dbEntriesByPlot.TryGetValue(plot.Id, out var plotEntries)) continue;

            var dbEntry = plotEntries.FirstOrDefault(e => e.Index == entry.EntryIndex);
            if (dbEntry == null) continue;

            // 检测异常
            var novelLen = entry.NovelText.Length;
            var dbLen = dbEntry.Dialog?.Length ?? 0;

            // 1. 真正的异常：小说文本不是 DB 文本的子串（归一化后），且长度差异大
            if (dbLen > 0 && novelLen > 0)
            {
                var novelNorm = NormalizeForCompare(entry.NovelText);
                var dbNorm = NormalizeForCompare(dbEntry.Dialog ?? "");
                
                // 检查是否为子串关系
                bool isSubstring = dbNorm.Contains(novelNorm) || novelNorm.Contains(dbNorm);
                double ratio = (double)Math.Min(novelLen, dbLen) / Math.Max(novelLen, dbLen);
                
                // 只有当不是子串关系且长度差异很大时，才算异常
                if (!isSubstring && ratio < threshold)
                {
                    anomalies.Add(new AlignmentAnomaly
                    {
                        Type = AnomalyType.LengthMismatch,
                        Chapter = entry.ChapterTitle,
                        NovelText = entry.NovelText,
                        DbText = dbEntry.Dialog ?? "",
                        NovelLength = novelLen,
                        DbLength = dbLen,
                        Ratio = ratio,
                        Character = entry.CharacterName
                    });
                }
            }

            // 2. 角色不匹配
            if (!string.IsNullOrEmpty(entry.CharacterName) && 
                !string.IsNullOrEmpty(dbEntry.CharacterName) &&
                entry.CharacterName != dbEntry.CharacterName)
            {
                anomalies.Add(new AlignmentAnomaly
                {
                    Type = AnomalyType.CharacterMismatch,
                    Chapter = entry.ChapterTitle,
                    NovelText = entry.NovelText,
                    DbText = dbEntry.Dialog ?? "",
                    NovelCharacter = entry.CharacterName,
                    DbCharacter = dbEntry.CharacterName
                });
            }
        }

        // 统计
        var byType = anomalies.GroupBy(a => a.Type).ToDictionary(g => g.Key, g => g.Count());
        var byChapter = anomalies.GroupBy(a => a.Chapter).ToDictionary(g => g.Key, g => g.Count());

        Console.WriteLine("═══ 异常统计 ═══");
        Console.WriteLine($"总计: {anomalies.Count} 个异常");
        Console.WriteLine();

        Console.WriteLine("按类型:");
        foreach (var (type, count) in byType.OrderByDescending(x => x.Value))
        {
            Console.WriteLine($"  {type}: {count}");
        }
        Console.WriteLine();

        Console.WriteLine("按章节 (Top 10):");
        foreach (var (chapter, count) in byChapter.OrderByDescending(x => x.Value).Take(10))
        {
            Console.WriteLine($"  {chapter}: {count}");
        }
        Console.WriteLine();

        // 详细案例
        Console.WriteLine("═══ 典型异常案例 ═══");

        // 长度不匹配案例
        var lengthMismatchCases = anomalies
            .Where(a => a.Type == AnomalyType.LengthMismatch)
            .OrderBy(a => a.Ratio)
            .Take(5);

        if (lengthMismatchCases.Any())
        {
            Console.WriteLine();
            Console.WriteLine("【长度不匹配】(小说文本远短于 DB 文本)");
            foreach (var a in lengthMismatchCases)
            {
                Console.WriteLine($"  章节: {a.Chapter}");
                Console.WriteLine($"  比例: {a.Ratio:P0} ({a.NovelLength}/{a.DbLength})");
                Console.WriteLine($"  小说: {TruncateText(a.NovelText, 50)}");
                Console.WriteLine($"  DB:   {TruncateText(a.DbText, 50)}");
                Console.WriteLine();
            }
        }

        // 角色不匹配案例
        var charMismatchCases = anomalies
            .Where(a => a.Type == AnomalyType.CharacterMismatch)
            .Take(5);

        if (charMismatchCases.Any())
        {
            Console.WriteLine();
            Console.WriteLine("【角色不匹配】");
            foreach (var a in charMismatchCases)
            {
                Console.WriteLine($"  章节: {a.Chapter}");
                Console.WriteLine($"  小说角色: {a.NovelCharacter}");
                Console.WriteLine($"  DB 角色:   {a.DbCharacter}");
                Console.WriteLine($"  小说文本: {TruncateText(a.NovelText, 50)}");
                Console.WriteLine($"  DB 文本:   {TruncateText(a.DbText, 50)}");
                Console.WriteLine();
            }
        }

        // 建议
        Console.WriteLine("═══ 建议 ═══");
        if (byType.GetValueOrDefault(AnomalyType.LengthMismatch) > 10)
        {
            Console.WriteLine("  ⚠️  大量长度不匹配案例，可能需要：");
            Console.WriteLine("     - 调整 Phase 3.5 合并逻辑");
            Console.WriteLine("     - 降低窗口匹配阈值");
            Console.WriteLine("     - 实现短文本专用匹配策略");
        }

        if (byType.GetValueOrDefault(AnomalyType.CharacterMismatch) > 5)
        {
            Console.WriteLine("  ⚠️  存在角色不匹配，可能是：");
            Console.WriteLine("     - CharacterCode 传播链断裂");
            Console.WriteLine("     - LLM 修改了角色名");
            Console.WriteLine("     - 锚点匹配错误导致继承链偏移");
        }

        if (anomalies.Count == 0)
        {
            Console.WriteLine("  ✅ 未检测到明显异常");
        }
    }

    private static string TruncateText(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "(空)";
        text = text.Replace("\n", " ").Replace("\r", "");
        return text.Length <= maxLen ? text : text[..maxLen] + "…";
    }
    
    /// <summary>
    /// 归一化文本用于比较（去除标点和空白）
    /// </summary>
    private static string NormalizeForCompare(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }
        return sb.ToString();
    }
}

public enum AnomalyType
{
    LengthMismatch,
    CharacterMismatch,
    LowConfidence,
    InheritanceChain
}

public class AlignmentAnomaly
{
    public AnomalyType Type { get; set; }
    public string Chapter { get; set; } = "";
    public string NovelText { get; set; } = "";
    public string DbText { get; set; } = "";
    public int NovelLength { get; set; }
    public int DbLength { get; set; }
    public double Ratio { get; set; }
    public string? Character { get; set; }
    public string? NovelCharacter { get; set; }
    public string? DbCharacter { get; set; }
}