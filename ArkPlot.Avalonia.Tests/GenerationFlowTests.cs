using System.Reflection;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Avalonia.Views;
using ArkPlot.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(ArkPlot.Avalonia.Tests.GenerationTestApplication))]
namespace ArkPlot.Avalonia.Tests;

public static class GenerationTestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class GenerationFlowTests
{
    private static MainWindowViewModel CreatePlan()
    {
        var vm = new MainWindowViewModel();
        typeof(MainWindowViewModel).GetField("currentActs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, new List<Act> { new() { Name = "孤星", Lang = "zh_CN" } });
        vm.StoriesNames = ["孤星"];
        vm.SelectedIndex = 0;
        vm.Chapters.Add(new ChapterSelectionViewModel("CW-1 星空", true));
        vm.Chapters.Add(new ChapterSelectionViewModel("CW-2 前路", false));
        vm.IsInitialized = true;
        return vm;
    }

    [AvaloniaFact]
    public void GoalAndChapterSelection_UpdatePlanAndDependencies()
    {
        using var vm = CreatePlan();
        Assert.Equal("已选 1 / 2 章", vm.ChapterSelectionSummary);
        vm.Chapters[1].IsSelected = true;
        Assert.Equal("已选 2 / 2 章", vm.ChapterSelectionSummary);
        vm.GenerationTarget = 2;
        Assert.True(vm.IsNovelizerEnabled);
        Assert.Contains("确认音色", vm.PlanSummary);
        vm.GenerationTarget = 0;
        Assert.False(vm.IsNovelizerEnabled);
        Assert.Contains("小说化：未启用", vm.PlanSummary);
        vm.DeselectAllChaptersCommand.Execute(null);
        vm.CheckConfigurationCommand.Execute(null);
        Assert.False(vm.ConfigurationReady);
        Assert.Contains("请选择至少一章", vm.ConfigurationStatus);
    }

    [AvaloniaFact]
    public void Workflow_CancellationAndFailureCannotBecomeSuccess()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(["剧本", "小说"]);
        vm.EnterStage(0);
        vm.EnterStage(1);
        vm.CancelPipeline("用户取消");
        Assert.Equal(StageStatus.Done, vm.Stages[0].Status);
        Assert.Equal(StageStatus.Cancelled, vm.Stages[1].Status);
        Assert.False(vm.IsComplete);
        Assert.False(vm.IsRunning);
        vm.CompletePipeline();
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsComplete);
        Assert.Equal(StageStatus.Cancelled, vm.Stages[1].Status);
        vm.BeginPipeline(["剧本", "小说"]);
        vm.EnterStage(0);
        vm.FailStage("下载失败");
        Dispatcher.UIThread.RunJobs();
        vm.CompletePipeline();
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsComplete);
        Assert.Equal(StageStatus.Pending, vm.Stages[1].Status);
    }

    [AvaloniaFact]
    public async Task MainAndSettings_RenderWithGoalAndAdvancedSections()
    {
        using var vm = CreatePlan();
        vm.GenerationTarget = 1;
        SukiUI.SukiTheme.GetInstance().ChangeBaseTheme(global::Avalonia.Styling.ThemeVariant.Light);
        for (var i = 3; i <= 8; i++) vm.Chapters.Add(new ChapterSelectionViewModel($"CW-{i} 行动前", i < 5));
        var main = new MainWindow();
        (main.DataContext as IDisposable)?.Dispose();
        main.DataContext = vm;
        Interaction.GetBehaviors(main).Clear();
        main.Show();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.SystemIdle);
        Assert.Contains(main.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "输出内容");
        Assert.NotNull(main.FindControl<Border>("BannerImage")?.Background);
        Assert.Contains(main.GetVisualDescendants().OfType<Expander>(), e => e.Header?.ToString() == "输出选项" && !e.IsExpanded);
        await Task.Delay(500);
        Capture(main, "01-generation-plan.png");
        SukiUI.SukiTheme.GetInstance().ChangeBaseTheme(global::Avalonia.Styling.ThemeVariant.Dark);
        await Task.Delay(600);
        Capture(main, "01-generation-plan-dark.png");
        SukiUI.SukiTheme.GetInstance().ChangeBaseTheme(global::Avalonia.Styling.ThemeVariant.Light);
        main.Close();

        var settings = new SettingsWindow();
        Interaction.GetBehaviors(settings).Clear();
        var settingsVm = new SettingsViewModel(Path.Combine(AppContext.BaseDirectory, "tags.json"));
        settingsVm.ModelOptions = ["deepseek-v4-pro", "deepseek-v4-flash"];
        settingsVm.SelectedModel = "deepseek-v4-pro";
        settingsVm.EnableMultiTurn = true;
        settings.DataContext = settingsVm;
        settings.Show();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.SystemIdle);
        Assert.Equal(1, settingsVm.SelectedTabIndex);
        Assert.Contains(settings.GetVisualDescendants().OfType<Expander>(), e => e.Header?.ToString() == "提示词与上下文参数" && !e.IsExpanded);
        await Task.Delay(600);
        Capture(settings, "02-model-settings.png");
        foreach (var index in new[] { 2, 3, 4, 0 })
        {
            settingsVm.SelectedTabIndex = index;
            Dispatcher.UIThread.RunJobs(DispatcherPriority.SystemIdle);
            await Task.Delay(300);
            Capture(settings, $"02-settings-section-{index}.png");
        }
        settingsVm.SelectedTabIndex = 1;
        SukiUI.SukiTheme.GetInstance().ChangeBaseTheme(global::Avalonia.Styling.ThemeVariant.Dark);
        await Task.Delay(600);
        Capture(settings, "02-model-settings-dark.png");
        SukiUI.SukiTheme.GetInstance().ChangeBaseTheme(global::Avalonia.Styling.ThemeVariant.Light);
        settings.Close();
    }

    [AvaloniaFact]
    public async Task Logs_AllStagesAndErrorsRemainInspectableAfterCompletion()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(["读取剧情", "整理剧本", "生成小说", "导出文件"]);
        vm.EnterStage(0); vm.Append(LogLevel.Success, "所选章节已准备好，命中本地缓存。");
        vm.EnterStage(1); vm.Append(LogLevel.Success, "原始剧本 Markdown / HTML 已保存。");
        vm.EnterStage(2); vm.Append(LogLevel.Success, "CW-1 行动前 · 小说已保存。");
        vm.Append(LogLevel.Error, "CW-2 行动后 · 模型请求超时。", detail: "服务未在等待时间内返回。已完成章节保留，重试会复用成功缓存。");
        Dispatcher.UIThread.RunJobs();
        vm.EnterStage(3); vm.Append(LogLevel.Success, "已导出成功章节的 EPUB；不包含失败章节。");
        vm.CompletePipeline(); Dispatcher.UIThread.RunJobs();
        vm.ShowAllLogsCommand.Execute(null);
        Assert.Equal(5, vm.VisibleLogs.Count);
        Assert.True(vm.HasErrors);
        var window = new WorkflowLogWindow { DataContext = vm };
        window.Show(); Dispatcher.UIThread.RunJobs(); await Task.Delay(600);
        Capture(window, "04-workflow-logs.png");
        vm.LocateAllErrorsCommand.Execute(null);
        Assert.True(Assert.Single(vm.VisibleLogs).IsError);
        Assert.False(vm.FollowLatest);
        SukiUI.SukiTheme.GetInstance().ChangeBaseTheme(global::Avalonia.Styling.ThemeVariant.Dark);
        await Task.Delay(600);
        Capture(window, "04-workflow-logs-dark.png");
        SukiUI.SukiTheme.GetInstance().ChangeBaseTheme(global::Avalonia.Styling.ThemeVariant.Light);
        vm.ClearLogsCommand.Execute(null);
        Assert.False(vm.HasErrors);
        Assert.Empty(vm.VisibleLogs);
        window.Close();
    }

    [AvaloniaFact]
    public void LogInspection_StaysOnChosenStageUntilFollowingResumes()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(["下载", "解析", "导出"]);
        vm.EnterStage(0);
        vm.SelectStage(vm.Stages[0]);
        vm.EnterStage(1);
        Assert.Same(vm.Stages[0], vm.SelectedStage);
        vm.FollowLatest = true;
        Assert.Same(vm.Stages[1], vm.SelectedStage);
        vm.EnterStage(2);
        Assert.Same(vm.Stages[2], vm.SelectedStage);
    }

    [AvaloniaFact]
    public void VisionCredentialSave_PreservesOtherProviderCredentials()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        var backup = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
        try
        {
            var original = new AppSettings(NovelizerSettings.CreateDefaults());
            (original with { Novelizer = original.Novelizer with { ApiKeys = new() { ["DeepSeek"] = "test-retain", ["百炼"] = "test-old" } } }).Save();
            var vm = new SettingsViewModel { VisionSelectedModel = "test-vision", BailianApiKeyText = "test-new" };
            vm.SaveVisionSettingsCommand.Execute(null);
            var saved = AppSettings.Load();
            Assert.Equal("test-retain", saved.Novelizer.ApiKeys["DeepSeek"]);
            Assert.Equal("test-new", saved.Novelizer.ApiKeys["百炼"]);
            Assert.Equal("test-vision", saved.Vision!.SelectedModel);
        }
        finally
        {
            if (backup != null) File.WriteAllText(settingsPath, backup);
            else File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task MissingModelCredential_PreventsStartingThePipeline()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        var backup = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
        var tag = Path.GetTempFileName();
        try
        {
            var settings = new AppSettings(NovelizerSettings.CreateDefaults());
            (settings with { Novelizer = settings.Novelizer with { SelectedProvider = "Unconfigured", SelectedModel = "test" } }).Save();
            using var vm = CreatePlan();
            vm.JsonPath = tag;
            vm.GenerationTarget = 1;
            await vm.LoadMdCommand.ExecuteAsync(null);
            Assert.False(vm.ConfigurationReady);
            Assert.Contains("API Key", vm.ConfigurationStatus);
            Assert.Empty(vm.WorkflowLog.Stages);
            Assert.False(vm.HasResult);
        }
        finally
        {
            File.Delete(tag);
            if (backup != null) File.WriteAllText(settingsPath, backup);
            else File.Delete(settingsPath);
        }
    }

    [AvaloniaFact]
    public async Task AudioExport_RejectsMissingSegmentsThenMergesEverySegmentInOrder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arkplot-audio-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var vm = new TtsViewModel("测试活动", storyOutputDir: dir);
        vm.TtsOutputDir = Path.Combine(dir, "tts");
        var entries = new List<ArkPlot.Tts.Alignment.AlignmentEntry>
        {
            new("第一句。", false, null, null, 0, "第一章"),
            new("第二句。", false, null, null, 1, "第一章")
        };
        typeof(TtsViewModel).GetField("_alignmentEntries", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, entries);
        vm.Chapters.Add(new ChapterItem("第一章", 0));
        try
        {
            await vm.ExportAllChaptersCommand.ExecuteAsync(null);
            Assert.Contains("缺少 2 段", vm.AudiobookStatus);
            Assert.False(File.Exists(Path.Combine(dir, "tts_export", "第一章.mp3")));
            var cache = Path.Combine(vm.TtsOutputDir, "_tts_cache");
            Directory.CreateDirectory(cache);
            var tone = Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.mp3");
            foreach (var entry in entries)
            {
                var voice = vm.VoiceConfigPanel.ResolveVoiceSelection("(旁白)", null, false);
                var key = ArkPlot.Tts.TtsCacheService.GetCacheKey(ArkPlot.Tts.TextSanitizer.Sanitize(entry.NovelText), voice);
                File.Copy(tone, Path.Combine(cache, key + ".mp3"));
            }
            await vm.ExportAllChaptersCommand.ExecuteAsync(null);
            var exported = Path.Combine(dir, "tts_export", "第一章.mp3");
            Assert.True(File.Exists(exported));
            Assert.Equal(2 * ArkPlot.Tts.AudioMerger.CountFrames(tone), ArkPlot.Tts.AudioMerger.CountFrames(exported));
            Assert.Contains("1/1 章完整 MP3", vm.AudiobookStatus);
            var window = new TtsWindow();
            window.Show(); // no data context: do not initialize network/data-backed alignment in a rendering test
            window.DataContext = vm;
            Dispatcher.UIThread.RunJobs(DispatcherPriority.SystemIdle);
            await Task.Delay(500);
            Capture(window, "03-audiobook-export.png");
            window.DataContext = null;
            window.Close();
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void AudiobookHandoff_SelectsThisRunsNovelAndKeepsItsOutputDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arkplot-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var oldFile = Path.Combine(dir, "story_novel_old.md");
            var selectedFile = Path.Combine(dir, "story_novel_new.md");
            File.WriteAllText(oldFile, "## 旧章\n旧内容");
            File.WriteAllText(selectedFile, "## 新章\n新内容");
            using var vm = new TtsViewModel("测试活动", ["新章"], selectedFile, dir);
            Assert.Equal(selectedFile, Assert.Single(vm.NovelFiles, f => f.IsSelected).FilePath);
            Assert.Equal(Path.Combine(dir, "tts"), vm.TtsOutputDir);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void Capture(Window window, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var output = Path.Combine(AppContext.BaseDirectory, "ui-review");
        Directory.CreateDirectory(output);
        frame.Save(Path.Combine(output, name));
    }
}
