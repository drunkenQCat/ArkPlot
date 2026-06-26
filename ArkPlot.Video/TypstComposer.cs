using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;

namespace ArkPlot.Video;

public static class TypstComposer
{
    private static readonly Lazy<string> InlineTemplateCache = new(LoadAndInlineTemplateCore);

    public static string InlineTemplate => InlineTemplateCache.Value;

    public static string StripImport(string typText)
    {
        if (string.IsNullOrEmpty(typText))
            return typText;
        var lines = typText.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        var callLines = lines.Where(l => !l.StartsWith("#import"));
        return string.Join("\n", callLines);
    }

    private static string LoadAndInlineTemplateCore()
    {
        var templatePath = Path.Combine(AppPaths.TypstTemplateDir(), "template.typ");
        var text = File.ReadAllText(templatePath);

        var idx = text.IndexOf("// @example");
        var defs = idx > 0 ? text[..idx].Trim() : text.Trim();

        defs = defs.Replace("\"pics/", $"\"{AppPaths.TypstPicsRelative}/");
        return defs;
    }

    /// <summary>
    /// 为每个 segment 生成完整 Typst 代码（模板 + 函数调用）。
    /// 返回查询到的 DB 条目字典（按 Id 索引），供 NetworkImageCache 收集图片 URL。
    /// </summary>
    public static Dictionary<long, FormattedTextEntry> Compose(List<VideoSegment> segments)
    {
        if (segments.Count == 0)
            return [];

        var dbEntriesById = new Dictionary<long, FormattedTextEntry>();
        var entryIds = segments
            .Select(s => s.EntryId)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (entryIds.Count > 0)
        {
            var db = DbFactory.GetClient();
            var entries = db
                .Queryable<FormattedTextEntry>()
                .Where(e => entryIds.Contains(e.Id))
                .ToList();

            dbEntriesById = entries.ToDictionary(e => e.Id);
        }

        foreach (var segment in segments)
        {
            if (segment.EntryId <= 0)
                continue;

            if (!dbEntriesById.TryGetValue(segment.EntryId, out var textEntry))
                continue;

            var call = JudgeAvgType(segment, textEntry) switch
            {
                AvgType.Narrator => ComposeNarrator(segment, textEntry),
                AvgType.SingleCharacter => ComposeSingleCharacter(segment, textEntry),
                AvgType.DoubleCharacter => ComposeDoubleCharacter(segment, textEntry),
                _ => "",
            };

            if (!string.IsNullOrEmpty(call))
                segment.TypstCode = InlineTemplate + "\n" + call;
        }

        return dbEntriesById;
    }

    private static AvgType JudgeAvgType(VideoSegment segment, FormattedTextEntry textEntry)
    {
        if (!segment.IsDialog)
            return AvgType.Narrator;

        var portraitCount = RealPortraits(textEntry).Count;
        return portraitCount >= 2 ? AvgType.DoubleCharacter : AvgType.SingleCharacter;
    }

    private static string ComposeNarrator(VideoSegment segment, FormattedTextEntry textEntry)
    {
        var text = EscapeText(segment.NovelText);
        var bg = BgImage(textEntry.Bg);
        var focus = Math.Max(0, textEntry.PortraitFocus);
        var size = NarratorSize(segment.NovelText);
        var portraits = RealPortraits(textEntry);

        if (portraits.Count >= 2)
            return $"#arknights_narrator_2p({text}, {PortraitImage(portraits[0])}, {PortraitImage(portraits[1])}, {bg}, focus: {focus}, size: \"{size}\")";

        var portrait = portraits.Count == 1
            ? PortraitImage(portraits[0])
            : TransparentImage();

        return $"#arknights_narrator({text}, {portrait}, {bg}, focus: {focus}, size: \"{size}\")";
    }

    private static string ComposeSingleCharacter(VideoSegment segment, FormattedTextEntry textEntry)
    {
        var name = EscapeText(segment.CharacterName);
        var text = EscapeText(segment.NovelText);
        var bg = BgImage(textEntry.Bg);
        var focus = Math.Max(0, textEntry.PortraitFocus);

        var portraits = RealPortraits(textEntry);
        var portrait = portraits.Count > 0
            ? PortraitImage(portraits[0])
            : TransparentImage();

        return $"#arknights_sim({name}, {text}, {portrait}, {bg}, focus: {focus})";
    }

    private static string ComposeDoubleCharacter(VideoSegment segment, FormattedTextEntry textEntry)
    {
        var name = EscapeText(segment.CharacterName);
        var text = EscapeText(segment.NovelText);
        var bg = BgImage(textEntry.Bg);
        var focus = Math.Max(0, textEntry.PortraitFocus);

        var portraits = RealPortraits(textEntry);
        var p1 = portraits.Count > 0 ? PortraitImage(portraits[0]) : TransparentImage();
        var p2 = portraits.Count > 1 ? PortraitImage(portraits[1]) : TransparentImage();

        return $"#arknights_sim_2p({name}, {text}, {p1}, {p2}, {bg}, focus: {focus})";
    }

    // ── Typst 片段构建辅助 ─────────────────────────

    /// <summary>过滤空值和 PrtsPreloader 哨兵值 (https://pics/transparent.png)。</summary>
    private static List<string> RealPortraits(FormattedTextEntry e) =>
        e.Portraits?.Where(p =>
            !string.IsNullOrEmpty(p)
            && !p.Contains("pics/transparent.png")
            && !p.Contains("pics\\transparent.png")
        ).ToList() ?? [];

    private static string BgImage(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return $"image(\"{AppPaths.TypstPicsRelative}/transparent.png\", width: 120%)";
        var clean = url.Replace("\\", "/");
        return $"image(\"{clean}\", width: 120%)";
    }

    private static string PortraitImage(string url)
    {
        var clean = url.Replace("\\", "/");
        return $"image(\"{clean}\", height: 150%)";
    }

    private static string TransparentImage() =>
        $"image(\"{AppPaths.TypstPicsRelative}/transparent.png\")";

    private static string EscapeText(string text) =>
        $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t")}\"";

    private static string NarratorSize(string text) => text.Length switch
    {
        <= 30 => "normal",
        <= 60 => "compact",
        _ => "small",
    };
}

public enum AvgType
{
    Narrator,
    SingleCharacter,
    DoubleCharacter,
}
