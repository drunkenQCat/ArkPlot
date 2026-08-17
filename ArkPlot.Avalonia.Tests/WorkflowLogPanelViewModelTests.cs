using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Arknights;
using ArkPlot.Core.Infrastructure;
using ArkPlot.Core.Model;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// WorkflowLogPanelViewModel 阶段推进逻辑 headless 测试。
/// Append 通过 Dispatcher.UIThread.Post 派发，测试用 RunJobs 同步执行排队的任务。
/// </summary>
public class WorkflowLogPanelViewModelTests
{
    [AvaloniaFact]
    public void BeginPipeline_EstablishesOrderedStages()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "初始化", "下载", "完成" });

        Assert.Equal(3, vm.Stages.Count);
        Assert.Equal("初始化", vm.Stages[0].Name);
        Assert.True(vm.Stages[0].IsFirst);
        Assert.True(vm.Stages[2].IsLast);
        Assert.Equal(StageStatus.Pending, vm.Stages[0].Status);
    }

    [AvaloniaFact]
    public void EnterStage_MarksActive_AndAppendAssignsToCurrentStage()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "初始化", "下载", "完成" });

        vm.EnterStage(1);
        vm.Append(LogLevel.Info, "下载开始");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(StageStatus.Active, vm.Stages[1].Status);
        Assert.Single(vm.Stages[1].Logs);
        Assert.Equal("下载开始", vm.Stages[1].Logs[0].Message);
        // 未进入的阶段保持空
        Assert.Empty(vm.Stages[0].Logs);
    }

    [AvaloniaFact]
    public void ErrorAppend_IncrementsErrorCount_AndSetsHint()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "初始化", "下载", "完成" });

        vm.EnterStage(1);
        vm.Append(LogLevel.Error, "下载失败");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, vm.Stages[1].ErrorCount);
        Assert.True(vm.Stages[1].ShowErrorHint);
    }

    [AvaloniaFact]
    public void CompletePipeline_MarksAllDone_AndSetsComplete()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "初始化", "下载", "完成" });

        vm.EnterStage(0);
        vm.EnterStage(1);
        vm.CompletePipeline();
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsComplete);
        Assert.Equal(100, vm.TotalProgress);
        Assert.All(vm.Stages, s => Assert.Equal(StageStatus.Done, s.Status));
    }

    [AvaloniaFact]
    public void Append_CapturesStageAtCallTime_LogGoesToCorrectStage()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "A", "B", "C" });

        vm.EnterStage(0);
        vm.Append(LogLevel.Info, "第一阶段日志");
        vm.EnterStage(1); // 在 Post 延迟执行前推进到 B
        Dispatcher.UIThread.RunJobs();

        // 日志应进 A（发出时处于的阶段），而不是 Post 执行时的 B
        Assert.Single(vm.Stages[0].Logs);
        Assert.Equal("第一阶段日志", vm.Stages[0].Logs[0].Message);
        Assert.Empty(vm.Stages[1].Logs);
    }

    [AvaloniaFact]
    public void Append_OutsidePipeline_GoesToSystemStage()
    {
        var vm = new WorkflowLogPanelViewModel();

        vm.Append(LogLevel.Warn, "系统消息");
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.SystemStage.Logs);
        Assert.Equal("系统消息", vm.SystemStage.Logs[0].Message);
    }

    [AvaloniaFact]
    public void ShowErrorsOnly_FiltersVisibleLogs()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "初始化", "下载", "完成" });

        vm.EnterStage(1);
        vm.Append(LogLevel.Info, "普通消息");
        vm.Append(LogLevel.Error, "错误消息");
        Dispatcher.UIThread.RunJobs();

        vm.ShowErrorsOnly = true;
        vm.RefreshVisibleLogs();

        Assert.Single(vm.VisibleLogs);
        Assert.Equal(LogLevel.Error, vm.VisibleLogs[0].Level);
    }

    [AvaloniaFact]
    public void AddImage_AddsLogWithImageUrlAndDetail()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "A" });
        vm.EnterStage(0);

        vm.AddImage("http://img/1.png", "角色立于荒野");
        Dispatcher.UIThread.RunJobs();

        var log = Assert.Single(vm.Stages[0].Logs);
        Assert.True(log.HasImage);
        Assert.Equal("http://img/1.png", log.ImageUrl);
        Assert.True(log.HasDetail);
    }

    [AvaloniaFact]
    public void AddThought_AddsLogWithDetailAndTooltip()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "A" });
        vm.EnterStage(0);

        vm.AddThought("思考", "第一步分析场景\n第二步组织叙事", "正文输出");
        Dispatcher.UIThread.RunJobs();

        var log = Assert.Single(vm.Stages[0].Logs);
        Assert.True(log.HasDetail);
        Assert.Contains("第一步分析场景", log.Detail);
        Assert.Contains("正文输出", log.Detail);
        Assert.NotNull(log.Tooltip);
    }

    [AvaloniaFact]
    public void ToggleExpand_TogglesOnlyWhenHasDetail()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "A" });
        vm.EnterStage(0);

        vm.AddImage("http://img/2.png", "场景描述");
        Dispatcher.UIThread.RunJobs();
        var withDetail = vm.Stages[0].Logs[0];

        vm.ToggleExpand(withDetail);
        Assert.True(withDetail.IsExpanded);
        vm.ToggleExpand(withDetail);
        Assert.False(withDetail.IsExpanded);

        vm.Append(LogLevel.Info, "无详情");
        Dispatcher.UIThread.RunJobs();
        var plain = vm.Stages[0].Logs[1];
        vm.ToggleExpand(plain);
        Assert.False(plain.IsExpanded);
    }

    [AvaloniaFact]
    public void EnterStage_ClosesPreviousStage()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "初始化", "下载", "完成" });

        vm.EnterStage(0);
        vm.EnterStage(1);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(StageStatus.Done, vm.Stages[0].Status);
        Assert.Equal(StageStatus.Active, vm.Stages[1].Status);
    }

    [AvaloniaFact]
    public void EnterStage_SameIndex_IsNoOp()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "初始化", "下载", "完成" });

        vm.EnterStage(1);
        vm.EnterStage(1);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(StageStatus.Active, vm.Stages[1].Status);
        // 未被关闭再重启：不产生 Done，计时也不被重置写 Duration。
        Assert.Equal(StageStatus.Pending, vm.Stages[0].Status);
        Assert.Equal(string.Empty, vm.Stages[1].Duration);
    }

    [AvaloniaFact]
    public void FailStage_MarksFailed_AndAppendFallsBackToSystemStage()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "初始化", "下载", "完成" });

        vm.EnterStage(1);
        vm.FailStage("下载失败");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(StageStatus.Failed, vm.Stages[1].Status);
        var err = Assert.Single(vm.Stages[1].Logs);
        Assert.Equal(LogLevel.Error, err.Level);
        Assert.Equal("下载失败", err.Message);

        // FailStage 清空 _activeStage 后，Append 落到系统阶段。
        vm.Append(LogLevel.Warn, "系统消息");
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.SystemStage.Logs);
        Assert.Equal("系统消息", vm.SystemStage.Logs[0].Message);
    }

    [AvaloniaFact]
    public void CompletePipeline_SkipsFailedStage()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "初始化", "下载", "完成" });

        vm.EnterStage(0);
        vm.EnterStage(1);
        vm.FailStage("下载失败");
        vm.CompletePipeline();
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsComplete);
        Assert.Equal(StageStatus.Failed, vm.Stages[1].Status);
        Assert.Equal(StageStatus.Done, vm.Stages[0].Status);
        Assert.Equal(StageStatus.Done, vm.Stages[2].Status);
    }

    [AvaloniaFact]
    public void UpdateProgress_IncludesActiveStageFraction()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "A", "B" });

        vm.EnterStage(0);
        vm.EnterStage(1); // A done，B active
        vm.Stages[1].Progress = 50;
        vm.Append(LogLevel.Info, "推进进度"); // 触发 UpdateProgress
        Dispatcher.UIThread.RunJobs();

        // 1 done + active 50% → (1 + 0.5) / 2 * 100 = 75
        Assert.Equal(75, vm.TotalProgress);
    }

    [AvaloniaFact]
    public void EnterStage_WritesDuration_OnStageClose()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "A", "B" });

        vm.EnterStage(0);
        vm.EnterStage(1);
        Dispatcher.UIThread.RunJobs();

        Assert.False(string.IsNullOrEmpty(vm.Stages[0].Duration));
    }

    [AvaloniaFact]
    public void LocateErrorCommand_ShowsOnlyErrors_AndNoErrorStatus()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "A", "B" });

        vm.EnterStage(1);
        vm.Append(LogLevel.Info, "普通消息");
        vm.Append(LogLevel.Error, "错误消息");
        Dispatcher.UIThread.RunJobs();

        vm.LocateErrorCommand.Execute(null);
        var err = Assert.Single(vm.VisibleLogs);
        Assert.Equal(LogLevel.Error, err.Level);

        // 选中无错误的阶段 → 提示没有错误。
        vm.SelectStage(vm.Stages[0]);
        vm.LocateErrorCommand.Execute(null);
        Assert.Contains("没有错误", vm.StatusMessage);
    }

    [AvaloniaFact]
    public void SelectedStageShowErrorHint_FollowsSelection()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "A", "B" });

        vm.EnterStage(0);
        vm.Append(LogLevel.Error, "错误");
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.SelectedStageShowErrorHint);

        vm.EnterStage(1);
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.SelectedStageShowErrorHint);
    }

    [AvaloniaFact]
    public void ClearCache_DeletesOnlyCurrentChaptersPicDescAndNovelFiles()
    {
        DbFactory.ConfigureForTesting("Data Source=:memory:");
        ArknightsDbInitializer.Init();
        var db = DbFactory.GetClient();

        // 当前章节用到的图：立绘 code（char_muersai）与场景 URL
        db.Insertable(new PicDescription
        {
            DedupKey = "char_muersai",                    // 立绘：本章用到 → 应删
            ImageUrl = "https://a/1.png",
            PicDesc = "缪尔赛思立绘",
            Source = "Vision",
        }).ExecuteCommand();
        db.Insertable(new PicDescription
        {
            DedupKey = "https://a/bg1.png",               // 本章场景 → 应删
            ImageUrl = "https://a/bg1.png",
            PicDesc = "场景",
            Source = "Vision",
        }).ExecuteCommand();
        // 其他章节独占的缓存：不应删除
        db.Insertable(new PicDescription
        {
            DedupKey = "char_aphris",
            ImageUrl = "https://a/2.png",
            PicDesc = "洛伦茨立绘",
            Source = "Vision",
        }).ExecuteCommand();
        // Placeholder 记录：保留（可重试语义）
        db.Insertable(new PicDescription
        {
            DedupKey = "char_skadi",
            ImageUrl = "https://a/3.png",
            PicDesc = "占位",
            Source = "Placeholder",
        }).ExecuteCommand();

        // 当前章节条目：characterCode + ResourceUrls 中带有上述图
        var entries = new List<ScriptLine>
        {
            new() { CharacterCode = "char_muersai", ResourceUrls = ["https://a/1.png"] },
            new() { CharacterCode = null, ResourceUrls = ["https://a/bg1.png"] },
        };

        // 造当前章节输出目录：.novelizer-cache.json 含本章 + 其他章节 key；输出文件同样两类
        var outputDir = Path.Combine(Path.GetTempPath(), $"clear_cache_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);
        var cacheMap = new Dictionary<string, string>
        {
            [$"{Path.Combine(outputDir, "孤星_第1章.md")}::deepseek-v4-flash"] = "hash1",
            [$"{Path.Combine(outputDir, "孤星_第2章.md")}::deepseek-v4-flash"] = "hash2",
        };
        var cacheJson = System.Text.Json.JsonSerializer.Serialize(cacheMap);
        File.WriteAllText(Path.Combine(outputDir, ".novelizer-cache.json"), cacheJson);
        File.WriteAllText(Path.Combine(outputDir, "孤星_第1章_novel_deepseek-v4-flash.md"), "第1章小说");
        File.WriteAllText(Path.Combine(outputDir, "孤星_第2章_novel_deepseek-v4-flash.md"), "第2章小说");

        var vm = new WorkflowLogPanelViewModel
        {
            StoryOutputDir = outputDir,
            CurrentChapterNames = ["孤星_第1章"],
            CurrentChapterEntries = entries,
        };
        vm.ClearCacheCommand.Execute(null);

        // 本章的 2 条图片描述被删（剩余 1 条 Vision = 其他章节的 char_aphris）；Placeholder 保留
        Assert.Equal(1, db.Queryable<PicDescription>().Count(it => it.Source == "Vision"));
        Assert.Null(db.Queryable<PicDescription>().First(it => it.Source == "Vision" && it.DedupKey == "char_muersai"));
        Assert.NotNull(db.Queryable<PicDescription>().First(it => it.DedupKey == "char_aphris"));
        Assert.NotNull(db.Queryable<PicDescription>().First(it => it.Source == "Placeholder"));

        // 小说缓存：本章 key 与文件删除，其他章节保留（json 反序列化读回，规避 \uXXXX 转义干扰）
        var remaining = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(outputDir, ".novelizer-cache.json")))!;
        Assert.DoesNotContain(remaining.Keys, k => k.Contains("孤星_第1章.md"));
        Assert.Contains(remaining.Keys, k => k.Contains("孤星_第2章.md"));
        Assert.False(File.Exists(Path.Combine(outputDir, "孤星_第1章_novel_deepseek-v4-flash.md")));
        Assert.True(File.Exists(Path.Combine(outputDir, "孤星_第2章_novel_deepseek-v4-flash.md")));
        Assert.Contains("图片描述缓存 2 条", vm.StatusMessage);
        Assert.Contains("小说化缓存 1 条 / 1 个文件", vm.StatusMessage);

        DbFactory.Reset();
        try { Directory.Delete(outputDir, recursive: true); } catch { }
    }

    [AvaloniaFact]
    public void ClearCache_WithNoContext_ReportsNothingToClear()
    {
        DbFactory.ConfigureForTesting("Data Source=:memory:");
        ArknightsDbInitializer.Init();

        var vm = new WorkflowLogPanelViewModel { StoryOutputDir = null, CurrentChapterNames = [] };
        vm.ClearCacheCommand.Execute(null);

        Assert.Contains("未发现可清除的缓存", vm.StatusMessage);

        DbFactory.Reset();
    }
}