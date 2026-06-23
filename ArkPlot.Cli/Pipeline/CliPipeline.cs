using ArkPlot.Cli.Dump;

namespace ArkPlot.Cli.Pipeline;

/// <summary>
/// CLI 纯编排器：按顺序调用各 Step，传递中间结果。
/// </summary>
public class CliPipeline
{
    private readonly string _tagsJsonPath;

    public CliPipeline(string tagsJsonPath)
    {
        _tagsJsonPath = tagsJsonPath;
    }

    public async Task RunAsync(string? actTitleFilter = null, int chapterCount = 4)
    {
        Console.WriteLine("=== ArkPlot CLI - 完整流程验证 ===");
        Console.WriteLine($"目标：简中 ACTIVITY_STORY{(actTitleFilter != null ? $"（{actTitleFilter}）" : " 第一个活动")} → 前{chapterCount}章 → 完整解析流程 → Dump JSON → 导出 Markdown");
        Console.WriteLine($"Debug 模式：{(CliOptions.DebugMode ? "开启（强制重写 PicDesc）" : "关闭")}");
        Console.WriteLine($"Vision 模式：{(CliOptions.UseMockVision ? "🧪 Mock（确定性输出）" : CliOptions.EnableVision ? "开启（百炼 生成真实图片描述）" : "关闭（使用占位符）")}");
        Console.WriteLine($"TTS 模式：{(CliOptions.EnableTts ? "开启（EdgeTTS 生成章节音频）" : "关闭")}");
        Console.WriteLine();

        // 1-2. 加载活动 & 获取前N章
        var (_, _, actName, storyLoader) = await ActivityLoader.LoadFirstActivityAsync(actTitleFilter: actTitleFilter);
        var chapters = await ActivityLoader.GetChaptersAsync(storyLoader, chapterCount);
        if (chapters.Count == 0) return;

        // 3. 下载所有章节内容（累积到 ContentTable）
        Console.WriteLine("[3/6] 正在下载章节内容...");
        await storyLoader.GetAllChapters(chapters);
        if (storyLoader.ContentTable.Count == 0)
        {
            Console.WriteLine("❌ 未成功下载任何内容。请检查网络连接。");
            return;
        }

        for (int i = 0; i < storyLoader.ContentTable.Count; i++)
        {
            var pm = storyLoader.ContentTable[i];
            Console.WriteLine($"    📖 {chapters[i]}: {pm.CurrentPlot.Content.Length} 字符, {pm.CurrentPlot.TextVariants.Count} 条目");
        }

        // 4-5. Prts 同步（一次）+ 所有章节资源预加载
        await ResourceLoader.SyncAndPreloadAsync(storyLoader);

        // 6. 逐章解析文档
        foreach (var pm in storyLoader.ContentTable)
        {
            Console.WriteLine($"[6/8] 正在解析：{pm.CurrentPlot.Title}");
            var parser = new ArkPlot.Core.Utilities.WorkFlow.AkpParser(_tagsJsonPath);
            await pm.StartParseLines(parser);

            var entries = pm.CurrentPlot.TextVariants;
            Console.WriteLine($"    共 {entries.Count} 条目, " +
                              $"有效 MdText: {entries.Count(e => !string.IsNullOrWhiteSpace(e.MdText))}, " +
                              $"有 ResourceUrls: {entries.Count(e => e.ResourceUrls.Count > 0)}");

            if (CliOptions.DebugMode)
                DocumentParser.InjectMockResourceUrls(entries);
        }

        // 7. 初始化 PicDescService（不创建 MdReconstructor，避免清空 charslot MdText）
        using var picDescService = PicDescRunner.Run();

        // 8. Dump JSON（逐章）
        Console.WriteLine("[7/8] 正在 Dump JSON（导出前验证）...");
        foreach (var (pm, idx) in storyLoader.ContentTable.Select((pm, i) => (pm, i)))
        {
            var chapterName = idx < chapters.Count ? chapters[idx] : pm.CurrentPlot.Title;
            var dumpResult = DumpService.DumpPlotToJson(pm.CurrentPlot, actName, chapterName);
            Console.WriteLine($"    ✅ {chapterName}: {dumpResult.DumpPath}");
            Console.WriteLine($"       📊 {dumpResult.Stats}, 🖼️ PicDesc: {dumpResult.PicDescCount}");
        }

        // 9. 导出 Markdown（所有章节合并）
        var (outputDir, markdown) = await MarkdownExporter.ExportAsync(storyLoader, picDescService, actName);
        Console.WriteLine($"    ✅ Markdown 已保存：{outputDir}");
        Console.WriteLine($"    📄 文件大小：{markdown.Content.Length} 字符");

        if (picDescService != null)
        {
            var picDescStats = picDescService.GetStats();
            Console.WriteLine($"    🗄️  PicDesc 数据库：{picDescStats.DbCount} 条记录");
            Console.WriteLine($"    📁 图片缓存目录：{picDescStats.CacheFileCount} 个文件，{picDescStats.CacheSizeBytes / 1024} KB");
        }

        // TTS
        if (CliOptions.EnableTts)
        {
            foreach (var (pm, idx) in storyLoader.ContentTable.Select((pm, i) => (pm, i)))
            {
                var chapterName = idx < chapters.Count ? chapters[idx] : pm.CurrentPlot.Title;
                await TtsRunner.RunAsync(pm.CurrentPlot.TextVariants, outputDir, actName, chapterName);
            }
        }

        // 汇总
        Console.WriteLine("\n=== 流程完成 ===");
        Console.WriteLine($"活动：{actName}");
        Console.WriteLine($"章节：{string.Join(", ", chapters)}");
        Console.WriteLine($"Markdown：{Path.Combine(outputDir, $"{markdown.Title}.md")}");
    }
}
