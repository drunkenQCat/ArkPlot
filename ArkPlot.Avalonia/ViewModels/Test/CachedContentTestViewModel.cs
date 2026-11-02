using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkPlot.Avalonia.ViewModels.Test;

public partial class CachedContentTestViewModel : ViewModelBase, IDisposable
{
    private readonly ITestCacheSource _cache;
    private readonly ICachedModelTestRunner _runner;
    private CatalogModel[] _catalog = [];
    private CancellationTokenSource? _running;
    private readonly string _catalogPath = Path.Combine(AppContext.BaseDirectory, "bailian-model-catalog.json");
    public bool IsImageTest { get; }
    public string InputTitle => IsImageTest ? "缓存图片" : "缓存文本（可编辑）";
    public string RunLabel => IsImageTest ? "运行图片测试" : "运行文本测试";
    public string[] Providers { get; }
    [ObservableProperty] private ObservableCollection<CachedChoice> _activities = [];
    [ObservableProperty] private CachedChoice? _selectedActivity;
    [ObservableProperty] private ObservableCollection<CachedChoice> _chapters = [];
    [ObservableProperty] private CachedChoice? _selectedChapter;
    [ObservableProperty] private ObservableCollection<CachedImage> _images = [];
    [ObservableProperty] private CachedImage? _selectedImage;
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private string _systemPrompt = "";
    [ObservableProperty] private string _outputText = "";
    [ObservableProperty] private string _thinkingText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _selectedProvider = "";
    [ObservableProperty] private ModelChoice[] _models = [];
    [ObservableProperty] private ModelChoice? _selectedModel;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isRefreshingModels;
    [ObservableProperty] private int _maxTokens = 2048;
    [ObservableProperty] private int _inputCharacterLimit = 4000;
    [ObservableProperty] private bool _enableThinking;

