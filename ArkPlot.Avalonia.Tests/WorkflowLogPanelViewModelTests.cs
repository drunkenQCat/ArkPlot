using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;
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
}