using ArkPlot.Core.Model;
using EntryList = System.Collections.Generic.List<ArkPlot.Core.Model.FormattedTextEntry>;

namespace ArkPlot.Core.Utilities.WorkFlow.StoryDocument;

/// <summary>
/// StoryDocument 构建器：Entry → 分段 → 立绘处理 → 渲染。
/// 纯同步，不含外部服务调用。描述数据应在构造前通过 PicDescEnricher 预填充。
/// </summary>
public class StoryDocumentBuilder
{
    private readonly StoryDocumentContext _ctx;
    private readonly IMdRenderer _renderer;

    public StoryDocumentBuilder(
        EntryList entries,
        bool enableDescriptions = true,
        OutputMode outputMode = OutputMode.Readable
    )
    {
        _ctx = new StoryDocumentContext(new EntryList(entries), enableDescriptions);
        _renderer = outputMode == OutputMode.PromptOptimized
            ? new PromptRenderer(_ctx.DescribedCharacters)
            : new ReadableRenderer(enableDescriptions, _ctx.DescribedImages);

        RemoveEmptyLines();
        _ctx.Groups.AddRange(SegmentGrouper.Group(_ctx.Lines));
        PortraitProcessor.DetectPortraits(_ctx);
        if (outputMode != OutputMode.PromptOptimized)
            PortraitProcessor.GenerateCharts(_ctx);
        PortraitProcessor.RemovePortraitLines(_ctx);
    }

    private void RemoveEmptyLines()
    {
        var filtered = _ctx.Lines
            .Where(line => !string.IsNullOrWhiteSpace(line.MdText))
            .ToList();
        _ctx.Lines.Clear();
        _ctx.Lines.AddRange(filtered);
        int idx = 0;
        _ctx.Lines.ForEach(line => { line.Index = idx; idx++; });
    }

    public string Result
    {
        get
        {
            var sb = new StringBuilder();
            AppendResultToBuilder(sb);
            return sb.ToString();
        }
    }

    public void AppendResultToBuilder(StringBuilder builder)
    {
        builder.AppendLine();
        var hasContent = false;
        var sep = _renderer.GroupSeparator;
        foreach (var group in _ctx.Groups)
        {
            var lines = _renderer.Render(group);
            if (lines.Count == 0) continue;
            if (hasContent) builder.Append(sep);
            builder.AppendJoin("\r\n\r\n", lines);
            hasContent = true;
        }
        builder.AppendLine();
    }
}