    public CachedContentTestViewModel(bool imageTest, ITestCacheSource? cache = null, ICachedModelTestRunner? runner = null)
    {
        IsImageTest = imageTest;
        _cache = cache ?? new TestCacheSource();
        _runner = runner ?? new CachedModelTestRunner();
        var settings = AppSettings.Load();
        var vision = settings.Vision ?? VisionSettings.CreateDefaults();
        Providers = imageTest ? vision.AllProviderNames : settings.Novelizer.AllProviderNames;
        SystemPrompt = imageTest ? vision.SystemPrompt : settings.Novelizer.SystemPrompt;
        if (string.IsNullOrWhiteSpace(SystemPrompt)) SystemPrompt = imageTest ? VisionSettings.DefaultSystemPrompt : "将这段剧情改写为连贯的小说正文。";
        try { _catalog = JsonSerializer.Deserialize<CatalogModel[]>(File.ReadAllText(_catalogPath)) ?? []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        SelectedProvider = imageTest ? vision.SelectedProvider : settings.Novelizer.SelectedProvider;
        var selected = imageTest ? vision.SelectedModel : settings.Novelizer.SelectedModel;
        SelectedModel = Models.FirstOrDefault(m => m.Id == selected) ?? Models.FirstOrDefault();
        RefreshCache();
    }

    partial void OnSelectedProviderChanged(string value)
    {
        var settings = AppSettings.Load();
        var ids = value == "百炼" ? BailianModelCatalog.SelectModels(_catalog, IsImageTest)
            : IsImageTest ? (settings.Vision ?? VisionSettings.CreateDefaults()).GetModelsForProvider(value)
            : settings.Novelizer.GetModelsForProvider(value);
        var previous = SelectedModel?.Id;
        Models = IsImageTest ? ids.Select(id => new ModelChoice(id, id)).ToArray() : ModelChoice.ForNovel(ids);
        SelectedModel = Models.FirstOrDefault(m => m.Id == previous) ?? Models.FirstOrDefault();
    }

    [RelayCommand]
    private void RefreshCache()
    {
        try
        {
            var previous = SelectedActivity?.Id;
            Activities = new(_cache.GetActivities(IsImageTest));
            SelectedActivity = Activities.FirstOrDefault(a => a.Id == previous) ?? Activities.FirstOrDefault();
            if (SelectedActivity == null)
            {
                Chapters.Clear(); Images.Clear(); SelectedImage = null; InputText = "";
                StatusText = "暂无已解析的剧情缓存，请先在主界面加载剧情。";
            }
        }
        catch (Exception ex) { StatusText = $"读取缓存失败：{ex.Message}"; }
    }

    partial void OnSelectedActivityChanged(CachedChoice? value)
    {
        try
        {
            Chapters = new(value == null ? [] : _cache.GetChapters(value.Id));
            SelectedChapter = Chapters.FirstOrDefault();
        }
        catch (Exception ex) { StatusText = $"读取章节失败：{ex.Message}"; }
    }
    partial void OnSelectedChapterChanged(CachedChoice? value) => LoadSelectedContent();
    partial void OnSelectedImageChanged(CachedImage? value)
    {
        OnPropertyChanged(nameof(ImagePreview));
        OnPropertyChanged(nameof(CachedDescription));
        OnPropertyChanged(nameof(ImageSourceLabel));
    }
    public string? ImagePreview => SelectedImage?.PreviewSource;
    public string CachedDescription => SelectedImage?.Description ?? "";
    public string ImageSourceLabel => SelectedImage == null ? "" : string.IsNullOrEmpty(SelectedImage.LocalPath)
        ? "缓存图片引用；预览与测试可能从原地址加载图片。" : "已找到本地图片文件。";

    [RelayCommand]
    private void LoadSelectedContent()
    {
        try
        {
            if (SelectedChapter == null) { InputText = ""; Images.Clear(); SelectedImage = null; return; }
            if (IsImageTest)
            {
                Images = new(_cache.GetImages(SelectedChapter.Id));
                SelectedImage = Images.FirstOrDefault();
                StatusText = $"找到 {Images.Count} 张缓存图片。";
            }
            else
            {
                var text = _cache.GetText(SelectedChapter.Id);
                InputText = text[..Math.Min(text.Length, Math.Clamp(InputCharacterLimit, 100, 50000))];
                StatusText = $"已载入 {InputText.Length} 字符（全章 {text.Length} 字符），可编辑后测试。";
            }
        }
        catch (Exception ex) { StatusText = $"读取内容失败：{ex.Message}"; }
    }

    [RelayCommand]
    private void RandomSample()
    {
        if (IsImageTest && Images.Count > 0) { SelectedImage = Images[Random.Shared.Next(Images.Count)]; return; }
        if (Chapters.Count == 0) return;
        var next = Chapters[Random.Shared.Next(Chapters.Count)];
        if (next == SelectedChapter) LoadSelectedContent(); else SelectedChapter = next;
    }

    [RelayCommand]
    private async Task RefreshModels()
    {
        if (IsRefreshingModels) return;
        IsRefreshingModels = true;
        StatusText = "正在刷新百炼模型目录…";
        try
        {
            var key = AppSettings.Load().Novelizer.GetApiKeyForProvider("百炼");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            try { _catalog = await new BailianModelCatalog(http).FetchAsync(key); }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException { StatusCode: null })
            {
                using var direct = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(30) };
                _catalog = await new BailianModelCatalog(direct).FetchAsync(key);
            }
            OnSelectedProviderChanged(SelectedProvider);
            StatusText = $"模型目录已刷新，当前平台可选 {Models.Length} 个模型。";
        }
        catch (Exception ex) { StatusText = $"刷新失败：{ex.Message}，已保留原有列表。"; }
        finally { IsRefreshingModels = false; }
    }

    [RelayCommand]
    private async Task RunTest()
    {
        if (IsRunning) return;
        if (SelectedModel == null) { StatusText = "请先选择模型；百炼列表为空时可点击刷新模型。"; return; }
        if (IsImageTest ? SelectedImage == null : string.IsNullOrWhiteSpace(InputText))
        { StatusText = "请先选择缓存内容。"; return; }
        _running = new CancellationTokenSource();
        var token = _running.Token;
        IsRunning = true;
        OutputText = ""; ThinkingText = "";
        StatusText = "正在调用模型…";
        var timer = Stopwatch.StartNew();
        try
        {
            var result = await _runner.RunAsync(new(IsImageTest, SelectedProvider, SelectedModel.Id, SystemPrompt,
                InputText, SelectedImage, Math.Clamp(MaxTokens, 128, 32768), EnableThinking), token);
            token.ThrowIfCancellationRequested();
            OutputText = result.Text; ThinkingText = result.Thinking;
            StatusText = $"完成，耗时 {timer.Elapsed.TotalSeconds:F1} 秒。测试结果未写入缓存。";
        }
        catch (Exception) when (token.IsCancellationRequested) { StatusText = "测试已取消。"; }
        catch (Exception ex) { StatusText = $"测试失败：{ex.Message}"; }
        finally { IsRunning = false; _running.Dispose(); _running = null; }
    }

    [RelayCommand] private void CancelTest() => _running?.Cancel();
    public void Dispose() => _running?.Cancel();
}
