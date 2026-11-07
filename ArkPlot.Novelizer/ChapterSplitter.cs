using System.Text.RegularExpressions;

namespace ArkPlot.Novelizer;

/// <summary>
/// 章节拆分器：负责从 Markdown 内容中按 ## 标题拆分章节
/// </summary>
public static partial class ChapterSplitter
{
    [GeneratedRegex(@"^#{1,6}\s*", RegexOptions.Multiline)]
    private static partial Regex MarkdownHeadingRegex();

    [GeneratedRegex(@"^(?=##[ \t]+)", RegexOptions.Multiline)]
    private static partial Regex ChapterSplitRegex();

    [GeneratedRegex(@"^#{1,6}[ \t]+")]
    private static partial Regex HeadingPrefixRegex();

    [GeneratedRegex(@"^(?:#{1,6}[ \t]+.*|(?:-[ \t]*){3,}|(?:\*[ \t]*){3,}|(?:_[ \t]*){3,})$")]
    private static partial Regex NonBodyLineRegex();

    public static string? GetDocumentTitle(string content)
    {
        var firstLine = content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n').Split('\n', 2)[0].Trim();
        return firstLine.StartsWith("# ") ? firstLine[2..].Trim() : null;
    }

    /// <summary>
    /// 将预处理后的 Markdown 内容拆分为章节列表
    /// </summary>
    public static IReadOnlyList<Chapter> SplitChapters(string processedContent)
    {
        var rawChapters = ChapterSplitRegex().Split(processedContent)
            .Select(s => s.Trim().TrimStart('\uFEFF'))
            .Where(s => s.Length > 0)
            .ToList();

        var chapters = new List<Chapter>(rawChapters.Count);
        for (int i = 0; i < rawChapters.Count; i++)
        {
            var chunk = rawChapters[i];
            var lines = chunk.Split('\n', 2);
            var hasHeading = HeadingPrefixRegex().IsMatch(lines[0]);
            var title = hasHeading ? HeadingPrefixRegex().Replace(lines[0], "").Trim() : "正文";
            var body = hasHeading ? (lines.Length > 1 ? lines[1].Trim() : "") : chunk;
            if (hasHeading && lines[0].StartsWith("# ")) title = rawChapters.Count > 1 ? "序言" : "正文";

            // A book title followed by whitespace or a separator is metadata, not a chapter.
            // Keep genuine preface text and heading-free input instead of dropping narrative.
            if (!body.Split('\n').Any(line => !string.IsNullOrWhiteSpace(line) && !NonBodyLineRegex().IsMatch(line.Trim())))
                continue;

            chapters.Add(new Chapter(chapters.Count, title, body));
        }

        return chapters;
    }

    /// <summary>
    /// 去除文本中的 Markdown 标题（# ## ### 等），保留正文
    /// </summary>
    public static string StripHeadings(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return MarkdownHeadingRegex().Replace(text, "");
    }
}

/// <summary>
/// 表示一个待处理的章节
/// </summary>
public record Chapter(int Index, string Title, string Body);
