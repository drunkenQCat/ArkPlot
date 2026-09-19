using System.Text.RegularExpressions;

namespace ArkPlot.Novelizer;

/// <summary>
/// 章节分块器：将章节正文按 --- 分隔线拆分为小段，再合并为指定大小的 chunk。
/// 段是原子单位，不切半段；最后一个 chunk 允许略超 chunkSize。
/// </summary>
public static partial class ChapterChunker
{
    /// <summary>
    /// 分割并合并为 chunk 列表
    /// </summary>
    /// <param name="chapterBody">章节正文（不含 ## 标题）</param>
    /// <param name="chunkSize">目标字符数，默认 10_000</param>
    /// <returns>chunk 列表，每个 chunk 包含一个或多个原始段，段间以 \n\n---\n\n 连接</returns>
    public static IReadOnlyList<string> ChunkChapter(string chapterBody, int chunkSize = 10_000)
    {
        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");

        var segments = SplitSegments(chapterBody);
        if (segments.Count == 0)
            return Array.Empty<string>();

        // 如果整章不超过 chunkSize，不分块
        if (chapterBody.Length <= chunkSize)
            return new[] { chapterBody };

        var chunks = new List<string>();
        var currentChunk = new List<string>();
        var currentSize = 0;

        // "---" 是剧情脚本的首选边界，但并非所有导入文本都有它。把超长段落再按
        // 段落/句子/字符边界拆开，避免长章悄悄降级为一次超长请求。
        foreach (var segment in segments.SelectMany(s => SplitOversizedSegment(s, chunkSize)))
        {
            // 空段跳过（首尾可能产生空段）
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var segSize = segment.Length;

            // 当前 chunk 非空且加上此段会超出阈值 → 封存当前 chunk
            if (currentChunk.Count > 0 && currentSize + segSize > chunkSize)
            {
                chunks.Add(string.Join("\n\n---\n\n", currentChunk));
                currentChunk.Clear();
                currentSize = 0;
            }

            currentChunk.Add(segment);
            currentSize += segSize;
        }

        // 收尾
        if (currentChunk.Count > 0)
        {
            chunks.Add(string.Join("\n\n---\n\n", currentChunk));
        }

        return chunks;
    }

    private static IEnumerable<string> SplitOversizedSegment(string segment, int chunkSize)
    {
        if (segment.Length <= chunkSize)
        {
            yield return segment;
            yield break;
        }

        var remaining = segment;
        while (remaining.Length > chunkSize)
        {
            var splitAt = FindSplitPoint(remaining, chunkSize);
            yield return remaining[..splitAt].Trim();
            remaining = remaining[splitAt..].TrimStart();
        }

        if (remaining.Length > 0)
            yield return remaining;
    }

    /// <summary>优先在自然边界截断；找不到时在目标长度处硬切，保证必然前进。</summary>
    private static int FindSplitPoint(string text, int chunkSize)
    {
        var searchStart = Math.Max(0, chunkSize * 2 / 3);
        for (var i = Math.Min(chunkSize - 1, text.Length - 1); i >= searchStart; i--)
        {
            if (text[i] is '\n' or '。' or '！' or '？' or '；' or '.' or '!' or '?' or ';')
                return i + 1;
        }

        return chunkSize;
    }

    /// <summary>
    /// 按 --- 分隔线拆分原始段
    /// </summary>
    private static List<string> SplitSegments(string text)
    {
        // 归一化换行
        text = text.Replace("\r\n", "\n");

        // 以 --- 独立行（前后可能有空白行）为分割点
        var parts = DashSeparatorRegex().Split(text);

        return parts
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }

    /// <summary>
    /// 匹配 --- 独立行（前后允许空行）
    /// </summary>
    [GeneratedRegex(@"\n{2,}---\n{2,}", RegexOptions.Compiled)]
    private static partial Regex DashSeparatorRegex();
}
