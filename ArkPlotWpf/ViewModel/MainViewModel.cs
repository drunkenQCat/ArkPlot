using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using ArkPlot.Core.Model;
using ArkPlot.Core.Services;
using ArkPlot.Core.Utilities;
using ArkPlot.Core.Utilities.PrtsComponents;
using ArkPlot.Core.Utilities.TagProcessingComponents;
using ArkPlot.Core.Utilities.WorkFlow;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Newtonsoft.Json;

// ReSharper disable InconsistentNaming

namespace ArkPlotWpf.ViewModel;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly StorySyncService storySync = new();

    private readonly NotificationBlock noticeBlock = NotificationBlock.Instance;
    private readonly PrtsDataProcessor prts = new();

    [ObservableProperty]
    private string consoleOutput =
        @"这是一个生成明日方舟剧情markdown/html文件的生成器，使用时有以下注意事项:

    - 因为下载剧情文本需要连接GitHub的服务器，所以在使用时务必先科学上网；
    - 如果遇到报错【出错的句子:****】，如过于影响阅读体验，需要结合报错信息填写相应正则表达式来规整，请点击“编辑Tags”按钮，添加相应tag的项目；
    - 如果有任何改进意见，欢迎Pr。";

    private List<Act> currentActs = new();

    [ObservableProperty]
    private bool isInitialized;

    [ObservableProperty]
    private bool isLocalResChecked;

    [ObservableProperty]
    private string jsonPath = Path.Combine(Environment.CurrentDirectory, "tags.json");

    private string language = "zh_CN";

    [ObservableProperty]
    private string outputPath = Path.Combine(Environment.CurrentDirectory, "output");

    private string outputPathOfCurrentStory => Path.Combine(outputPath, activeTitle);

    [ObservableProperty]
    private int selectedIndex;

    [ObservableProperty]
    private ICollectionView? storiesNames = CollectionViewSource.GetDefaultView(
        new[] { "加载中，请稍等..." }
    );

    private string storyType = "ACTIVITY_STORY";
    private string? activeTitle;

    private Act CurrentAct => currentActs[SelectedIndex];

    [RelayCommand]
    private async Task LoadInitResource()
    {
        SubscribeAll();
        await LoadResourceTable();
        await LoadLangTable(language);
        IsInitialized = true;
    }

    private void SubscribeAll()
    {
        SubscribeCommonNotification();
        SubscribeChapterLoadedNotification();
        SubscribeNetErrorNotification();
        SubscribeLineNoMatchNotification();
    }

    private async Task LoadResourceTable()
    {
        try
        {
            await prts.EnsureSyncedAsync();
            noticeBlock.RaiseCommonEvent("【prts资源索引文件加载完成】\r\n");
        }
        catch (Exception)
        {
            var s = "\r\n网络错误，无法加载资源文件。\r\n";
            noticeBlock.RaiseCommonEvent(s);
            MessageBox.Show(s);
        }
    }

    [RelayCommand]
    private async Task LoadLangTable(string lang)
    {
        try
        {
            language = lang;
            await SyncActsAsync(lang);
            noticeBlock.RaiseCommonEvent("【剧情索引文件加载完成】\r\n");
            LoadActs(storyType);
        }
        catch (Exception)
        {
            var s = "\r\n索引文件加载出错！请检查网络代理。\r\n";
            noticeBlock.RaiseCommonEvent(s);
            MessageBox.Show(s);
        }
    }

    private async Task SyncActsAsync(string lang)
    {
        var repo = StorySyncService.GetRepoByLang(lang);
        var remoteSha = await StorySyncService.GetLatestCommitShaAsync(repo);
        var localSha = storySync.GetSyncState(lang)?.LastCommitSha;

        if (remoteSha != null && remoteSha != localSha)
        {
            await storySync.DownloadAndSaveAsync(lang);
            storySync.UpsertSyncState(lang, remoteSha);
        }
        else if (remoteSha == null)
        {
            var existing = storySync.GetActsFromDb(lang);
            if (existing.Count == 0)
                throw new Exception("无法连接到 GitHub，且本地无缓存数据。");
        }
    }

    [RelayCommand]
    private void LoadActs(string type)
    {
        storyType = type;
        currentActs = storySync.GetActsByType(language, type);
        StoriesNames = CollectionViewSource.GetDefaultView(
            from act in currentActs
            select act.Name
        );
        SelectedIndex = 0;
    }

    [RelayCommand]
    private async Task LoadMd()
    {
        PrepareLoading();
        var chapters = storySync.GetChaptersByActId(CurrentAct.Id);
        var content = new AkpStoryLoader(CurrentAct, chapters);
        await LoadAllChapters(content);
        await PreloadResources(content);
        await StartParseDocuments(content);
        await ExportDocuments(content);
        CompleteLoading();
    }

    private void PrepareLoading()
    {
        IsInitialized = false;
        ClearConsoleOutput();
        noticeBlock.RaiseCommonEvent("初始化加载...");
    }

    private async Task LoadAllChapters(AkpStoryLoader contentLoader)
    {
        activeTitle = CurrentAct.Name;
        await contentLoader.GetAllChapters();
        noticeBlock.RaiseCommonEvent("章节加载完成。");
    }

    private async Task PreloadResources(AkpStoryLoader contentLoader)
    {
        noticeBlock.RaiseCommonEvent("正在预加载资源....");
        if (IsLocalResChecked)
        {
            noticeBlock.RaiseCommonEvent("正在下载资源....");
            await contentLoader.PreloadAssetsForAllChapters();
        }
        else
        {
            await Task.Run(contentLoader.GetPreloadInfo);
        }
    }

    private async Task StartParseDocuments(AkpStoryLoader content)
    {
        noticeBlock.RaiseCommonEvent("正在解析文档....");
        await content.ParseAllDocuments(jsonPath);
    }

    private async Task ExportDocuments(AkpStoryLoader contentLoader)
    {
        noticeBlock.RaiseCommonEvent("正在导出文档....");
        var rawMd = await ExportPlots(contentLoader.ContentTable);
        var rawMdWithTitle = "# " + (activeTitle ?? "") + "\r\n\r\n" + rawMd;
        ExportMdAndHtmlFiles(rawMdWithTitle);
        if (IsLocalResChecked)
        {
            AkpProcessor.WriteTyp(outputPathOfCurrentStory, contentLoader);
        }
    }

    private void ExportMdAndHtmlFiles(string mdWithTitle)
    {
        if (!Directory.Exists(outputPathOfCurrentStory))
            Directory.CreateDirectory(outputPathOfCurrentStory);
        var rawMarkdown = new Plot(activeTitle ?? "", new StringBuilder(mdWithTitle));
        AkpProcessor.WriteMd(outputPathOfCurrentStory, rawMarkdown);
        if (IsLocalResChecked)
        {
            AkpProcessor.WriteHtmlWithLocalRes(outputPathOfCurrentStory, rawMarkdown);
        }
        else
            AkpProcessor.WriteHtml(outputPathOfCurrentStory, rawMarkdown);
    }

    private void CompleteLoading()
    {
        var result = MessageBox.Show(
            "生成完成。是否打开文件夹？",
            "markdown/html文件生成完成！",
            MessageBoxButton.OKCancel
        );
        if (result == MessageBoxResult.OK)
        {
            OpenOutputFolder();
        }
        IsInitialized = true;
    }

    private void OpenOutputFolder()
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                Arguments = outputPath,
                FileName = "explorer.exe",
                Verb = "runas",
            };
            Process.Start(startInfo);
        }
        catch (Win32Exception win32Exception)
        {
            MessageBox.Show(win32Exception.Message);
        }
    }

    public async Task<string> LoadSingleMd()
    {
        List<PlotManager> allPlots;
        var plotsJsonFile = new FileInfo(
            "C:\\TechnicalProjects\\ArkPlot\\ArkPlotWpf\\all_plots.json"
        );
        if (!plotsJsonFile.Exists)
        {
            var chapters = storySync.GetChaptersByActId(currentActs[0].Id);
            var content = new AkpStoryLoader(currentActs[0], chapters);
            await content.GetAllChapters();
            allPlots = content.ContentTable;
            var plotJson = JsonConvert.SerializeObject(allPlots, Formatting.Indented); // 使用Newtonsoft.Json进行序列化

            // 将序列化的JSON字符串写入文件
            await File.WriteAllTextAsync(plotsJsonFile.FullName, plotJson);
        }
        else
        {
            // 从文件中读取JSON字符串并反序列化
            var plotJson = await File.ReadAllTextAsync(plotsJsonFile.FullName);
            allPlots = JsonConvert.DeserializeObject<List<PlotManager>>(plotJson)!; // 使用Newtonsoft.Json进行反序列化
        }

        var testPlot = allPlots.First();
        var title = testPlot.CurrentPlot.Title;
        return title + "\n" + testPlot.CurrentPlot.Content;
    }

    private void ClearConsoleOutput()
    {
        ConsoleOutput = ""; //先清空这片区域
    }

    private async Task<string> ExportPlots(List<PlotManager> allPlots)
    {
        return await AkpProcessor.ExportPlotsAsync(allPlots);
    }

    [RelayCommand]
    private void OpenTagEditor()
    {
        var messenger = WeakReferenceMessenger.Default;
        messenger.Send(new OpenWindowMessage("TagEditor", jsonPath));
    }

    private void SubscribeCommonNotification()
    {
        noticeBlock.CommonEventHandler += (_, args) => ConsoleOutput += $"\r\n{args}";
    }

    private void SubscribeNetErrorNotification()
    {
        noticeBlock.NetErrorHappen += (_, args) =>
        {
            var s = $"\r\n网络错误：{args.Message}，请确认是否连接代理？";
            ConsoleOutput += s;
            MessageBox.Show(s);
        };
    }

    private void SubscribeLineNoMatchNotification()
    {
        noticeBlock.LineNoMatch += (_, args) =>
        {
            var s = $"\r\n警告：请检查tags.json中{args.Tag}是否存在？\r\n出错的句子：" + args.Line;
            ConsoleOutput += s;
        };
    }

    private void SubscribeChapterLoadedNotification()
    {
        noticeBlock.ChapterLoaded += (_, args) =>
        {
            var s = "\r\n" + args.Title.ToString() + "已加载";
            ConsoleOutput += s;
        };
    }

    public void SelectJsonFile(string path)
    {
        JsonPath = path;
    }

    public void SelectOutputFolder(string path)
    {
        OutputPath = path;
    }

    internal void DropJsonFile(string v)
    {
        JsonPath = v;
    }
}
