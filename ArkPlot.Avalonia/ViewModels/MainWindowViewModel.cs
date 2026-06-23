using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.Services;
using ArkPlot.Core.Model;
using ArkPlot.Core.Services;
using ArkPlot.Core.Utilities; // Added for AkpProcessor
using ArkPlot.Core.Utilities.PrtsComponents;
using ArkPlot.Core.Utilities.TagProcessingComponents;
using ArkPlot.Core.Utilities.WorkFlow;
using ArkPlot.Core.Utilities.WorkFlow.StoryDocument;
using ArkPlot.Novelizer;
using ArkPlot.Vision;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Newtonsoft.Json;
using SukiUI.Toasts;

// ReSharper disable InconsistentNaming

namespace ArkPlot.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly StorySyncService storySync = new();

    private readonly NotificationBlock noticeBlock = NotificationBlock.Instance;
    private readonly PrtsDataProcessor prts = new();

    [ObservableProperty]
    private ISukiToastManager toastManager = new SukiToastManager(); // public, 只读属性

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
    private bool isNovelizerEnabled;

    [ObservableProperty]
    private bool isPicDescEnabled;

    /// <summary>
    /// DeepSeek 官方 API Key（从环境变量 DEEPSEEK_API_KEY 读取）
    /// </summary>
    public string DeepSeekApiKey { get; private set; } = "";

    /// <summary>
    /// 百炼平台 API Key（从环境变量 DASHSCOPE_API_KEY 读取）
    /// </summary>
    public string BailianApiKey { get; private set; } = "";

    [ObservableProperty]
    private string jsonPath = Path.Combine(AppContext.BaseDirectory, "tags.json");

    private string language = "zh_CN";

    [ObservableProperty]
    private string outputPath = Path.Combine(AppContext.BaseDirectory, "output");

    private string outputPathOfCurrentStory => Path.Combine(OutputPath, activeTitle!);

    [ObservableProperty]
    private int selectedIndex;

    [ObservableProperty]
    private System.Collections.Generic.IEnumerable<string>? storiesNames; // Changed ICollectionView to IEnumerable and removed CollectionViewSource.GetDefaultView

    [ObservableProperty]
    private string status = "准备中...";

    private string storyType = "ACTIVITY_STORY";
    private string? activeTitle;

    private Act CurrentAct => currentActs[SelectedIndex];

    [ObservableProperty]
    private ObservableCollection<ChapterSelectionViewModel> _chapters = new();

    // 当用户切换主活动时，需要清空旧的章节列表，以便下次点击按钮时重新加载
    partial void OnSelectedIndexChanged(int value)
    {
        Chapters.Clear();
    }

    // 当图片描述开关变化时，保存到 settings.json（保留其他 Vision 配置）
    partial void OnIsPicDescEnabledChanged(bool value)
    {
        var settings = AppSettings.Load();
        var vision = (settings.Vision ?? VisionSettings.CreateDefaults()) with { IsPicDescEnabled = value };
        settings = settings with { Vision = vision };
        settings.Save();
    }

    [RelayCommand]
    private async Task LoadChapters()
    {
        // 如果当前活动没有章节，则加载它们
        if (CurrentAct != null && Chapters.Count == 0)
        {
            await LoadChaptersForCurrentAct();
        }
    }

    private async Task LoadChaptersForCurrentAct()
    {
        Chapters.Clear();
        Status = "正在加载章节列表...";

        var chapters = storySync.GetChaptersByActId(CurrentAct.Id);
        var storyLoader = new AkpStoryLoader(CurrentAct, chapters);
        var chapterNames = await storyLoader.GetChapterNamesAsync();

        foreach (var name in chapterNames)
        {
            Chapters.Add(new ChapterSelectionViewModel(name));
        }
        Status = "章节列表加载完成。";
    }

    [RelayCommand]
    private void SelectAllChapters()
    {
        foreach (var chapter in Chapters)
        {
            chapter.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectAllChapters()
    {
        foreach (var chapter in Chapters)
        {
            chapter.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task LoadInitResource()
    {
        // 初始化 API Key：从 AppSettings 读取（settings.json 优先 → 环境变量备选）
        var settings = AppSettings.Load();
        DeepSeekApiKey = settings.GetApiKey("DeepSeek");
        BailianApiKey = settings.GetApiKey("百炼");

        // 根据 API Key 可用性初始化小说化开关
        IsNovelizerEnabled = !string.IsNullOrEmpty(DeepSeekApiKey) || !string.IsNullOrEmpty(BailianApiKey);

        // 加载图片描述开关
        IsPicDescEnabled = settings.Vision?.IsPicDescEnabled ?? false;

        SubscribeAll();
        await Task.Yield();
        Status = $"正在加载Prts资源索引...";
        ToastManager
            .CreateToast()
            .WithTitle("初始化中")
            .WithContent(Status)
            .WithLoadingState(true)
            .Dismiss()
            .After(TimeSpan.FromSeconds(7))
            .Queue();

        var sw = Stopwatch.StartNew();
        await LoadResourceTable();
        sw.Stop();
        Status = $"Prts资源索引加载完成，耗时：{sw.ElapsedMilliseconds / 1000} s";
        ToastManager
            .CreateToast()
            .WithTitle("初始化中")
            .OfType(NotificationType.Success)
            .WithContent(Status)
            .Dismiss()
            .After(TimeSpan.FromSeconds(1))
            .Queue();

        Status = $"正在加载活动列表...";
        ToastManager
            .CreateToast()
            .WithTitle("初始化中")
            .WithContent(Status)
            .WithLoadingState(true)
            .Dismiss()
            .After(TimeSpan.FromSeconds(2))
            .Queue();
        await LoadLangTable(language);
        Status = $"初始化已完成";
        ToastManager
            .CreateToast()
            .WithTitle("初始化中")
            .OfType(NotificationType.Success)
            .WithContent(Status)
            .Dismiss()
            .After(TimeSpan.FromSeconds(3))
            .Queue();
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
            noticeBlock.RaiseCommonEvent("【prts资源索引文件加载完成】\n");
        }
        catch (Exception)
        {
            var s = "\n网络错误，无法加载资源文件。\n";
            noticeBlock.RaiseCommonEvent(s);
            // Removed MessageBox.Show(s);
            MessageBoxManager.GetMessageBoxStandard(
                title: "网络异常",
                text: s,
                @enum: ButtonEnum.Ok,
                icon: Icon.Error
            );
        }
    }

    [RelayCommand]
    private async Task LoadLangTable(string lang)
    {
        try
        {
            language = lang;
            await SyncActsAsync(lang);
            noticeBlock.RaiseCommonEvent("【剧情索引文件加载完成】\n");
            LoadActs(storyType);
        }
        catch (Exception)
        {
            var s = "\n索引文件加载出错！请检查网络代理。\n";
            noticeBlock.RaiseCommonEvent(s);
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
            // GitHub API 失败时尝试从 DB 读取已有数据
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
        StoriesNames = currentActs.Select(a => a.Name);
        SelectedIndex = 0;
    }

    [RelayCommand]
    private async Task LoadMd()
    {
        if (Chapters.Count == 0)
        {
            await LoadChapters();
        }
        var selectedChapters = Chapters
            .Where(c => c.IsSelected)
            .Select(c => c.ChapterName)
            .ToList();
        if (!selectedChapters.Any())
        {
            ToastManager
                .CreateToast()
                .OfType(NotificationType.Information)
                .WithTitle("选择出错")
                .WithContent("您没有选择任何章节")
                .Dismiss()
                .After(TimeSpan.FromSeconds(7))
                .Queue();
            return;
        }

        PrepareLoading();
        activeTitle = CurrentAct.Name;
        var chapters = storySync.GetChaptersByActId(CurrentAct.Id);

        var content = new AkpStoryLoader(CurrentAct, chapters);

        try
        {
            // GetAllChapters 内部自动处理缓存：
            // - Status=2 章节从 DB 加载
            // - 未缓存章节从 GitHub 下载并写 Status=1
            await content.GetAllChapters(selectedChapters);
            noticeBlock.RaiseCommonEvent("章节加载完成。");

            await PreloadResources(content);
            // StartParseDocuments → PlotManager.StartParseLines 自动将解析结果写为 Status=2
            await StartParseDocuments(content);

            await ExportDocuments(content);
            await RunNovelizerIfEnabled();
            await CompleteLoading();
        }
        finally
        {
            IsInitialized = true;
        }
    }

    private void PrepareLoading()
    {
        IsInitialized = false;
        ClearConsoleOutput();
        noticeBlock.RaiseCommonEvent("初始化加载...");
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
        await content.ParseAllDocuments(JsonPath);
    }

    private async Task ExportDocuments(AkpStoryLoader contentLoader)
    {
        noticeBlock.RaiseCommonEvent("正在导出文档....");

        PicDescService? picDescService = null;
        IDisposable? visionDisposable = null;

        if (IsPicDescEnabled)
        {
            try
            {
                var settings = AppSettings.Load();
                var vision = settings.Vision ?? VisionSettings.CreateDefaults();
                var providerName = vision.SelectedProvider;
                var model = vision.SelectedModel;
                var systemPrompt = string.IsNullOrEmpty(vision.SystemPrompt)
                    ? VisionSettings.DefaultSystemPrompt : vision.SystemPrompt;

                var log = (string msg) =>
                {
                    Dispatcher.UIThread.InvokeAsync(() => noticeBlock.RaiseCommonEvent(msg));
                };

                Func<string, Task<string>> describeByUrl;

                if (providerName == "Ollama")
                {
                    var visionConfig = new VisionConfig
                    {
                        BaseUrl = vision.OllamaBaseUrl,
                        Model = model,
                        SystemPrompt = systemPrompt,
                        TimeoutSeconds = 120,
                        MaxTokens = 2048
                    };
                    var ollamaClient = new OllamaVisionClient(visionConfig, onLog: log);
                    visionDisposable = ollamaClient;
                    // Ollama 不支持 URL 直传，需要下载后转 base64
                    describeByUrl = async url =>
                    {
                        using var http = new HttpClient();
                        var bytes = await http.GetByteArrayAsync(url);
                        var base64 = Convert.ToBase64String(bytes);
                        return await ollamaClient.DescribeImageBase64Async(base64);
                    };
                }
                else
                {
                    var apiKey = vision.GetApiKeyForProvider(providerName);
                    if (string.IsNullOrEmpty(apiKey))
                        apiKey = settings.GetApiKey(providerName);
                    var baseUrl = vision.GetBaseUrlForProvider(providerName);

                    if (string.IsNullOrEmpty(apiKey))
                    {
                        noticeBlock.RaiseCommonEvent($"⚠️ 图片描述已开启但未配置 {providerName} API Key，跳过。");
                        goto skipVision;
                    }

                    var visionConfig = new BailianVisionConfig
                    {
                        ApiKey = apiKey,
                        BaseUrl = baseUrl,
                        Model = model,
                        SystemPrompt = systemPrompt,
                        TimeoutSeconds = 120,
                        MaxTokens = 2048
                    };
                    var bailianClient = new BailianVisionClient(visionConfig, onLog: log);
                    visionDisposable = bailianClient;
                    describeByUrl = async url => await bailianClient.DescribeImageUrlAsync(url);
                }

                // 如果同时开启小说化，创建 YAML 提取委托（复用 Novelizer 模型）
                Func<string, Task<string>>? extractFacts = null;
                if (IsNovelizerEnabled)
                {
                    var novelizerSettings = AppSettings.Load().Novelizer;
                    var nProviderName = novelizerSettings.SelectedProvider;
                    var nApiKey = novelizerSettings.GetApiKeyForProvider(nProviderName);
                    var nBaseUrl = novelizerSettings.GetBaseUrlForProvider(nProviderName);
                    var nProvider = nProviderName switch
                    {
                        "DeepSeek" => ApiProvider.DeepSeek,
                        "百炼" => ApiProvider.Bailian,
                        _ => ApiProvider.Custom
                    };
                    if (!string.IsNullOrEmpty(nApiKey))
                    {
                        var nConfig = new ApiConfig { Provider = nProvider, ApiKey = nApiKey, BaseUrl = nBaseUrl };
                        var nHttp = new HttpClient();
                        var nClient = new BailianClient(nHttp, nConfig);
                        extractFacts = async prose =>
                        {
                            var result = await nClient.ChatAsync(
                                novelizerSettings.SelectedModel,
                                PicDescService.YamlExtractionPrompt,
                                prose);
                            return result.AnswerContent;
                        };
                    }
                }

                picDescService = new PicDescService(describeByUrl, extractFacts);
                picDescService.InitializeCleanup();
                noticeBlock.RaiseCommonEvent($"✅ 图片描述已启用（{providerName} {model}）");

            skipVision:;
            }
            catch (Exception ex)
            {
                noticeBlock.RaiseCommonEvent($"⚠️ 图片描述初始化失败：{ex.Message}");
            }
        }

        try
        {
            var outputMode = (IsNovelizerEnabled && IsPicDescEnabled)
                ? OutputMode.PromptOptimized
                : OutputMode.Readable;
            var rawMd = await ExportPlots(contentLoader.ContentTable, picDescService, outputMode);
            var rawMdWithTitle = "# " + (activeTitle ?? "") + "\n\n" + rawMd;
            ExportMdAndHtmlFiles(rawMdWithTitle);
            if (IsLocalResChecked)
            {
                AkpProcessor.WriteTyp(outputPathOfCurrentStory, contentLoader);
            }
        }
        finally
        {
            picDescService?.Dispose();
            visionDisposable?.Dispose();
        }
    }

    private void ExportMdAndHtmlFiles(string mdWithTitle)
    {
        if (!Directory.Exists(outputPathOfCurrentStory))
            Directory.CreateDirectory(outputPathOfCurrentStory);
        var rawMarkdown = new Plot(activeTitle ?? "", new System.Text.StringBuilder(mdWithTitle));
        AkpProcessor.WriteMd(outputPathOfCurrentStory, rawMarkdown);
        if (IsLocalResChecked)
        {
            AkpProcessor.WriteHtmlWithLocalRes(outputPathOfCurrentStory, rawMarkdown);
        }
        else
            AkpProcessor.WriteHtml(outputPathOfCurrentStory, rawMarkdown);
    }

    [RelayCommand]
    private async Task PickTagFile()
    {
        var storageProvider = GlobalStorageProvider.StorageProvider;
        var resultFile = await storageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions()
            {
                Title = "选取json文件",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("tag 文件") { Patterns = new[] { "*.json" } },
                },
            }
        );
        if (resultFile is null || resultFile.FirstOrDefault() is null)
            return;
        else
            JsonPath = resultFile.FirstOrDefault()!.Path.LocalPath;
    }

    [RelayCommand]
    private async Task PickOutputFolder()
    {
        var storageProvider = GlobalStorageProvider.StorageProvider;
        var resultFolder = await storageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions() { Title = "选择输出文件夹" }
        );
        if (resultFolder is null || resultFolder.FirstOrDefault() is null)
            return;
        else
            OutputPath = resultFolder.FirstOrDefault()!.Path.LocalPath;
    }

    private async Task RunNovelizerIfEnabled()
    {
        LogDiag("[RunNovelizer] 入口。IsNovelizerEnabled={0}", IsNovelizerEnabled);

        if (!IsNovelizerEnabled)
        {
            LogDiag("[RunNovelizer] IsNovelizerEnabled=false，直接返回");
            return;
        }

        // 从 AppSettings 读取小说化配置
        var settings = AppSettings.Load();
        var novelizer = settings.Novelizer;

        var selectedProviderName = novelizer.SelectedProvider;
        var apiKey = novelizer.GetApiKeyForProvider(selectedProviderName);
        var baseUrl = novelizer.GetBaseUrlForProvider(selectedProviderName);
        var provider = selectedProviderName switch
        {
            "DeepSeek" => ApiProvider.DeepSeek,
            "百炼" => ApiProvider.Bailian,
            _ => ApiProvider.Custom
        };
        LogDiag("[RunNovelizer] provider={0}, baseUrl={1}, apiKey长度={2}", selectedProviderName, baseUrl, apiKey.Length);

        if (string.IsNullOrEmpty(apiKey))
        {
            noticeBlock.RaiseCommonEvent($"❌ 未配置 {selectedProviderName} API Key，跳过小说生成。");
            LogDiag("[RunNovelizer] apiKey 为空，返回");
            return;
        }

        var model = novelizer.SelectedModel;
        var systemPrompt = novelizer.SystemPrompt;
        LogDiag("[RunNovelizer] model={0}，outputDir={1}", model, outputPathOfCurrentStory);
        noticeBlock.RaiseCommonEvent($"正在使用 {model} 生成小说...");

        try
        {
            LogDiag("[RunNovelizer] 开始创建 BailianClient + NovelizerPipeline");
            var config = new ApiConfig { Provider = provider, ApiKey = apiKey, BaseUrl = baseUrl };
            using var http = new HttpClient();
            var log = (string msg) =>
            {
                global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => noticeBlock.RaiseCommonEvent(msg));
            };
            var client = new BailianClient(http, config, onLog: log);
            var pipeline = new NovelizerPipeline(
                client, config, onLog: log,
                systemPrompt: systemPrompt,
                enableMultiTurn: novelizer.EnableMultiTurn,
                chunkSize: novelizer.ChunkSize,
                compressInterval: novelizer.CompressInterval);
            LogDiag("[RunNovelizer] 对象创建完成，即将调用 BatchProcessAsync");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await pipeline.BatchProcessAsync(outputPathOfCurrentStory, [model], force: false);
            sw.Stop();
            LogDiag("[RunNovelizer] BatchProcessAsync 返回，耗时 {0}s", sw.Elapsed.TotalSeconds);

            noticeBlock.RaiseCommonEvent($"✅ 小说生成完成，已保存至 {outputPathOfCurrentStory}");

            // 将小说 MD 也转换为 HTML（epub 已在 NovelizerPipeline 内部生成）
            try
            {
                var novelMdFiles = Directory.GetFiles(outputPathOfCurrentStory, "*_novel_*.md");
                LogDiag("[RunNovelizer] 找到 {0} 个小说 MD，开始转 HTML", novelMdFiles.Length);
                foreach (var mdPath in novelMdFiles)
                {
                    var novelTitle = Path.GetFileNameWithoutExtension(mdPath);
                    var novelContent = File.ReadAllText(mdPath);
                    var novelPlot = new Plot(novelTitle, new StringBuilder(novelContent));

                    if (IsLocalResChecked)
                        AkpProcessor.WriteHtmlWithLocalRes(outputPathOfCurrentStory, novelPlot);
                    else
                        AkpProcessor.WriteHtml(outputPathOfCurrentStory, novelPlot);

                    LogDiag("[RunNovelizer] HTML 已生成: {0}.html", novelTitle);
                    noticeBlock.RaiseCommonEvent($"📄 小说HTML已生成: {novelTitle}.html");
                }
            }
            catch (Exception ex)
            {
                LogDiag("[RunNovelizer] HTML 生成过程异常: {0}", ex.Message);
                noticeBlock.RaiseCommonEvent($"⚠️ 小说HTML生成失败: {ex.Message}");
            }
        }
        catch (BailianException ex)
        {
            LogDiag("[RunNovelizer] 捕获 BailianException: {0}", ex.Message);
            noticeBlock.RaiseCommonEvent($"❌ 小说生成失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            LogDiag("[RunNovelizer] 捕获 Exception({1}): {0}", ex.Message, ex.GetType().Name);
            noticeBlock.RaiseCommonEvent($"❌ 小说生成出错: {ex.Message}");
        }

        LogDiag("[RunNovelizer] 执行完毕，即将返回到 LoadMd");
    }

    // 纯诊断日志，加 [DIAG] 标记，仅用于排查问题
    private void LogDiag(string format, params object?[] args)
    {
        var msg = "[DIAG] " + string.Format(format, args);
        noticeBlock.RaiseCommonEvent(msg);
    }

    private async Task CompleteLoading()
    {
        var messageBox = MessageBoxManager.GetMessageBoxStandard(
            title: "提示",
            text: "生成完成。是否打开文件夹？",
            @enum: ButtonEnum.OkCancel,
            icon: Icon.Info
        );

        // 2. 以 Popup 形式展示，并等待用户点击结果
        var result = await messageBox.ShowAsync();

        // 3. 根据返回值执行后续逻辑
        if (result == ButtonResult.Ok)
        {
            OpenOutputFolder();
        }
    }

    private void OpenOutputFolder()
    {
        try
        {
            string command = string.Empty;
            string arguments = OutputPath;

            // 检查当前操作系统并选择适当的命令
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                command = "explorer.exe";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                command = "open";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                command = "xdg-open";
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported platform.");
            }

            // 执行打开文件夹命令
            ProcessStartInfo startInfo = new() { FileName = command, Arguments = arguments };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            noticeBlock.RaiseCommonEvent(ex.Message); // 处理异常
        }
    }

    public async Task<string> LoadSingleMd()
    {
        List<PlotManager> allPlots;
        var plotsJsonFile = new FileInfo(Path.Combine(AppContext.BaseDirectory, "all_plots.json")); // Changed hardcoded path
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

    private async Task<string> ExportPlots(
        List<PlotManager> allPlots,
        PicDescService? picDescService = null,
        OutputMode outputMode = OutputMode.Readable)
    {
        return await AkpProcessor.ExportPlotsAsync(allPlots, picDescService, outputMode: outputMode);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var messenger = WeakReferenceMessenger.Default;
        messenger.Send(new OpenWindowMessage("SettingsWindow", JsonPath));
    }

    [RelayCommand]
    private void OpenTts()
    {
        // 优先用当前选中的活动名，回退到上次 LoadMd 设置的 activeTitle
        var actName = CurrentAct?.Name ?? activeTitle;

        if (string.IsNullOrEmpty(actName))
        {
            ToastManager
                .CreateToast()
                .OfType(NotificationType.Warning)
                .WithTitle("TTS 语音生成")
                .WithContent("请先选择一个活动并生成内容")
                .Dismiss()
                .After(TimeSpan.FromSeconds(3))
                .Queue();
            return;
        }

        var storyOutputDir = Path.Combine(OutputPath, actName);

        // 检测输出目录是否有小说化缓存
        var hasNovelCache = Directory.Exists(storyOutputDir)
            && Directory.GetFiles(storyOutputDir, "*_novel_*.md").Length > 0;

        if (!hasNovelCache)
        {
            ToastManager
                .CreateToast()
                .OfType(NotificationType.Warning)
                .WithTitle("TTS 语音生成")
                .WithContent($"输出目录中没有找到小说化缓存，请先生成内容并启用小说化")
                .Dismiss()
                .After(TimeSpan.FromSeconds(5))
                .Queue();
            return;
        }

        var messenger = WeakReferenceMessenger.Default;
        messenger.Send(new OpenWindowMessage("TtsWindow", storyOutputDir));
    }

    private void SubscribeCommonNotification()
    {
        noticeBlock.CommonEventHandler += (_, args) => ConsoleOutput += $"\n{args}";
    }

    private void SubscribeNetErrorNotification()
    {
        noticeBlock.NetErrorHappen += (_, args) =>
        {
            var s = $"\n网络错误：{args.Message}，请确认是否连接代理？";
            ConsoleOutput += s;
            // Removed MessageBox.Show(s);
        };
    }

    private void SubscribeLineNoMatchNotification()
    {
        noticeBlock.LineNoMatch += (_, args) =>
        {
            var s = $"\n警告：请检查tags.json中{args.Tag}是否存在？\n出错的句子:" + args.Line;
            ConsoleOutput += s;
        };
    }

    private void SubscribeChapterLoadedNotification()
    {
        noticeBlock.ChapterLoaded += (_, args) =>
        {
            var s = "\n" + args.Title.ToString() + "已加载";
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
