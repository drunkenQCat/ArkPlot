using System.Threading;
using ArkPlot.Core.Model;
using ArkPlot.Core.Services;

namespace ArkPlot.Core.Utilities.WorkFlow.StoryDocument;

/// <summary>
/// 图片描述富化器：为 Entry 列表中的图片链接预填充 PicDesc 和 PicFacts。
/// 结果直接写入 entry.PicDesc / entry.PicFacts，后续管线无需再访问 PicDescService。
/// </summary>
public static class PicDescEnricher
{
    public static async Task EnrichAsync(IList<FormattedTextEntry> entries, PicDescService picDescService, CancellationToken ct = default)
    {
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.ResourceUrls.Count == 0)
                continue;

            var isPortraitType = entry.Type is "character" or "charactercutin" or "charslot";

            List<string> urlsToDescribe;
            if (isPortraitType && entry.ResourceUrls.Count > 0)
            {
                var resourceIndex = entry.Type == "charslot"
                    ? 0
                    : (entry.PortraitFocus > 0 ? entry.PortraitFocus - 1 : 0);
                if (resourceIndex >= entry.ResourceUrls.Count)
                    resourceIndex = 0;

                var focusUrl = entry.ResourceUrls[resourceIndex];
                urlsToDescribe = new List<string> { focusUrl };
                foreach (var url in entry.ResourceUrls)
                {
                    if (url != focusUrl && !entry.Portraits.Contains(url))
                        urlsToDescribe.Add(url);
                }
            }
            else
            {
                urlsToDescribe = entry.ResourceUrls;
            }

            var characterCode = !string.IsNullOrEmpty(entry.CharacterCode)
                ? entry.CharacterCode : null;

            var descs = new List<string>();
            var factss = new List<string>();
            foreach (var url in urlsToDescribe)
            {
                var result = await picDescService.GetOrCreatePicDescWithFactsAsync(url, characterCode);
                if (!string.IsNullOrEmpty(result.Description))
                    descs.Add(result.Description);
                if (!string.IsNullOrEmpty(result.Facts))
                    factss.Add(result.Facts);
            }
            entry.PicDesc = string.Join("; ", descs);
            entry.PicFacts = string.Join("\n", factss);
        }
    }
}