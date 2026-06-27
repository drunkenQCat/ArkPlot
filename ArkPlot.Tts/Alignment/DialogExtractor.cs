using System.Text.RegularExpressions;

namespace ArkPlot.Tts.Alignment;

/// <summary>
/// 从小说化文本中提取章节结构和对话/旁白分段。
/// </summary>
public static partial class DialogExtractor
{
    /// <summary>
    /// 将小说文本按 ## 标题拆分为章节，每章内提取旁白/对话交替的片段列表。
    /// </summary>
    public static List<NovelChapter> ExtractChapters(string novelText)
    {
        var chapters = new List<NovelChapter>();
        var chapterChunks = ChapterSplitRegex().Split(novelText);

        foreach (var chunk in chapterChunks)
        {
            var trimmed = chunk.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var lines = trimmed.Split('\n', 2);
            var title = lines[0].Trim();
            var body = lines.Length > 1 ? lines[1].Trim() : "";

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body)) continue;

            var segments = ExtractSegments(body);
            chapters.Add(new NovelChapter(title, segments));
        }

        return chapters;
    }

    /// <summary>
    /// 从一段正文中提取旁白/对话交替的片段。
    /// 识别中文弯引号 "" 和 ASCII 直引号 "" 内的内容作为对话。
    /// 按 \n\n 分段，为每个自然段内的片段分配相同的 Paragraph 索引。
    /// </summary>
    public static List<NovelSegment> ExtractSegments(string text)
    {
        var segments = new List<NovelSegment>();
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        for (int p = 0; p < paragraphs.Length; p++)
        {
            var para = paragraphs[p].Trim();
            if (string.IsNullOrWhiteSpace(para)) continue;

            var paraSegments = ExtractSegmentsCore(para);
            foreach (var seg in paraSegments)
                segments.Add(seg with { Paragraph = p });
        }

        return segments;
    }

    /// <summary>
    /// 从单个自然段中提取旁白/对话交替的片段（不含 Paragraph 分配）。
    /// </summary>
    private static List<NovelSegment> ExtractSegmentsCore(string text)
    {
        var segments = new List<NovelSegment>();
        int pos = 0;
        
        // 预扫描：统计段落中短引号内容的数量
        int shortQuoteCount = 0;
        int scanPos = 0;
        while (scanPos < text.Length)
        {
            int curveOpen = text.IndexOf('\u201C', scanPos);
            int straightOpen = text.IndexOf('"', scanPos);
            int openIdx = -1;
            char closeChar = '\0';
            
            if (curveOpen >= 0 && (straightOpen < 0 || curveOpen < straightOpen))
            {
                openIdx = curveOpen;
                closeChar = '\u201D';
            }
            else if (straightOpen >= 0)
            {
                openIdx = straightOpen;
                closeChar = '"';
            }
            
            if (openIdx < 0) break;
            
            int closeIdx = text.IndexOf(closeChar, openIdx + 1);
            if (closeIdx < 0) break;
            
            var dialog = text[(openIdx + 1)..closeIdx].Trim();
            if (dialog.Length <= 4)
                shortQuoteCount++;
            
            scanPos = closeIdx + 1;
        }
        
        // 如果段落包含多个短引号内容，则所有短引号都视为引用词
        bool hasMultipleShortQuotes = shortQuoteCount >= 2;

        while (pos < text.Length)
        {
            // 找最近的开引号（弯引号 " 或直引号 "）
            int curveOpen = text.IndexOf('\u201C', pos); // "
            int straightOpen = text.IndexOf('"', pos);   // "

            int openIdx;
            char closeChar;

            if (curveOpen < 0 && straightOpen < 0)
            {
                AddNarration(segments, text[pos..]);
                break;
            }

            if (curveOpen >= 0 && (straightOpen < 0 || curveOpen < straightOpen))
            {
                openIdx = curveOpen;
                closeChar = '\u201D'; // "
            }
            else if (straightOpen >= 0 && (curveOpen < 0 || straightOpen < curveOpen))
            {
                openIdx = straightOpen;
                closeChar = '"'; // "
            }
            else
            {
                AddNarration(segments, text[pos..]);
                break;
            }

            if (openIdx > pos)
                AddNarration(segments, text[pos..openIdx]);

            int closeIdx = text.IndexOf(closeChar, openIdx + 1);
            if (closeIdx < 0)
            {
                AddNarration(segments, text[openIdx..]);
                break;
            }

            // 提取引号内的内容
            var dialog = text[(openIdx + 1)..closeIdx].Trim();
            
            // 启发式过滤：判断是否为真正的对话
            // 1. 如果引号内容很短（<=4字符）且段落包含多个短引号，则视为引用词
            // 2. 如果引号内容很短（<=4字符）且前后都有叙述文本，则视为引用词
            bool isLikelyDialog = true;
            if (dialog.Length <= 4)
            {
                if (hasMultipleShortQuotes)
                {
                    isLikelyDialog = false;
                }
                else
                {
                    var beforeQuote = text[pos..openIdx].Trim();
                    var afterQuote = closeIdx + 1 < text.Length ? text[(closeIdx + 1)..].Trim() : "";
                    
                    // 如果引号前有文本（>=5字符）且引号后也有文本（>=5字符），则可能是引用词
                    if (beforeQuote.Length >= 5 && afterQuote.Length >= 5)
                    {
                        isLikelyDialog = false;
                    }
                }
            }
            
            if (!string.IsNullOrWhiteSpace(dialog))
            {
                if (isLikelyDialog)
                {
                    segments.Add(new NovelSegment(dialog, 0, IsDialog: true));
                }
                else
                {
                    // 将短引号内容作为叙述处理
                    AddNarration(segments, dialog);
                }
            }

            pos = closeIdx + 1;
        }

        return segments;
    }

    /// <summary>
    /// 提取文本中所有引号内的对话（纯字符串列表，不含分段信息）。
    /// 支持中文弯引号和 ASCII 直引号。
    /// </summary>
    public static List<string> ExtractDialogs(string text)
    {
        var dialogs = new List<string>();
        var matches = QuotedDialogRegex().Matches(text);
        foreach (Match match in matches)
        {
            // Group 1 = 弯引号内容, Group 2 = 直引号内容
            var dialog = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim();
            if (!string.IsNullOrWhiteSpace(dialog))
                dialogs.Add(dialog);
        }
        return dialogs;
    }

    /// <summary>
    /// 标准化对话文本，用于比较时消除标点差异。
    /// </summary>
    public static string Normalize(string text)
    {
        text = text.Replace("......", "\u2026");
        text = text.Replace(".....", "\u2026");
        text = text.Replace("....", "\u2026");
        text = text.Replace("\u2025", "\u2026");
        // 统一多个连续省略号为单个
        while (text.Contains("\u2026\u2026"))
            text = text.Replace("\u2026\u2026", "\u2026");
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text;
    }

    private static void AddNarration(List<NovelSegment> segments, string text)
    {
        var trimmed = text.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
            segments.Add(new NovelSegment(trimmed, 0, IsDialog: false));
    }

    [GeneratedRegex(@"^#+\s+", RegexOptions.Multiline)]
    private static partial Regex ChapterSplitRegex();

    [GeneratedRegex(@"(?:\u201C([^\u201D]*)\u201D|""([^""]*)"")")]
    private static partial Regex QuotedDialogRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
