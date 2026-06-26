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
    /// </summary>
    public static List<NovelSegment> ExtractSegments(string text)
    {
        var segments = new List<NovelSegment>();
        int pos = 0;

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

            var dialog = text[(openIdx + 1)..closeIdx].Trim();
            if (!string.IsNullOrWhiteSpace(dialog))
                segments.Add(new NovelSegment(dialog, IsDialog: true));

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
        text = text.Replace("\u2025", "\u2026");
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text;
    }

    private static void AddNarration(List<NovelSegment> segments, string text)
    {
        // 按段落（双换行）拆分，避免将跨场景的大段旁白合并为一个 segment
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var para in paragraphs)
        {
            var trimmed = para.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                segments.Add(new NovelSegment(trimmed, IsDialog: false));
        }
    }

    [GeneratedRegex(@"^#+\s+", RegexOptions.Multiline)]
    private static partial Regex ChapterSplitRegex();

    [GeneratedRegex(@"(?:\u201C([^\u201D]*)\u201D|""([^""]*)"")")]
    private static partial Regex QuotedDialogRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
