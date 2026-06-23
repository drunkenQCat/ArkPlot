using ArkPlot.Core.Model;
using EntryList = System.Collections.Generic.List<ArkPlot.Core.Model.FormattedTextEntry>;
using EntryGroups = System.Collections.Generic.List<System.Collections.Generic.List<ArkPlot.Core.Model.FormattedTextEntry>>;

namespace ArkPlot.Core.Utilities.WorkFlow.StoryDocument;

/// <summary>
/// 重建上下文：持有 StoryDocument 管线运行过程中的所有中间数据。
/// </summary>
public class StoryDocumentContext
{
    public EntryList Lines { get; init; }
    public EntryGroups Groups { get; init; } = new();
    public List<PortraitGrp> Portraits { get; init; } = new();
    public List<int> PortraitIndexes { get; init; } = new();
    public HashSet<string> DescribedCharacters { get; init; } = new();
    public HashSet<string> DescribedImages { get; init; } = new();
    public bool EnableDescriptions { get; init; }

    public StoryDocumentContext(EntryList lines, bool enableDescriptions = true)
    {
        Lines = lines;
        EnableDescriptions = enableDescriptions;
    }
}

public record PortraitGrp(EntryList SList, List<CharacterInfo> PortraitMarks);

public record CharacterInfo(FormattedTextEntry OriginalEntry, string Name, string PortraitHtml);