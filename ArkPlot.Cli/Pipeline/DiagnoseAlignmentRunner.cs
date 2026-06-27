using ArkPlot.Core.Infrastructure;
using ArkPlot.Tts.Alignment;

namespace ArkPlot.Cli.Pipeline;

/// <summary>
/// diagnose-alignment 命令：追踪单个小说片段在各对齐 Phase 中的匹配过程。
/// 用法: diagnose-alignment <novel_file.md> <章节标题> <目标文本> [--db <db_path>]
/// </summary>
public static class DiagnoseAlignmentRunner
{
    public static async Task RunAsync(string novelFilePath, string chapterTitle, string targetText, string? dbPath = null)
    {
        if (!File.Exists(novelFilePath))
        {
            Console.Error.WriteLine($"❌ 文件不存在: {novelFilePath}");
            return;
        }

        // 如果指定了 DB 路径，配置 DbFactory
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
        Console.WriteLine("║        Alignment Diagnostic             ║");
        Console.WriteLine("╚══════════════════════════════════════════╝");
        Console.WriteLine();

        var aligner = new NovelAligner();
        AlignmentDiagnostic diag;
        try
        {
            diag = await aligner.DiagnoseChapterAsync(novelFilePath, chapterTitle, targetText);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ {ex.Message}");
            return;
        }

        // ── 概要 ──
        Console.WriteLine($"目标文本: \"{diag.TargetText}\"");
        Console.WriteLine($"章节:     {diag.ChapterTitle}");
        Console.WriteLine($"文件:     {Path.GetFileName(diag.NovelFilePath)}");
        Console.WriteLine($"NovelIdx: {diag.NovelDialogIdx}");
        Console.WriteLine($"最终 DB EntryIndex: {(diag.FinalEntryIndex >= 0 ? diag.FinalEntryIndex.ToString() : "❌ 未对齐")}");
        Console.WriteLine();

        // ── Phase 1 ──
        PrintPhase1(diag.Phase1);

        // ── Phase 3 ──
        PrintPhase3(diag.Phase3);

        // ── Phase 3.5 ──
        PrintPhase35(diag.Phase35);

        // ── 结论 ──
        Console.WriteLine("── 结论 ──");
        if (diag.FinalEntryIndex >= 0)
        {
            var phase = diag.Phase1?.Matched == true ? "Phase 1 (锚点)"
                : diag.Phase31Matched ? "Phase 3.1 (高压缩间隙全局回退)"
                : diag.Phase35Check4Matched ? "Phase 3.5 Check 4 (长文本拆分匹配)"
                : diag.Phase35Check5Matched ? "Phase 3.5 Check 5 (短文本专用匹配)"
                : diag.Phase35?.Fixed == true ? "Phase 3.5 (碎片修复)"
                : "Phase 3 (窗口匹配)";
            Console.WriteLine($"  ✅ 已对齐到 DB EntryIndex={diag.FinalEntryIndex}，来源: {phase}");
            if (diag.MatchedDbEntryIndex >= 0)
                Console.WriteLine($"     DB 原始 Index={diag.MatchedDbEntryIndex}");
        }
        else
        {
            Console.WriteLine("  ❌ 未对齐");
            if (diag.Phase3?.OutOfWindowCandidates.Count > 0)
            {
                Console.WriteLine("  ⚠️  窗口外存在高分候选但被排除:");
                foreach (var c in diag.Phase3.OutOfWindowCandidates)
                    Console.WriteLine($"     DB[{c.DbIdx}] score={c.Score:F3} \"{Truncate(c.DbText, 60)}\"");
                Console.WriteLine($"  💡 建议: 增大 WindowSize(当前={NovelAligner.WindowSize}) 或调整预期位置计算");
            }
        }
    }

    private static void PrintPhase1(Phase1Diag? p1)
    {
        Console.WriteLine("── Phase 1: 锚点匹配 ──");
        if (p1 == null)
        {
            Console.WriteLine("  (未执行)");
            return;
        }

        if (p1.Matched)
            Console.WriteLine($"  ✅ 命中锚点 → DB[{p1.MatchedDbIdx}]");
        else
            Console.WriteLine($"  ❌ {p1.SkipReason}");
        Console.WriteLine();
    }

