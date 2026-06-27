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
        Console.WriteLine($"未对齐: {stats.UnalignedDialogs}");

        // ── 全局未对齐对话分析 ──
        var allUnaligned = entries.Where(e => e.IsDialog && e.EntryIndex < 0).ToList();
        Console.WriteLine($"\n═══ 全局未对齐对话 ({allUnaligned.Count} 条) ═══");

        if (allUnaligned.Count > 0)
        {
            // 按长度分布
            var byLen = allUnaligned
                .GroupBy(e => e.NovelText.Length switch { <= 10 => "≤10字", <= 20 => "11-20字", <= 40 => "21-40字", _ => "40+字" })
                .OrderBy(g => g.Key);
            foreach (var g in byLen)
                Console.WriteLine($"  {g.Key}: {g.Count()} 条");

            // 按章节分布
            var byChapter = allUnaligned.GroupBy(e => e.ChapterTitle).OrderBy(g => g.Key);
            Console.WriteLine($"\n── 按章节分布 ──");
            foreach (var g in byChapter)
                Console.WriteLine($"  {g.Key}: {g.Count()} 条");

            // 典型样本
            Console.WriteLine($"\n── 样本（前 30）──");
            foreach (var e in allUnaligned.Take(30))
            {
                var text = e.NovelText.Replace('\n', ' ');
                if (text.Length > 70) text = text[..70] + "…";
                Console.WriteLine($"  [{e.ChapterTitle,-15}] [{e.CharacterName ?? "—",-8}] \"{text}\"");
            }
        }
    }

    public void Dispose() => DbFactory.Reset();
}
