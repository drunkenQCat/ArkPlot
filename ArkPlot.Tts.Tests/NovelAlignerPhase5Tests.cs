using ArkPlot.Core.Infrastructure;
using ArkPlot.Tts.Alignment;
using Xunit;

namespace ArkPlot.Tts.Tests;

/// <summary>
/// Phase 5 smoke test：真实数据验证对齐后旁白不再有 -1 EntryIndex。
/// </summary>
public sealed class NovelAlignerPhase5Tests : IDisposable
{
    [Fact]
    public async Task AlignByFileName_LoneTrail_Chapter1_NoOrphanNarratorEntries()
    {
        var repoRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var dbPath = Path.Combine(repoRoot, "ArkPlot.Avalonia", "bin", "Debug", "net9.0", "arkplot.db");
        var novelPath = Path.Combine(repoRoot, "ArkPlot.Avalonia", "bin", "Debug", "net9.0",
            "output", "孤星", "孤星_novel_deepseek-v4-flash.md");

        if (!File.Exists(dbPath) || !File.Exists(novelPath))
        {
            Console.WriteLine("⏭️ 跳过：需要本地 arkplot.db + 孤星小说文件");
            return;
        }

        DbFactory.ConfigureForTesting($"Data Source={dbPath}");
        var cacheDir = Path.Combine(Path.GetTempPath(), "arkplot-phase5-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);

        var aligner = new NovelAligner();
        var (entries, stats) = await aligner.AlignByFileNameAsync(novelPath, cacheDir);

        Console.WriteLine($"对齐完成: {stats.AlignedDialogs}/{stats.TotalDialogs} 对话, " +
                          $"{entries.Count} 总条目");

        // 按章节分组
        var byChapter = entries.GroupBy(e => e.ChapterTitle).ToList();
        var violations = new List<string>();

        foreach (var group in byChapter)
        {
            var chapterEntries = group.ToList();
            var hasDialog = chapterEntries.Any(e => e.IsDialog && e.EntryIndex >= 0);

            // 只有当章节存在已对齐的对话时，旁白才不应该有 -1
            if (!hasDialog) continue;

            var orphanNarrators = chapterEntries
                .Where(e => !e.IsDialog && e.EntryIndex < 0)
                .ToList();

            foreach (var orphan in orphanNarrators)
            {
                violations.Add(
                    $"[{group.Key}] 旁白 EntryIndex=-1: {Truncate(orphan.NovelText, 50)}");
            }
        }

        if (violations.Count > 0)
        {
            Console.WriteLine($"❌ {violations.Count} 个旁白条目仍为 -1:");
            foreach (var v in violations.Take(10))
                Console.WriteLine($"  {v}");
            if (violations.Count > 10)
                Console.WriteLine($"  ... 及其他 {violations.Count - 10} 条");
        }
        else
        {
            Console.WriteLine("✅ 所有章节的旁白均已继承有效 EntryIndex");
        }

        // 统计 Phase 5 效果
        var totalNarrators = entries.Count(e => !e.IsDialog);
        var inheritedNarrators = entries.Count(e => !e.IsDialog && e.EntryIndex >= 0);
        Console.WriteLine($"旁白统计: {inheritedNarrators}/{totalNarrators} 有有效 EntryIndex");

        Assert.Empty(violations);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    public void Dispose() => DbFactory.Reset();
}
