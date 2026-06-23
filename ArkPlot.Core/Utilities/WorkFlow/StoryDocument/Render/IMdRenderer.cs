using ArkPlot.Core.Model;
using EntryList = System.Collections.Generic.List<ArkPlot.Core.Model.FormattedTextEntry>;

namespace ArkPlot.Core.Utilities.WorkFlow.StoryDocument;

/// <summary>
/// Markdown 渲染器接口。
/// </summary>
internal interface IMdRenderer
{
    List<string> Render(EntryList group);
    string GroupSeparator { get; }
}