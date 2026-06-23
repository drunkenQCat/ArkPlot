using ArkPlot.Core.Model;
using CharacterChart = System.Collections.Generic.Dictionary<string, string>;
using EntryList = System.Collections.Generic.List<ArkPlot.Core.Model.FormattedTextEntry>;

namespace ArkPlot.Core.Utilities.WorkFlow.StoryDocument;

/// <summary>
/// 立绘处理管线：角色识别、图表生成、描述注入、立绘行清理。
/// </summary>
public static class PortraitProcessor
{
    public static void DetectPortraits(StoryDocumentContext ctx)
    {
        foreach (var group in ctx.Groups)
        {
            if (group.Any(e => IsPortrait(e)))
                AppendPortrait(ctx, group);
        }
    }

    public static void GenerateCharts(StoryDocumentContext ctx)
    {
        foreach (var portraitGrp in ctx.Portraits)
        {
            var chart = GenerateChartDict(portraitGrp.PortraitMarks);
            MakePortraitChart(ctx, portraitGrp, chart);
        }
    }

    public static void Process(StoryDocumentContext ctx)
    {
        DetectPortraits(ctx);
        GenerateCharts(ctx);
    }

    public static void RemovePortraitLines(StoryDocumentContext ctx)
    {
        foreach (var digit in ctx.PortraitIndexes)
            ctx.Lines[digit].MdText = "";
    }

    private static void AppendPortrait(StoryDocumentContext ctx, EntryList grp)
    {
        var characters = ExtractCharacterInfo(ctx, grp);
        ctx.Portraits.Add(new PortraitGrp(grp, characters));
    }

    private static List<CharacterInfo> ExtractCharacterInfo(StoryDocumentContext ctx, EntryList paragraphLines)
    {
        var characters = new List<CharacterInfo>();
        foreach (var line in paragraphLines)
        {
            if (!IsPortrait(line))
                continue;
            ctx.PortraitIndexes.Add(line.Index);
            var characterName = ExtractCharacterNameFromLines(ctx.Lines, line);
            var url = line.MdText.Split("\r\n")[0];
            characters.Add(
                !string.IsNullOrWhiteSpace(characterName)
                    ? new CharacterInfo(line, characterName, url)
                    : new CharacterInfo(line, "Unknown", string.Empty));
        }
        return characters;
    }

    private static string ExtractCharacterNameFromLines(EntryList lineList, FormattedTextEntry line)
    {
        var nameEntry = lineList.ElementAtOrDefault(line.Index + 1);
        return nameEntry is not null && !string.IsNullOrEmpty(nameEntry.CharacterName)
            ? nameEntry.CharacterName
            : "";
    }

    public static bool IsPortrait(FormattedTextEntry item)
    {
        return item.Type.Contains("Char") || item.Type.Contains("char");
    }

    private static CharacterChart GenerateChartDict(List<CharacterInfo> characters)
    {
        var charaDict = new CharacterChart();
        foreach (var character in characters)
        {
            if (character.Name == "Unknown") continue;
            charaDict[character.Name] = EmbedTitleInPortraitHtml(character);
        }
        return charaDict;
    }

    private static string EmbedTitleInPortraitHtml(CharacterInfo character)
    {
        var htmlTag = new HtmlTagParser(character.PortraitHtml)
        {
            Attributes = { ["title"] = character.Name },
        };
        var portraitUrl = GetPortraitUrl(character.OriginalEntry);
        if (!string.IsNullOrEmpty(portraitUrl))
            htmlTag.Attributes["src"] = portraitUrl;
        return $"<div class=\"crop\">{htmlTag.ReconstructHtml()}</div>";
    }

    private static string GetPortraitUrl(FormattedTextEntry entry)
    {
        _ = entry.CommandSet.TryGetValue("focus", out string? focusIndex);
        if (int.TryParse(focusIndex, out var focusIdx))
        {
            if (focusIdx > 0 && focusIdx <= entry.ResourceUrls.Count)
                return entry.ResourceUrls[focusIdx - 1];
        }
        return "";
    }

    private static void MakePortraitChart(
        StoryDocumentContext ctx,
        PortraitGrp group,
        CharacterChart portraitLinks)
    {
        if (portraitLinks.Count == 0) return;

        var chartItems = string.Join("|", portraitLinks.Values);
        var chartHead = $"|{chartItems}|";
        var chartSeg = $"|{string.Concat(Enumerable.Repeat(" --- |", portraitLinks.Count))}";

        var chartBody = ctx.EnableDescriptions
            ? BuildDescribedChart(ctx, group, portraitLinks)
            : $"{chartHead}\r\n{chartSeg}\r\n\r\n";

        var firstLine = group.SList.First();
        firstLine.MdText = firstLine.MdText.Insert(0, chartBody);
    }

    private static string BuildDescribedChart(
        StoryDocumentContext ctx,
        PortraitGrp group,
        CharacterChart portraitLinks)
    {
        var headCells = string.Join("\r\n", portraitLinks.Values.Select(v => $"    <td>{v}</td>"));
        var descCells = new List<string>();
        foreach (var charName in portraitLinks.Keys)
        {
            var mark = group.PortraitMarks.FirstOrDefault(m => m.Name == charName);
            var desc = "";
            if (mark != null)
            {
                var code = mark.OriginalEntry.CharacterCode;
                if (!string.IsNullOrEmpty(code) && !ctx.DescribedCharacters.Contains(code))
                {
                    ctx.DescribedCharacters.Add(code);
                    desc = GetOrGenerateDescription(mark);
                }
            }
            var cellText = string.IsNullOrEmpty(desc)
                ? charName
                : $"【此处为对{charName}的形象描述，请结合上下文将其融入文中，不要生搬硬套】：{desc}";
            descCells.Add($"    <td>{cellText.Replace("\r\n", "<br>").Replace("\n", "<br>")}</td>");
        }

        return $"<table class=\"portrait-table\">\r\n"
            + $"<tr>\r\n{string.Join("\r\n", headCells)}\r\n</tr>\r\n"
            + $"<tr>\r\n{string.Join("\r\n", descCells)}\r\n</tr>\r\n"
            + $"</table>\r\n\r\n";
    }

    private static string GetOrGenerateDescription(CharacterInfo mark)
    {
        var desc = mark.OriginalEntry.PicDesc;
        if (string.IsNullOrWhiteSpace(desc)
            || desc.StartsWith("[PIC_DESC:")
            || desc.StartsWith("[DESC_ERROR:"))
            return "";
        return desc;
    }
}