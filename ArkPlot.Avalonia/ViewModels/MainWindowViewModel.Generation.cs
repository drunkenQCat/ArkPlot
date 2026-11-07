using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ArkPlot.Core.Infrastructure;
using ArkPlot.Avalonia.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkPlot.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    public string[] GenerationTargets { get; } = ["剧情剧本", "AI 小说", "有声书"];
    [ObservableProperty] private int generationTarget;
    [ObservableProperty] private bool forceNovelGeneration;
    partial void OnForceNovelGenerationChanged(bool value) => RefreshPlan();
    [ObservableProperty] private string planSummary = "请选择活动和章节。";
    [ObservableProperty] private string configurationStatus = "开始前会检查所需配置。";
    [ObservableProperty] private bool configurationReady;
    [ObservableProperty] private string resultSummary = "";
    [ObservableProperty] private string lastNovelPath = "";
    private readonly System.Collections.Generic.List<string> _runWarnings = new();
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private bool canRetry;
    private string NovelOutcome { get; set; } = "未完成";
    private string EpubOutcome { get; set; } = "未生成";
    public bool CanOpenNovel => File.Exists(LastNovelPath);
    partial void OnLastNovelPathChanged(string value) => OnPropertyChanged(nameof(CanOpenNovel));
    private bool _scriptReady;
    private bool _novelReady;

    public string ChapterSelectionSummary => Chapters.Count == 0 ? "章节尚未加载 · 点击选择章节" :
        $"已选 {Chapters.Count(c => c.IsSelected)} / {Chapters.Count} 章";
    public string PlanOutputDirectory => SelectedIndex >= 0 && SelectedIndex < currentActs.Count ? Path.Combine(OutputPath, CurrentAct.Name) : OutputPath;
    public string StartButtonText => (GenerationTarget == 0 ? "导出剧本" : GenerationTarget == 1 ? "生成小说" : "生成小说并准备配音") + $" · {Chapters.Count(c => c.IsSelected)} 章";
    public bool HasChapters => Chapters.Count > 0;
    public bool HasSelectedChapters => Chapters.Any(c => c.IsSelected);
    public string CurrentLanguageName => language switch { "ja_JP" => "日本語", "en_US" => "English", "ko_KR" => "한국어", _ => "简体中文" };
    public string TargetDescription => GenerationTarget switch { 0 => "整理原始剧情，保留对白与场景。", 1 => "生成连贯的小说，同时保留原始剧本。", _ => "先生成小说，再确认声音、合成与导出。" };
    [ObservableProperty] private string modelDisplayName = "";
    [ObservableProperty] private string modelDetail = "";
    public string CompactFlow => "获取剧情 → 整理剧本" + (IsPicDescEnabled ? " → 画面描述" : "") + (IsNovelizerEnabled ? " → 小说 / EPUB" : " → 导出文件");
    public string CostNotice => IsNovelizerEnabled || IsPicDescEnabled ? "模型调用可能产生费用；剧情优先读取缓存，缺失时联网下载。" : "剧本整理在本地完成；缺失的剧情与资源需要联网下载。";

    [RelayCommand]
    private void SelectGenerationTarget(string target)
    {
        if (int.TryParse(target, out var value) && value is >= 0 and <= 2) GenerationTarget = value;
        HasResult = false;
    }

    [RelayCommand]
    private void ReturnToPlan() => HasResult = false;
    public bool IsAudiobookGoal => GenerationTarget == 2;
    public bool IsScriptGoal => GenerationTarget == 0;
    public bool IsNovelGoal => GenerationTarget == 1;
    public string TtsReadiness => CanOpenNovel ? "本次小说已就绪，可确认音色并生成音频。" : IsTtsReady ? "找到已有小说（可能来自历史任务），请在有声书窗口确认文件和章节。" : "需先生成 AI 小说，再确认音色、生成音频并导出 MP3。";
    public bool IsTtsReady => SelectedIndex >= 0 && SelectedIndex < currentActs.Count &&
        Directory.Exists(Path.Combine(OutputPath, CurrentAct.Name)) &&
        Directory.EnumerateFiles(Path.Combine(OutputPath, CurrentAct.Name), "*_novel_*.md").Any();

    partial void OnGenerationTargetChanged(int value)
    {
        IsNovelizerEnabled = value != 0;
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(IsAudiobookGoal));
        OnPropertyChanged(nameof(IsScriptGoal));
        OnPropertyChanged(nameof(IsNovelGoal));
        RefreshPlan();
    }
    partial void OnIsNovelizerEnabledChanged(bool value) => RefreshPlan();
    partial void OnIsLocalResCheckedChanged(bool value) => RefreshPlan();
    private void ChapterChanged(object? sender, PropertyChangedEventArgs e) => RefreshPlan();

    public void RefreshPlan()
    {
        OnPropertyChanged(nameof(ChapterSelectionSummary));
        OnPropertyChanged(nameof(HasChapters));
        OnPropertyChanged(nameof(HasSelectedChapters));
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(TargetDescription));
        OnPropertyChanged(nameof(CompactFlow));
        OnPropertyChanged(nameof(CostNotice));
        OnPropertyChanged(nameof(PlanOutputDirectory));
        OnPropertyChanged(nameof(IsTtsReady));
        OnPropertyChanged(nameof(TtsReadiness));
        var settings = AppSettings.Load();
        var story = SelectedIndex >= 0 && SelectedIndex < currentActs.Count ? CurrentAct.Name : "未选择活动";
        var novel = settings.Novelizer;
        ModelDisplayName = novel.SelectedProvider;
        ModelDetail = novel.SelectedModel + (novel.UseMock ? " · 模拟模式" : string.IsNullOrWhiteSpace(novel.GetApiKeyForProvider(novel.SelectedProvider)) ? " · 待配置 API Key" : " · 已填写，尚未验证连接");
        var vision = settings.Vision;
        PlanSummary = $"{story} · {ChapterSelectionSummary}\n" +
            $"产物：{(IsNovelizerEnabled ? "剧本 + 小说（Markdown / HTML），尝试导出 EPUB" : "剧本（Markdown / HTML）")}\n" +
            (IsNovelizerEnabled ? $"文本模型：{novel.SelectedProvider} / {novel.SelectedModel}{(novel.UseMock ? "（模拟，不调用 API）" : "（在线调用，可能计费）")}\n" : "小说化：未启用\n") +
            (IsPicDescEnabled ? $"图片描述：{vision?.SelectedProvider} / {vision?.SelectedModel}{(vision?.UseMockVision == true ? "（模拟）" : vision?.SelectedProvider == "Ollama" ? "（本地模型，图片仍可能联网下载）" : "（在线调用，可能计费）")}\n" : "图片描述：未启用\n") +
            "剧情优先读取缓存，缺失内容联网下载。\n" +
            (IsNovelizerEnabled ? ForceNovelGeneration ? "小说：忽略缓存，重新调用模型。\n" : "小说：相同剧本与模型复用缓存；改提示词后可在高级选项中强制重生成。\n" : "") +
            (IsAudiobookGoal ? "下一步：确认音色 → 生成音频 → 导出 MP3" : "");
    }

    [RelayCommand]
    private void CheckConfiguration()
    {
        RefreshPlan();
        var settings = AppSettings.Load();
        var issues = new System.Collections.Generic.List<string>();
        if (SelectedIndex < 0 || SelectedIndex >= currentActs.Count) issues.Add("请选择活动");
        if (Chapters.Count == 0 || !Chapters.Any(c => c.IsSelected)) issues.Add("请选择至少一章");
        if (!File.Exists(JsonPath)) issues.Add("标签规则文件不存在，请在高级选项中选择有效文件");
        var n = settings.Novelizer;
        if (IsNovelizerEnabled && !n.UseMock)
        {
            if (string.IsNullOrWhiteSpace(n.GetApiKeyForProvider(n.SelectedProvider))) issues.Add($"请配置 {n.SelectedProvider} API Key");
            if (string.IsNullOrWhiteSpace(n.SelectedModel)) issues.Add("请选择文本模型");
        }
        var v = settings.Vision ?? VisionSettings.CreateDefaults();
        if (IsPicDescEnabled && v != null && !v.UseMockVision)
        {
            if (string.IsNullOrWhiteSpace(v.SelectedModel)) issues.Add("请选择视觉模型");
            if (v.SelectedProvider != "Ollama" && string.IsNullOrWhiteSpace(settings.GetVisionApiKey(v.SelectedProvider)))
                issues.Add($"请配置图片描述的 {v.SelectedProvider} API Key");
        }
        ConfigurationReady = issues.Count == 0;
        ConfigurationStatus = ConfigurationReady ? "本地配置检查通过；服务可用性将在调用时确认。" : string.Join("；", issues);
    }

    private void FinishRun(string outcome)
    {
        HasResult = true;
        CanRetry = outcome != "全部成功";
        ResultSummary = $"{activeTitle} · {outcome} · {Chapters.Count(c => c.IsSelected)} 章\n剧本：{(_scriptReady ? "已生成" : "未完成")}\n" +
            $"小说：{(!IsNovelizerEnabled ? "未启用" : _novelReady ? NovelOutcome : "未完整生成")}\n" +
            (IsNovelizerEnabled ? $"EPUB：{EpubOutcome}\n" : "") +
            string.Join("\n", _runWarnings.Distinct().Take(5)) + (_runWarnings.Count > 5 ? "\n更多详情请展开日志。" : "") + $"\n目录：{outputPathOfCurrentStory}" +
            (IsAudiobookGoal ? "\n有声书尚未导出：继续确认音色、生成音频并导出 MP3。" : "");
        RefreshPlan();
    }

    [RelayCommand]
    private void OpenNovel()
    {
        if (File.Exists(LastNovelPath)) Process.Start(new ProcessStartInfo(LastNovelPath) { UseShellExecute = true });
    }
}
