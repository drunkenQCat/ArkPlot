using System.IO;
using ArkPlot.Core.Model;
using EntryList = System.Collections.Generic.List<ArkPlot.Core.Model.FormattedTextEntry>;

namespace ArkPlot.Core.Utilities.WorkFlow.StoryDocument;

/// <summary>
/// Readable 模式渲染器：HTML 表格 + 散文描述，面向人类阅读。
/// </summary>
internal class ReadableRenderer : IMdRenderer
{
    private readonly bool _enableDescriptions;
    private readonly HashSet<string> _describedImages;

    public string GroupSeparator => "\r\n\r\n---\r\n\r\n";

    public ReadableRenderer(bool enableDescriptions, HashSet<string> describedImages)
    {
        _enableDescriptions = enableDescriptions;
        _describedImages = describedImages;
    }

    public List<string> Render(EntryList grp)
    {
        var mdList = new List<string>();
        foreach (var entry in grp)
        {
            if (string.IsNullOrWhiteSpace(entry.MdText))
                continue;

            var mdText = entry.MdText;
            while (mdText.StartsWith("> "))
                mdText = mdText[2..];

            mdList.Add(mdText);

            if (!_enableDescriptions
                || entry.ResourceUrls.Count == 0
                || string.IsNullOrEmpty(entry.PicDesc))
                continue;

            foreach (var url in entry.ResourceUrls)
            {
                if (_describedImages.Contains(url))
                    continue;

                var desc = entry.PicDesc;
                if (string.IsNullOrWhiteSpace(desc)
                    || desc.Trim() == ";"
                    || desc.StartsWith("[PIC_DESC:")
                    || desc.StartsWith("[DESC_ERROR:"))
                    continue;

                _describedImages.Add(url);
                var bgName = Path.GetFileNameWithoutExtension(entry.Bg);
                mdList.Add(
                    $"<p class=\"scene-desc\">【此处为对场景图片{bgName}的描述，请结合上下文将其融入文中】{desc}</p>"
                );
            }
        }
        return mdList;
    }
}