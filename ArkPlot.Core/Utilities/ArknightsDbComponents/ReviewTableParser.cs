using Newtonsoft.Json.Linq;

namespace ArkPlot.Core.Utilities.ArknightsDbComponents;

public class ReviewTableParser
{
    private string lang;

    // private string KGithubTableUrl => $"https://raw.kgithub.com/Kengxxiao/ArknightsGameData/master/{lang}/gamedata/excel/story_review_table.json";
    private JObject? reviewTable;

    public ReviewTableParser(string language)
    {
        lang = language;
        LoadJson();
    }

    public ReviewTableParser()
    {
        lang = "zh_CN";
    }

    public string Lang
    {
        get => lang;
        set
        {
            lang = value;
            LoadJson();
        }
    }

    public List<JToken> TitleList => GetSideStory();
    public List<JToken> MiniStory => GetMiniStory();
    public List<JToken> MainStory => GetMainStory();

    private string GetTableUrl()
    {
        return GitHubProxy.GetStoryTableUrl(lang);
    }

    private void LoadJson()
    {
        var jsonContent = NetworkUtility.GetAsync(GetTableUrl()).GetAwaiter().GetResult();
        reviewTable = JObject.Parse(jsonContent);
    }

    public List<JToken> GetStories(string type)
    {
        var stories =
            from item in reviewTable?.Children().ToList()
            let obj = item.ToObject<JProperty>()!.Value
            let actType = obj["actType"]!.ToString()
            where actType == type
            select obj;
        return stories.ToList();
    }

    private List<JToken> GetMiniStory()
    {
        return GetStories("MINI_STORY");
    }

    private List<JToken> GetSideStory()
    {
        return GetStories("ACTIVITY_STORY");
    }

    private List<JToken> GetMainStory()
    {
        return GetStories("MAIN_STORY");
    }
}
