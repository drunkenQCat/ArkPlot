using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;
using ArkPlot.Core.Utilities.WorkFlow;
using ArkPlot.Core.Utilities.WorkFlow.StoryDocument;
using Xunit;

namespace ArkPlot.Novelizer.Tests;

/// <summary>
/// MdReconstructor 回归测试 — 孤星第一章 Golden 验证。
/// 确保重构过程中 Markdown 输出、分组、立绘位置、描述插入位置保持一致。
/// </summary>
public class MdReconstructorGoldenTests
{
    private static readonly string ProjectRoot = FindProjectRoot();
    private static readonly string GoldenDir = Path.Combine(ProjectRoot, "ArkPlot.Novelizer.Tests", "Golden");
    private static readonly string DbPath = Path.Combine(
        ProjectRoot, "ArkPlot.Avalonia", "bin", "Debug", "net9.0", "arkplot.db");

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "ArkPlot.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Cannot find ArkPlot.sln");
    }

    /// <summary>
    /// 加载孤星第一章数据，生成 MdReconstructor 输出。
    /// </summary>
    private static (string Readable, string Prompt) GenerateOutputs()
    {
        if (!File.Exists(DbPath))
            throw new FileNotFoundException($"数据库不存在: {DbPath}。请先在 Avalonia 中解析孤星活动。");

        DbFactory.ConfigureForTesting($"Data Source={DbPath}");
        var db = DbFactory.GetClient();

        var plot = db.Queryable<Plot>()
            .Where(p => p.Title.Contains("CW-ST-1"))
            .First();

        if (plot == null)
            throw new InvalidOperationException("未找到孤星第一章 (CW-ST-1)。请先在 Avalonia 中解析孤星活动。");

        var entries = db.Queryable<FormattedTextEntry>()
            .Where(e => e.PlotId == plot.Id)
            .OrderBy(e => e.Index)
            .ToList();

        // 传播 CharacterCode：charslot 条目 → 后续对话条目
        PropagateCharacterCode(entries);

        // 从 PicDescription 表填充 PicDesc 和 PicFacts
        PopulatePicDescs(entries, db);

        // Readable 模式
        var readableEntries = entries.Select(e => new FormattedTextEntry(e)).ToList();
        var readableReconstructor = new StoryDocumentBuilder(
            readableEntries,
            enableDescriptions: true,
            outputMode: OutputMode.Readable);
        var readableMd = new System.Text.StringBuilder();
        readableMd.Append($"## {plot.Title}\r\n\r\n");
        readableReconstructor.AppendResultToBuilder(readableMd);

        // Prompt 模式
        var promptEntries = entries.Select(e => new FormattedTextEntry(e)).ToList();
        var promptReconstructor = new StoryDocumentBuilder(
            promptEntries,
            enableDescriptions: true,
            outputMode: OutputMode.PromptOptimized);
        var promptMd = new System.Text.StringBuilder();
        promptMd.Append($"## {plot.Title}\r\n\r\n");
        promptReconstructor.AppendResultToBuilder(promptMd);

        return (readableMd.ToString(), promptMd.ToString());
    }

    /// <summary>
    /// 将 charslot/character 条目的 CharacterCode 传播到后续对话条目。
    /// </summary>
    private static void PropagateCharacterCode(List<FormattedTextEntry> entries)
    {
        var nameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? pendingCode = null;

        foreach (var entry in entries)
        {
            if (entry.Type is "character" or "charactercutin" or "charslot"
                && !string.IsNullOrEmpty(entry.CharacterCode))
            {
                pendingCode = entry.CharacterCode;
            }
            else if (!string.IsNullOrEmpty(entry.CharacterName) && string.IsNullOrEmpty(entry.CharacterCode))
            {
                if (nameToCode.TryGetValue(entry.CharacterName, out var knownCode))
                {
                    entry.CharacterCode = knownCode;
                }
                else if (pendingCode != null)
                {
                    nameToCode[entry.CharacterName] = pendingCode;
                    entry.CharacterCode = pendingCode;
                    pendingCode = null;
                }
            }
        }
    }

    /// <summary>
    /// 从 PicDescription 表填充 entries 的 PicDesc 和 PicFacts 字段。
    /// </summary>
    private static void PopulatePicDescs(List<FormattedTextEntry> entries, SqlSugar.ISqlSugarClient db)
    {
        foreach (var entry in entries)
        {
            // 有 CharacterCode 的条目按 CharacterCode 查
            if (!string.IsNullOrEmpty(entry.CharacterCode))
            {
                var record = db.Queryable<PicDescription>()
                    .Where(r => r.DedupKey == entry.CharacterCode && r.Source == "Vision")
                    .First();
                if (record != null)
                {
                    if (!string.IsNullOrEmpty(record.PicDesc))
                        entry.PicDesc = record.PicDesc;
                    if (!string.IsNullOrEmpty(record.PicFacts))
                        entry.PicFacts = record.PicFacts;
                    continue;
                }
            }

            // 有 ResourceUrls 的条目按 URL 查
            foreach (var url in entry.ResourceUrls)
            {
                var record = db.Queryable<PicDescription>()
                    .Where(r => (r.DedupKey == url || r.ImageUrl == url) && r.Source == "Vision")
                    .First();
                if (record != null)
                {
                    if (!string.IsNullOrEmpty(record.PicDesc))
                        entry.PicDesc = string.IsNullOrEmpty(entry.PicDesc)
                            ? record.PicDesc
                            : entry.PicDesc + "; " + record.PicDesc;
                    if (!string.IsNullOrEmpty(record.PicFacts))
                        entry.PicFacts = string.IsNullOrEmpty(entry.PicFacts)
                            ? record.PicFacts
                            : entry.PicFacts + "\n" + record.PicFacts;
                }
            }
        }
    }

    /// <summary>
    /// 验证 Readable 模式输出与 Golden 文件一致。
    /// </summary>
    [Fact]
    public void LoneTrail_Chapter1_Readable_ShouldMatchGolden()
    {
        var (readable, _) = GenerateOutputs();
        var goldenPath = Path.Combine(GoldenDir, "LoneTrail_Chapter1_Readable.md");
        AssertGoldenMatches(readable, goldenPath);
    }

    /// <summary>
    /// 验证 Prompt 模式输出与 Golden 文件一致。
    /// </summary>
    [Fact]
    public void LoneTrail_Chapter1_Prompt_ShouldMatchGolden()
    {
        var (_, prompt) = GenerateOutputs();
        var goldenPath = Path.Combine(GoldenDir, "LoneTrail_Chapter1_Prompt.md");
        AssertGoldenMatches(prompt, goldenPath);
    }

    /// <summary>
    /// 输出统计信息（辅助诊断）。
    /// </summary>
    [Fact]
    public void LoneTrail_Chapter1_Stats()
    {
        var (readable, prompt) = GenerateOutputs();

        int Count(string text, string pattern)
        {
            int count = 0, index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
            { count++; index += pattern.Length; }
            return count;
        }

        Console.WriteLine("=== Readable 模式 ===");
        Console.WriteLine($"  总字符数: {readable.Length}");
        Console.WriteLine($"  portrait-table: {Count(readable, "class=\"portrait-table\"")}");
        Console.WriteLine($"  scene-desc: {Count(readable, "class=\"scene-desc\"")}");

        Console.WriteLine("=== Prompt 模式 ===");
        Console.WriteLine($"  总字符数: {prompt.Length}");
        Console.WriteLine($"  scene-facts: {Count(prompt, "<aside class=\"scene-facts\"")}");
        Console.WriteLine($"  portrait-facts: {Count(prompt, "<aside class=\"portrait-facts\"")}");
        Console.WriteLine($"  item-facts: {Count(prompt, "<aside class=\"item-facts\"")}");
        Console.WriteLine($"  旧 scene-desc: {Count(prompt, "class=\"scene-desc\"")}（应为 0）");
        Console.WriteLine($"  旧 portrait-table: {Count(prompt, "class=\"portrait-table\"")}（应为 0）");
    }

    private static void AssertGoldenMatches(string actual, string goldenPath)
    {
        if (!File.Exists(goldenPath))
        {
            Directory.CreateDirectory(GoldenDir);
            File.WriteAllText(goldenPath, actual);
            Assert.True(true, $"Golden 文件已创建: {goldenPath}。请检查内容后重新运行测试。");
            return;
        }

        var expected = File.ReadAllText(goldenPath);

        if (expected != actual)
        {
            // 输出差异长度帮助诊断
            var diffPath = Path.ChangeExtension(goldenPath, ".diff.md");
            File.WriteAllText(diffPath, actual);
            Assert.Fail(
                $"Golden 不匹配！\n" +
                $"  Golden: {goldenPath} ({expected.Length} 字符)\n" +
                $"  Actual: {actual.Length} 字符\n" +
                $"  Diff 已写入: {diffPath}");
        }
    }
}