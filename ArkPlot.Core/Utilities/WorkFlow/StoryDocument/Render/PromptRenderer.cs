using System.IO;
using ArkPlot.Core.Model;
using EntryList = System.Collections.Generic.List<ArkPlot.Core.Model.FormattedTextEntry>;

namespace ArkPlot.Core.Utilities.WorkFlow.StoryDocument;

/// <summary>
/// Prompt 模式渲染器：aside + YAML 事实，面向 LLM 输入优化。
/// </summary>
internal class PromptRenderer : IMdRenderer
{
    private readonly HashSet<string> _describedCharacters;

    private static readonly HashSet<string> MusicSkipTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "playmusic", "stopmusic", "musicvolume", "musicstop",
        "soundvolume"
    };

    public string GroupSeparator => "\r\n\r\n---\r\n\r\n";

    public PromptRenderer(HashSet<string> describedCharacters)
    {
        _describedCharacters = describedCharacters;
    }

    public List<string> Render(EntryList grp)
    {
        var mdList = new List<string>();
        foreach (var entry in grp)
        {
            if (MusicSkipTypes.Contains(entry.Type))
                continue;

            if (string.IsNullOrWhiteSpace(entry.MdText))
                continue;

            if (entry.MdText.Trim() == "---")
                continue;

            var mdText = entry.MdText;

            while (mdText.StartsWith("> "))
                mdText = mdText[2..];

            if (entry.Type == "subtitle")
            {
                mdText = mdText.Replace("`居中字幕`：", "");
                mdText = mdText.Replace("`居中字幕`:", "");
            }

            if (!string.IsNullOrEmpty(entry.CharacterName)
                && !string.IsNullOrEmpty(entry.CharacterCode)
                && !_describedCharacters.Contains(entry.CharacterCode))
            {
                _describedCharacters.Add(entry.CharacterCode);
                if (!string.IsNullOrEmpty(entry.PicFacts))
                {
                    mdList.Add(
                        $"<aside class=\"portrait-facts\" data-character=\"{entry.CharacterName}\">\n{entry.PicFacts}\n</aside>");
                }
            }

            mdList.Add(mdText);

            if (entry.Type is "background" or "largebg"
                && !string.IsNullOrEmpty(entry.PicFacts))
            {
                var bgName = Path.GetFileNameWithoutExtension(entry.Bg);
                mdList.Add(
                    $"<aside class=\"scene-facts\" data-bg=\"{bgName}\">\n{entry.PicFacts}\n</aside>");
            }

            if (entry.Type is "showitem" or "cgitem" or "interlude" or "image"
                && !string.IsNullOrEmpty(entry.PicFacts))
            {
                mdList.Add($"<aside class=\"item-facts\">\n{entry.PicFacts}\n</aside>");
            }
        }
        return mdList;
    }
}