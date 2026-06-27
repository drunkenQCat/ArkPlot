using ArkPlot.Cli.Pipeline;
using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;

// 命令行参数解析
if (args.Length > 0 && args[0].Equals("align", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("用法: ArkPlot.Cli align <novel_file.md>");
        return;
    }
    await AlignRunner.RunAsync(args[1]);
    return;
}

if (args.Length > 0 && args[0].Equals("dump-db", StringComparison.OrdinalIgnoreCase))
{
    DbDumpRunner.Run();
    return;
}

if (args.Length > 0 && args[0].Equals("copy-avalonia-db", StringComparison.OrdinalIgnoreCase))
{
    DbDumpRunner.CopyFromAvalonia();
    return;
}

if (args.Length > 0 && args[0].Equals("show-misaligned", StringComparison.OrdinalIgnoreCase))
{
    DbDumpRunner.ShowMisalignedCharacters();
    return;
}

if (args.Length > 0 && args[0].Equals("tts-novel", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("用法: ArkPlot.Cli tts-novel <aligned.json> [segment_limit]");
        return;
    }
    int? limit = args.Length >= 3 && int.TryParse(args[2], out var l) ? l : null;
    await NovelTtsRunner.RunAsync(args[1], limit);
    return;
}

if (args.Length > 0 && args[0].Equals("chapter-tts", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("用法: ArkPlot.Cli chapter-tts <novel_file.md> [--limit N] [--debug-voice]");
        Console.Error.WriteLine("  对小说化 md 按章节生成 MP3（自动对齐 + TTS）");
        Console.Error.WriteLine("  --limit N: 每章只生成前 N 个片段（快速测试）");
        Console.Error.WriteLine("  --debug-voice: 只输出音色分配表，不生成音频");
        return;
    }
    int? limit = null;
    bool debugVoice = false;
    for (int i = 2; i < args.Length; i++)
    {
        if (args[i] == "--limit" && i + 1 < args.Length && int.TryParse(args[i + 1], out var l))
        {
            limit = l;
            i++;
        }
        else if (args[i] == "--debug-voice")
        {
            debugVoice = true;
        }
    }
    await ChapterTtsRunner.RunAsync(args[1], limit, debugVoice);
    return;
}

if (args.Length > 0 && args[0].Equals("verify-tts", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("用法: ArkPlot.Cli verify-tts <output_dir> [--segments N]");
        Console.Error.WriteLine("  端到端验证整套 TTS 工作流");
        Console.Error.WriteLine("  --segments N: 测试片段数 (默认 3)");
        return;
    }
    int segments = 3;
    for (int i = 2; i < args.Length; i++)
    {
        if (args[i] == "--segments" && i + 1 < args.Length && int.TryParse(args[i + 1], out var s))
        {
            segments = s;
            i++;
        }
    }
    await VerifyTtsRunner.RunAsync(args[1], segments);
    return;
}

if (args.Length > 0 && args[0].Equals("diagnose-tts-assets", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("用法: ArkPlot.Cli diagnose-tts-assets <act_name>");
        Console.Error.WriteLine("  诊断立绘和背景图在 DB 中的真实数据");
        return;
    }
    await DiagnoseTtsAssetsRunner.RunAsync(args[1]);
    return;
}

if (args.Length > 0 && args[0].Equals("simulate-tts-click", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("用法: ArkPlot.Cli simulate-tts-click <novel_file.md> <角色名> [点击第几行]");
        Console.Error.WriteLine("  模拟点击某角色的一行，验证立绘和 Gallery 组件输入");
        return;
    }
    int clickRow = args.Length >= 4 && int.TryParse(args[3], out var r) ? r : 3;
    await SimulateTtsClickRunner.RunAsync(args[1], args[2], clickRow);
    return;
}

if (args.Length > 0 && args[0].Equals("verify-tts-component-inputs", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("用法: ArkPlot.Cli verify-tts-component-inputs <novel_file.md> <角色名>");
        Console.Error.WriteLine("  自动化验证立绘和 Gallery 组件输入是否正确");
        return;
    }
    var exitCode = await VerifyTtsComponentInputsRunner.RunAsync(args[1], args[2]);
    Environment.Exit(exitCode);
    return;
}

