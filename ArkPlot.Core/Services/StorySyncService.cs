using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;
using ArkPlot.Core.Utilities;
using Newtonsoft.Json.Linq;
using SqlSugar;
using System.Net.Http;

namespace ArkPlot.Core.Services;

/// <summary>
/// 活动列表同步服务：SHA 验证 → 下载 story_review_table.json → 写入 Acts / StoryChapters 表。
/// </summary>
public class StorySyncService
{
    private readonly SqlSugarClient _db;

    public StorySyncService()
    {
        _db = DbFactory.GetClient();
    }

    /// <summary>
    /// 获取指定仓库的最新 commit SHA。
    /// GitHub API 需要 User-Agent，否则返回 403。
    /// </summary>
    public static async Task<string?> GetLatestCommitShaAsync(string repo)
    {
        var url = GitHubProxy.GetUrl($"https://api.github.com/repos/{repo}/commits?per_page=1");
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "ArkPlot/1.0");
            var json = await client.GetStringAsync(url);
            var arr = JArray.Parse(json);
            return arr[0]?["sha"]?.ToString();
        }
        catch (Exception ex)
        {
            GitHubProxy.CheckConnectionError(url, exception: ex);
            return null;
        }
    }

    /// <summary>
    /// 根据语言获取仓库标识。
    /// </summary>
    public static string GetRepoByLang(string lang)
    {
        return lang == "zh_CN"
            ? "Kengxxiao/ArknightsGameData"
            : "Kengxxiao/ArknightsGameData_YoStar";
    }

    /// <summary>
    /// 获取 story_review_table.json 的下载 URL。
    /// </summary>
    public static string GetTableUrl(string lang)
    {
        if (lang == "zh_CN")
            return GitHubProxy.GetUrl($"https://raw.githubusercontent.com/Kengxxiao/ArknightsGameData/master/{lang}/gamedata/excel/story_review_table.json");

        return GitHubProxy.GetUrl($"https://raw.githubusercontent.com/Kengxxiao/ArknightsGameData_YoStar/master/{lang}/gamedata/excel/story_review_table.json");
    }

    /// <summary>
    /// 查 DB 中指定语言的 SyncState。
    /// </summary>
    public SyncState? GetSyncState(string lang)
    {
        var repo = GetRepoByLang(lang);
        return _db.Queryable<SyncState>().First(s => s.Lang == lang && s.Repo == repo);
    }

    /// <summary>
    /// 更新 SyncState。
    /// </summary>
    public void UpsertSyncState(string lang, string sha)
    {
        var repo = GetRepoByLang(lang);
        var existing = _db.Queryable<SyncState>().First(s => s.Lang == lang && s.Repo == repo);
        if (existing != null)
        {
            existing.LastCommitSha = sha;
            existing.UpdatedAt = DateTime.UtcNow;
            _db.Updateable(existing).ExecuteCommand();
        }
        else
        {
            _db.Insertable(new SyncState
            {
                Lang = lang,
                Repo = repo,
                LastCommitSha = sha,
                UpdatedAt = DateTime.UtcNow
            }).ExecuteCommand();
        }
    }

    /// <summary>
    /// 更新 SyncState 中 PRTS 同步标记。
    /// </summary>
    public void UpdatePrtsSyncSha(string lang, string? sha)
    {
        var repo = GetRepoByLang(lang);
        var existing = _db.Queryable<SyncState>().First(s => s.Lang == lang && s.Repo == repo);
        if (existing != null)
        {
            existing.PrtsSyncedAtSha = sha;
            _db.Updateable(existing).ExecuteCommand();
        }
    }

    /// <summary>
    /// 下载 story_review_table.json，按业务键 upsert 到 Acts + StoryChapters。
    /// Acts  按 (ActId, Lang) 匹配 → ID 稳定不变
    /// Chapters 按 (ActId, StoryId) 匹配 → upsert 后清理已移除的章节
    /// </summary>
    public async Task<(List<Act> Acts, List<StoryChapter> Chapters)> DownloadAndSaveAsync(string lang)
    {
        var json = await NetworkUtility.GetAsync(GetTableUrl(lang));
        var table = JObject.Parse(json);

        var allActs = new List<Act>();
        var allChapters = new List<StoryChapter>();

        // ===== 解析 JSON =====
        foreach (var item in table.Properties())
        {
            var obj = (JObject)item.Value;

            var act = new Act
            {
                ActId = item.Name,
                Lang = lang,
                Name = obj["name"]?.ToString() ?? item.Name,
                ActType = obj["actType"]?.ToString() ?? "UNKNOWN"
            };
            allActs.Add(act);

            var infoUnlockDatas = obj["infoUnlockDatas"] as JArray;
            if (infoUnlockDatas == null) continue;

            foreach (var ch in infoUnlockDatas)
            {
                allChapters.Add(new StoryChapter
                {
                    StoryId = ch["storyId"]?.ToString() ?? "",
                    StoryCode = ch["storyCode"]?.ToString() ?? "",
                    StoryName = ch["storyName"]?.ToString() ?? "",
                    StoryTxt = ch["storyTxt"]?.ToString() ?? "",
                    AvgTag = ch["avgTag"]?.ToString(),
                    StorySort = ch["storySort"]?.Value<int>() ?? 0,
                    StoryDependence = ch["storyDependence"]?.ToString()
                });
            }
        }

        // ===== Act upsert：按 (ActId, Lang) 匹配，ID 不变 =====
        foreach (var act in allActs)
        {
            var existing = _db.Queryable<Act>()
                .First(a => a.ActId == act.ActId && a.Lang == act.Lang);

            if (existing != null)
            {
                act.Id = existing.Id;
                _db.Updateable(act)
                    .WhereColumns(it => new { it.ActId, it.Lang })
                    .ExecuteCommand();
            }
            else
            {
                act.Id = _db.Insertable(act).ExecuteReturnIdentity();
            }
        }

        // ===== 将 ActId 填入章节 =====
        var actLookup = allActs.ToDictionary(a => a.ActId);
        var index = 0;
        foreach (var item in table.Properties())
        {
            var act = actLookup[item.Name];
            var infoUnlockDatas = item.Value["infoUnlockDatas"] as JArray;
            if (infoUnlockDatas == null) continue;

            foreach (var chToken in infoUnlockDatas)
            {
                allChapters[index].ActId = act.Id;
                index++;
            }
        }

        // ===== 收集本次所有有效 StoryId =====
        var validStoryIds = new HashSet<string>(allChapters.Select(c => c.StoryId));

        // ===== Chapter upsert：按 (ActId, StoryId) 匹配 =====
        _db.Storageable(allChapters)
            .WhereColumns(it => new { it.ActId, it.StoryId })
            .ExecuteCommand();

        // ===== 删除已不在 JSON 中的章节 =====
        var langActIds = allActs.Select(a => a.Id).ToList();
        _db.Deleteable<StoryChapter>()
            .Where(sc => langActIds.Contains(sc.ActId))
            .Where(sc => !validStoryIds.Contains(sc.StoryId))
            .ExecuteCommand();

        return (allActs, allChapters);
    }

    /// <summary>
    /// 从 DB 读取指定语言的所有活动。
    /// </summary>
    public List<Act> GetActsFromDb(string lang)
    {
        return _db.Queryable<Act>().Where(a => a.Lang == lang).ToList();
    }

    /// <summary>
    /// 从 DB 读取指定活动的章节列表。
    /// </summary>
    public List<StoryChapter> GetChaptersByActId(long actId)
    {
        return _db.Queryable<StoryChapter>().Where(s => s.ActId == actId)
            .OrderBy(s => s.StorySort).ToList();
    }

    /// <summary>
    /// 按活动类型过滤。
    /// </summary>
    public List<Act> GetActsByType(string lang, string actType)
    {
        return _db.Queryable<Act>()
            .Where(a => a.Lang == lang && a.ActType == actType)
            .ToList();
    }
}
