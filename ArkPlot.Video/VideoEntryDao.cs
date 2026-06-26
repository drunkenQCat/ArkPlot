using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;

namespace ArkPlot.Video;

/// <summary>
/// 视频生成用的数据访问层：解析 FormattedTextEntry 的唯一 Id。
/// </summary>
public static class VideoEntryDao
{
    /// <summary>
    /// 给定活动名和章节标题，返回该章节下所有 FormattedTextEntry 的 Index→Id 映射。
    /// </summary>
    /// <param name="actName">活动名（如 "孤星"）</param>
    /// <param name="chapterTitle">章节标题（Plot.Title，如 "CW-ST-1 阴云密布 幕间"）</param>
    /// <returns>Index → Id 字典。Index 是 FormattedTextEntry.Index（Plot 内局部序号）。</returns>
    public static Dictionary<int, long> GetIndexToIdMap(string actName, string chapterTitle)
    {
        var db = DbFactory.GetClient();

        var act = db.Queryable<Act>()
            .First(a => a.Name == actName && a.Lang == "zh_CN");
        if (act == null)
            return [];

        var plot = db.Queryable<Plot>()
            .First(p => p.ActId == act.Id && p.Title == chapterTitle);
        if (plot == null)
            return [];

        var entries = db.Queryable<FormattedTextEntry>()
            .Where(e => e.PlotId == plot.Id)
            .Select(e => new { e.Index, e.Id })
            .ToList();

        return entries.ToDictionary(e => e.Index, e => e.Id);
    }

    /// <summary>
    /// 批量版本：一次查询获取多个章节的 Index→Id 映射。
    /// 返回 Dictionary&lt;chapterTitle, Dictionary&lt;Index, Id&gt;&gt;。
    /// </summary>
    public static Dictionary<string, Dictionary<int, long>> GetIndexToIdMapBatch(
        string actName, IEnumerable<string> chapterTitles)
    {
        var db = DbFactory.GetClient();
        var titles = chapterTitles.ToList();
        if (titles.Count == 0)
            return [];

        var act = db.Queryable<Act>()
            .First(a => a.Name == actName && a.Lang == "zh_CN");
        if (act == null)
            return [];

        var plots = db.Queryable<Plot>()
            .Where(p => p.ActId == act.Id && titles.Contains(p.Title))
            .Select(p => new { p.Id, p.Title })
            .ToList();

        if (plots.Count == 0)
            return [];

        var plotIds = plots.Select(p => p.Id).ToList();
        var entries = db.Queryable<FormattedTextEntry>()
            .Where(e => plotIds.Contains(e.PlotId))
            .Select(e => new { e.PlotId, e.Index, e.Id })
            .ToList();

        var plotIdToTitle = plots.ToDictionary(p => p.Id, p => p.Title);

        return entries
            .GroupBy(e => e.PlotId)
            .Where(g => plotIdToTitle.ContainsKey(g.Key))
            .ToDictionary(
                g => plotIdToTitle[g.Key],
                g => g.ToDictionary(e => e.Index, e => e.Id));
    }
}
