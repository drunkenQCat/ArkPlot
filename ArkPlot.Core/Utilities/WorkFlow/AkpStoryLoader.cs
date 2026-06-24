using System.Text.RegularExpressions;
using System.Threading;
using ArkPlot.Core.Model;
using ArkPlot.Core.Services;
using ArkPlot.Core.Utilities.PrtsComponents;
using ArkPlot.Core.Utilities.TagProcessingComponents;
using PreloadSet = System.Collections.Generic.HashSet<System.Collections.Generic.KeyValuePair<string, string>>;

namespace ArkPlot.Core.Utilities.WorkFlow;

/// <summary>
/// 从GitHub获取明日方舟各个章节数据的类。
/// </summary>
public class AkpStoryLoader
{
    public string StoryName { get; }
    private readonly string lang;
    private readonly long _actId;
    private readonly List<StoryChapter> _chapters;

    private readonly NotificationBlock notifyBlock = NotificationBlock.Instance;

    /// <param name="act">当前活动</param>
    /// <param name="chapters">该活动下的章节列表</param>
    public AkpStoryLoader(Act act, List<StoryChapter> chapters)
    {
        StoryName = act.Name;
        lang = act.Lang;
        _actId = act.Id;
        _chapters = chapters;
    }

    /// <summary>
    /// 当前活动内所有章节的内容。
    /// </summary>
    public List<PlotManager> ContentTable { get; set; } = new();

    /// <summary>
    /// 获取GitHub上对应本次活动的RAW数据URL的开头。
    /// </summary>
    private string GetRawUrl()
    {
        return GitHubProxy.GetStoryBaseUrl(lang);
    }

    /// <summary>
    /// 获取所有章节名称。
    /// </summary>
    public Task<IEnumerable<string>> GetChapterNamesAsync()
    {
        var chapterUrlTable = GetChapterUrls();
        return Task.FromResult(chapterUrlTable.Keys.AsEnumerable());
    }

    /// <summary>
    /// 下载所有章节的文本。优先从 PlotCache 加载已缓存章节。
    /// </summary>
    public async Task GetAllChapters()
    {
        var chapterUrlTable = GetChapterUrls();
        await GetAllChapters(chapterUrlTable.Keys);
    }

    /// <summary>
    /// 下载指定章节的文本内容。已缓存章节从 DB 加载（Status=2），
    /// 未缓存章节从 GitHub 下载并写入 Status=1 缓存。
    /// </summary>
    /// <param name="chaptersToLoad">需要加载的章节名称列表。</param>
    public async Task GetAllChapters(IEnumerable<string> chaptersToLoad, CancellationToken ct = default)
    {
        var chapterUrlTable = GetChapterUrls();
        var chaptersList = chaptersToLoad.ToList();
        var filteredChapters = chapterUrlTable
            .Where(kvp => chaptersList.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // 启动时清理历史脏缓存（下载失败但被标记为已完成的空内容记录）
        if (_actId != 0)
            await PlotCache.CleanupEmptyPlotsAsync(_actId);

        // 查缓存
        var cachedTitles = _actId != 0
            ? await PlotCache.GetCachedTitlesAsync(_actId)
            : new HashSet<string>();

        // 收集需要下载的章节
        var chaptersToDownload = new List<(string title, string url, long chapterId)>();

        foreach (var chapter in filteredChapters)
        {
            // 已缓存（Status=2）→ 从 DB 加载
            if (cachedTitles.Contains(chapter.Key))
            {
                var loaded = await PlotCache.TryLoadAsync(_actId, chapter.Key);
                if (loaded.HasValue)
                {
                    loaded.Value.Plot.Content = new StringBuilder();
                    var pm = new PlotManager(loaded.Value.Plot);
                    pm.CurrentPlot.TextVariants = loaded.Value.Entries;
                    ContentTable.Add(pm);
                    notifyBlock.OnChapterLoaded(new ChapterLoadedEventArgs(chapter.Key));
                    continue;
                }
            }

            // 未缓存 → 加入下载队列
            chaptersToDownload.Add((chapter.Key, chapter.Value.Url, chapter.Value.ChapterId));
        }

        // 并行下载所有章节内容
        var downloadTasks = chaptersToDownload.Select(async ch =>
        {
            var content = await NetworkUtility.GetAsync(ch.url, ct);
            return (ch.title, ch.chapterId, content);
        }).ToList();

        var downloadedChapters = await Task.WhenAll(downloadTasks);
        ct.ThrowIfCancellationRequested();

        // 串行处理下载结果并写入数据库（避免 SQLite 并发冲突）
        foreach (var (title, chapterId, content) in downloadedChapters)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                notifyBlock.OnNetErrorHappen(new NetworkErrorEventArgs(
                    $"章节 \"{title}\" 下载失败（内容为空），已跳过，不会写入缓存。"));
                continue;
            }

            notifyBlock.OnChapterLoaded(new ChapterLoadedEventArgs(title));
            var plot = new PlotManager(title, new StringBuilder(content), _actId);
            plot.CurrentPlot.StoryChapterId = chapterId;
            plot.InitializePlot();
            ContentTable.Add(plot);

            // 写入 Status=1 缓存（基础下载，未解析）
            if (_actId != 0)
                await PlotCache.SaveAsync(plot.CurrentPlot, plot.CurrentPlot.TextVariants, status: 1);
        }

        ContentTable = ContentTable.OrderBy(plot =>
        {
            var index = chapterUrlTable.Keys.ToList().IndexOf(plot.CurrentPlot.Title);
            return index;
        }).ToList();
    }

    /// <summary>
    /// 获取预加载信息。
    /// </summary>
    public PreloadSet GetPreloadInfo()
    {
        var resourceSets = ContentTable.Select(c =>
        {
            var pl = new PrtsPreloader(c);
            pl.ParseAndCollectAssets();
            return pl;
        }).ToList();
        var toPreLoad = new PreloadSet();
        foreach (var res in resourceSets) toPreLoad.UnionWith(res.Assets);
        PrtsAssets.Instance.PreLoaded = StringDict.FromEnumerable(toPreLoad);
        return toPreLoad;
    }

    /// <summary>
    /// 预加载所有章节相关的资源。
    /// </summary>
    public async Task PreloadAssetsForAllChapters(CancellationToken ct = default)
    {
        var toPreLoad = GetPreloadInfo();
        await PrtsResLoader.DownloadAssets(StoryName, toPreLoad, ct);
    }

    public async Task ParseAllDocuments(string jsonPath, CancellationToken ct = default)
    {
        var parser = new AkpParser(jsonPath);
        foreach (var pm in ContentTable)
        {
            ct.ThrowIfCancellationRequested();
            await pm.StartParseLines(parser);
        }
    }

    /// <summary>
    /// 构建章节 → (下载URL, StoryChapterId) 的映射。
    /// </summary>
    private Dictionary<string, (string Url, long ChapterId)> GetChapterUrls()
    {
        var collection =
            from chapter in _chapters
            let variation = chapter.StoryId.Contains("variation") ? ExtractVariationNumber(chapter.StoryId) : ""
            let title = $"{chapter.StoryCode} {chapter.StoryName} {chapter.AvgTag}{variation}"
            let url = $"{GetRawUrl()}{chapter.StoryTxt}.txt"
            select (title, url, chapter.Id);
        return collection.ToDictionary(x => x.title, x => (x.url, x.Id));
    }

    private static string ExtractVariationNumber(string storyCode)
    {
        var regex = new Regex(@"variation(\d+)");
        var match = regex.Match(storyCode);
        return match.Success ? match.Groups[1].Value : "";
    }
}
