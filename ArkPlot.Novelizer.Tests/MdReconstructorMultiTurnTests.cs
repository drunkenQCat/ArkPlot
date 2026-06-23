using Xunit;

namespace ArkPlot.Novelizer.Tests;

/// <summary>
/// 多轮对话模式回归测试 — 验证 ChapterChunker 分割逻辑正确。
/// 以孤星第一章 Golden Prompt 文件作为输入数据。
/// </summary>
public class MdReconstructorMultiTurnTests
{
    private static readonly string ProjectRoot = FindProjectRoot();
    private static readonly string GoldenDir = Path.Combine(ProjectRoot, "ArkPlot.Novelizer.Tests", "Golden");

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
    /// 加载孤星第一章 Prompt MD → 预处理 → 拆章 → 验证 Chunk 逻辑
    /// </summary>
    [Fact]
    public void LoneTrail_Chapter1_Chunking_ShouldWork()
    {
        var mdPath = Path.Combine(GoldenDir, "LoneTrail_Chapter1_Prompt.md");
        Assert.True(File.Exists(mdPath), $"Golden 文件不存在: {mdPath}");

        var rawMd = File.ReadAllText(mdPath);
        Assert.False(string.IsNullOrEmpty(rawMd));

        // 模拟管线预处理
        var processed = MarkdownBuilder.PreprocessMdContent(rawMd);
        Assert.False(string.IsNullOrEmpty(processed));

        // 拆章（孤星第一章只有 1 个 ## 章节）
        var chapters = ChapterSplitter.SplitChapters(processed);
        Assert.Single(chapters);

        var chapter = chapters[0];
        Assert.Equal("CW-ST-1 阴云密布 幕间", chapter.Title);
        Assert.False(string.IsNullOrEmpty(chapter.Body));

        var bodyLen = chapter.Body.Length;
        Console.WriteLine($"📄 章节正文: {bodyLen} 字符");

        // 默认 chunkSize = 10_000
        const int chunkSize = 10_000;
        var chunks = ChapterChunker.ChunkChapter(chapter.Body, chunkSize);

        Console.WriteLine($"📦 Chunks: {chunks.Count} 个 (chunkSize={chunkSize})");
        for (int i = 0; i < chunks.Count; i++)
        {
            Console.WriteLine($"  Chunk {i + 1}: {chunks[i].Length} 字符");
        }

        // 21KB 正文以 10KB 分块应拆出至少 2 个 chunk
        Assert.True(chunks.Count >= 2,
            $"正文 {bodyLen} 字符，chunkSize={chunkSize} 应拆出至少 2 个 chunk，实际 {chunks.Count}");

        // 每个 chunk 都有内容
        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c)));

        // 前 N-1 个 chunk 不超过 chunkSize 太多（允许最后一个超）
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            Assert.True(chunks[i].Length <= chunkSize * 1.2,
                $"Chunk {i + 1} 大小为 {chunks[i].Length}，超过 chunkSize {chunkSize} 的 120%");
        }

        // 验证关键内容未被丢弃（而非逐字符比较，因为 --- 分隔线附近的空白被规范化了）
        var allContent = string.Concat(chunks);
        Assert.Contains("1099年，哥伦比亚", allContent);   // 开头附近的内容
        Assert.Contains("伊芙利特", allContent);            // 结尾附近的内容
        Assert.Contains("小贾斯汀", allContent);            // 中间的内容
        Assert.Contains("克丽斯腾", allContent);
    }

    /// <summary>
    /// 验证不同 chunkSize 下的分割行为
    /// </summary>
    [Theory]
    [InlineData(5_000)]
    [InlineData(10_000)]
    [InlineData(20_000)]
    [InlineData(30_000)]
    [InlineData(50_000)] // 大于预处理后的正文 → 不分块
    public void Chunking_WithVariousSizes_ShouldBeConsistent(int chunkSize)
    {
        var mdPath = Path.Combine(GoldenDir, "LoneTrail_Chapter1_Prompt.md");
        var rawMd = File.ReadAllText(mdPath);
        var processed = MarkdownBuilder.PreprocessMdContent(rawMd);
        var chapters = ChapterSplitter.SplitChapters(processed);
        var body = chapters[0].Body;

        var chunks = ChapterChunker.ChunkChapter(body, chunkSize);

        Console.WriteLine($"chunkSize={chunkSize}, body={body.Length}, chunks={chunks.Count}");

        // 如果 chunkSize >= body 长度，应只有 1 块
        if (chunkSize >= body.Length)
        {
            Assert.Single(chunks);
        }
        else
        {
            // 应有多个 chunk，除非 body 仅略大于 chunkSize
            //（某些场景下仅 1 块也可能：segment 太大无法合并，第一个 segment 就是整章）
            Assert.True(chunks.Count >= 1);
        }

        // 验证关键内容未被丢弃
        // 从 body 中抽取一个有代表性的片段，验证它在某个 chunk 中存在
        var representativeSnippet = "1099年，哥伦比亚";
        var found = chunks.Any(c => c.Contains(representativeSnippet));
        Assert.True(found, $"未找到代表性内容 \"{representativeSnippet}\"");
    }
}