if (args.Length > 0 && args[0].Equals("repair-db-portraits", StringComparison.OrdinalIgnoreCase))
{
    string? dbPath = null;
    long? plotId = null;
    bool dryRun = false;
    string? outPath = null;
    int show = 20;

    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--db" && i + 1 < args.Length)
        {
            dbPath = args[i + 1];
            i++;
        }
        else if (args[i] == "--plot" && i + 1 < args.Length && long.TryParse(args[i + 1], out var p))
        {
            plotId = p;
            i++;
        }
        else if (args[i] == "--out" && i + 1 < args.Length)
        {
            outPath = args[i + 1];
            i++;
        }
        else if (args[i] == "--show" && i + 1 < args.Length && int.TryParse(args[i + 1], out var s))
        {
            show = s;
            i++;
        }
        else if (args[i] == "--dry-run")
        {
            dryRun = true;
        }
    }

    var exitCode = await RepairDbPortraitsRunner.RunAsync(dbPath, plotId, dryRun, outPath, show);
    Environment.Exit(exitCode);
    return;
}

if (args.Length > 0 && args[0].Equals("diagnose-charslot", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("用法: ArkPlot.Cli diagnose-charslot <novel_file.md>");
        return;
    }
    await DiagnoseCharSlotRunner.RunAsync(args[1]);
    return;
}

if (args.Length > 0 && args[0].Equals("diagnose-alignment", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 4)
    {
        Console.Error.WriteLine("用法: ArkPlot.Cli diagnose-alignment <novel_file.md> <章节标题> <目标文本> [--db <db_path>]");
        Console.Error.WriteLine("  追踪单个小说片段在各对齐 Phase 中的匹配过程");
        Console.Error.WriteLine("  示例: ArkPlot.Cli diagnose-alignment \"xxx_novel.md\" \"CW-1 迷雾重重 行动前\" \"真有趣，\" --db \"path/to/arkplot.db\"");
        return;
    }

    string? dbPath = null;
    for (int i = 4; i < args.Length; i++)
    {
        if (args[i] == "--db" && i + 1 < args.Length)
        {
            dbPath = args[i + 1];
            i++;
        }
    }
    await DiagnoseAlignmentRunner.RunAsync(args[1], args[2], args[3], dbPath);
    return;
}

if (args.Length > 0 && args[0].Equals("diagnose-alignment-batch", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("用法: ArkPlot.Cli diagnose-alignment-batch <novel_file.md> [--db <db_path>] [--threshold <ratio>]");
        Console.Error.WriteLine("  批量检测对齐异常并汇总问题");
        Console.Error.WriteLine("  --threshold: 长度比例阈值 (默认 0.3，低于此值视为异常)");
        Console.Error.WriteLine("  示例: ArkPlot.Cli diagnose-alignment-batch \"xxx_novel.md\" --db \"path/to/arkplot.db\"");
        return;
    }

    string? dbPath2 = null;
    double threshold = 0.3;
    for (int i = 2; i < args.Length; i++)
    {
        if (args[i] == "--db" && i + 1 < args.Length)
        {
            dbPath2 = args[i + 1];
            i++;
        }
        else if (args[i] == "--threshold" && i + 1 < args.Length && double.TryParse(args[i + 1], out var t))
        {
            threshold = t;
            i++;
        }
    }
    await DiagnoseAlignmentBatchRunner.RunAsync(args[1], dbPath2, threshold);
    return;
}

if (args.Length == 0)
{
    // 清空旧的 PicDescription 记录
    var db = DbFactory.GetClient();
    var before = db.Queryable<PicDescription>().Count();
    db.Deleteable<PicDescription>().ExecuteCommand();
    Console.WriteLine($"已清空 PicDescription 表（{before} 条旧记录）");

    var tagsJson = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tags.json");
    var pipeline = new CliPipeline(tagsJson);
    await pipeline.RunAsync();
}
else
{
    Console.Error.WriteLine($"未知命令: {args[0]}");
    Console.Error.WriteLine("可用命令: align, dump-db, copy-avalonia-db, show-misaligned, tts-novel, chapter-tts, verify-tts, diagnose-tts-assets, simulate-tts-click, verify-tts-component-inputs, repair-db-portraits, diagnose-charslot, diagnose-alignment, diagnose-alignment-batch");
}
