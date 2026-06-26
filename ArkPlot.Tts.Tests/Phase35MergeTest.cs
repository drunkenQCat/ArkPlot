using ArkPlot.Core.Infrastructure;
using ArkPlot.Tts.Alignment;
using Xunit;

namespace ArkPlot.Tts.Tests;

/// <summary>
/// Phase 3.5 合并对齐效果测试：用孤星真实数据，
/// 统计未对齐对话的数量和典型样本。
/// </summary>
public sealed class Phase35MergeTest : IDisposable
{
    [Fact]
    public async Task LoneTrail_Chapter1_MergeAlignmentStats()
    {
        var repoRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var dbPath = Path.Combine(repoRoot, "ArkPlot.Avalonia", "bin", "Debug", "net9.0", "arkplot.db");
        var novelPath = Path.Combine(repoRoot, "ArkPlot.Avalonia", "bin", "Debug", "net9.0",
            "output", "孤星", "孤星_novel_deepseek-v4-flash.md");

        if (!File.Exists(dbPath) || !File.Exists(novelPath))
        {
            Console.WriteLine("⏭️ 跳过：需要本地数据");
            return;
        }

        DbFactory.ConfigureForTesting($"Data Source={dbPath}");

        // ── 先看 DB 原文 ──
        var db = DbFactory.GetClient();
        var plotId = db.Queryable<ArkPlot.Core.Model.Plot>()
            .Where(p => p.Title.Contains("CW-ST-1")).Select(p => p.Id).First();
        Console.WriteLine($"═══ DB 原文 (CW-ST-1 idx 68-80) ═══");
        var dbEntries = db.Queryable<ArkPlot.Core.Model.FormattedTextEntry>()
            .Where(e => e.PlotId == plotId && e.Index >= 68 && e.Index <= 80)
            .OrderBy(e => e.Index).ToList();
        foreach (var e in dbEntries)
        {
            var name = string.IsNullOrEmpty(e.CharacterName) ? "(旁白)" : e.CharacterName;
            var dialog = (e.Dialog ?? "").Length > 80 ? (e.Dialog ?? "")[..80] + "…" : (e.Dialog ?? "");
            Console.WriteLine($"  idx={e.Index,3} [{name,-10}] \"{dialog}\"");
        }

        // ── 对齐 ──
        var cacheDir = Path.Combine(Path.GetTempPath(), "arkplot-phase35-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);
        var aligner = new NovelAligner();
        var (entries, stats) = await aligner.AlignByFileNameAsync(novelPath, cacheDir);

        Console.WriteLine($"\n═══ 对齐统计 ═══");
        Console.WriteLine($"对话: {stats.AlignedDialogs}/{stats.TotalDialogs} 已对齐 " +
                          $"(锚点={stats.AnchorMatches}, 窗口={stats.WindowMatches})");

        // ── 看 CW-ST-1 对齐结果 (idx 68-80 范围附近的对话) ──
        var cwst1 = entries.Where(e => e.ChapterTitle.Contains("CW-ST-1")).ToList();
        Console.WriteLine($"\n═══ CW-ST-1 对齐结果 (含 idx 68-80 附近的对话) ═══");
        foreach (var e in cwst1.Where(e => e.IsDialog && e.EntryIndex >= 68 && e.EntryIndex <= 80))
        {
            var text = e.NovelText.Length > 60 ? e.NovelText[..60] + "…" : e.NovelText;
            Console.WriteLine($"  → idx={e.EntryIndex,3} [{e.CharacterName ?? "?",-10}] \"{text}\"");
        }

        // ── 也看 EntryIndex=-1 的对话 ──
        var unaligned = cwst1.Where(e => e.IsDialog && e.EntryIndex < 0).ToList();
        Console.WriteLine($"\n═══ CW-ST-1 未对齐对话 ({unaligned.Count} 条) ═══");
        foreach (var e in unaligned)
        {
            var text = e.NovelText.Length > 60 ? e.NovelText[..60] + "…" : e.NovelText;
            Console.WriteLine($"  → idx={e.EntryIndex,3} [{e.CharacterName ?? "?",-10}] \"{text}\"");
        }
    }

    public void Dispose() => DbFactory.Reset();
}