    private static void PrintPhase3(Phase3Diag? p3)
    {
        Console.WriteLine("── Phase 3: 窗口匹配 ──");
        if (p3 == null)
        {
            Console.WriteLine("  (未执行)");
            return;
        }

        if (p3.SkipReason != null)
        {
            Console.WriteLine($"  ⏭️  跳过: {p3.SkipReason}");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"  间隙 #{p3.GapIndex}: 锚点 A[{p3.PrevAnchorNi}]→DB[{p3.PrevAnchorDi}] .. A[{p3.NextAnchorNi}]→DB[{p3.NextAnchorDi}]");
        Console.WriteLine($"  间隙大小: novel={p3.NGap}, db={p3.DGap}");
        Console.WriteLine($"  目标位置: gap[{p3.GapPosition}], 预期 DB 偏移={p3.ExpectedPos}");
        Console.WriteLine($"  搜索范围: DB[{p3.PrevAnchorDi + 1 + p3.SearchStart}..{p3.PrevAnchorDi + 1 + p3.SearchEnd}]");

        Console.WriteLine();
        if (p3.Candidates.Count == 0)
        {
            Console.WriteLine("  (窗口内无候选)");
        }
        else
        {
            Console.WriteLine($"  窗口内候选 ({p3.Candidates.Count} 个):");
            Console.WriteLine($"  {"DB Idx",-8} {"Score",-8} 文本预览");
            Console.WriteLine($"  {new string('-', 8),-8} {new string('-', 8),-8} {new string('-', 40)}");
            foreach (var c in p3.Candidates)
            {
                var marker = c.IsSelected ? "← 选中" : "";
                Console.WriteLine($"  [{c.DbIdx,-6}] {c.Score,-8:F3} {Truncate(c.DbText, 50)}{marker}");
            }
        }

        if (p3.OutOfWindowCandidates.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  ⚠️  窗口外高分候选 ({p3.OutOfWindowCandidates.Count} 个):");
            foreach (var c in p3.OutOfWindowCandidates)
                Console.WriteLine($"  [{c.DbIdx,-6}] {c.Score,-8:F3} {Truncate(c.DbText, 50)}");
        }

        if (p3.Matched)
            Console.WriteLine($"\n  → 窗口匹配结果: DB[{p3.MatchedDbIdx}] (score={p3.MatchedScore:F3})");
        else
            Console.WriteLine($"\n  → 窗口匹配未命中 (最高分 < {NovelAligner.WindowMatchThreshold})");

        Console.WriteLine();
    }

    private static void PrintPhase35(Phase35Diag? p35)
    {
        Console.WriteLine("── Phase 3.5: 碎片修复 ──");
        if (p35 == null)
        {
            Console.WriteLine("  (未执行)");
            return;
        }

        Console.WriteLine($"  进入时已对齐: {(p35.WasAligned ? "是" : "否")}");

        PrintCheck("检查1 (前邻子串)", p35.Check1,
            c => $"前邻 DB[{c.PrevDbIdx}] \"{Truncate(c.PrevDbText, 40)}\"");

        PrintCheck("检查2 (后邻子串)", p35.Check2,
            c => $"后邻 DB[{c.NextDbIdx}] \"{Truncate(c.NextDbText, 40)}\"");

        PrintCheck("检查3 (合并搜索)", p35.Check3,
            c => $"合并文本 \"{Truncate(c.MergedText, 40)}\"");

        if (p35.Fixed)
            Console.WriteLine($"  ✅ 碎片修复成功 → DB[{p35.FixedToDbIdx}]");
        else
        {
            // Check 4 and Check 5 don't have diagnostic objects, so we just show if they were tried
            Console.WriteLine("  ❌ 碎片修复未命中");
        }

        Console.WriteLine();
    }

    private static void PrintCheck<T>(string label, T? check, Func<T, string> detail) where T : class
    {
        if (check == null)
        {
            Console.WriteLine($"  ⏭️  {label}: (条件不满足，跳过)");
            return;
        }

        Console.WriteLine($"  ┌ {label}: {detail(check)}");
        if (check is Check1Diag c1)
        {
            Console.WriteLine(c1.IsSubstring
                ? "  └ ✅ 命中"
                : $"  └ ❌ {c1.SkipReason}");
        }
        else if (check is Check2Diag c2)
        {
            Console.WriteLine(c2.IsSubstring
                ? "  └ ✅ 命中"
                : $"  └ ❌ {c2.SkipReason}");
        }
        else if (check is Check3Diag c3)
        {
            if (c3.Matched)
                Console.WriteLine($"  └ ✅ 命中 → DB[{c3.MatchedDbIdx}]");
            else
                Console.WriteLine($"  └ ❌ {c3.SkipReason}");
        }
    }

    private static string Truncate(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "(空)";
        return text.Length <= maxLen ? text : text[..maxLen] + "…";
    }
}