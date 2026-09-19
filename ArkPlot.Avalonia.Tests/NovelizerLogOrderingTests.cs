using System.Linq;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Novelizer;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// 小说化日志顺序回归：思考条目必须与「Turn N 完成」按真实发生顺序交错，
/// 而不是全部思考挤在前面、全部完成行堆在后面。
/// 根因：管线日志经 InvokeAsync 入队（由 Append 统一 Post，两跳），而 onThought 直接
/// 在调用线程调 AddThought（一跳 Post）；Mock 客户端同步返回时整轮循环一口气跑完，
/// 一跳的思考条目全部插队到两跳的日志前面。修复后 onThought 同样经 InvokeAsync 入队。
/// </summary>
public class NovelizerLogOrderingTests
{
    [AvaloniaFact]
    public async Task ThoughtEntries_InterleaveWithTurnCompletions()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "小说化" });
        vm.EnterStage(0);

        var processor = new ChapterProcessor(
            new MockBailianClient(new System.Net.Http.HttpClient(), new ApiConfig()),
            "system",
            log: msg => Dispatcher.UIThread.InvokeAsync(() => vm.Append(LogLevel.Info, msg)),
            logError: _ => { },
            onThought: (summary, prompt, thinking, answer, turnKey, isCompress) =>
                Dispatcher.UIThread.InvokeAsync(() =>
                    vm.AddThought(summary, prompt, thinking, answer, turnKey: turnKey, isCompress: isCompress)),
            enableMultiTurn: true,
            chunkSize: 1000);

        await processor.ProcessAllAsync([new Chapter(0, "测试章", new string('x', 3500))], "mock-model");
        Dispatcher.UIThread.RunJobs(DispatcherPriority.SystemIdle);

        var messages = vm.Stages[0].Logs.Select(e => e.Message).ToList();
        int Idx(string contains) => messages.FindIndex(m => m.Contains(contains));

        var thought1 = Idx("思考 Turn 1");
        var thought2 = Idx("思考 Turn 2");
        Assert.True(thought1 >= 0, $"应有第 1 轮思考条目；实际日志：\n{string.Join("\n", messages)}");
        Assert.True(thought2 >= 0, "应有第 2 轮思考条目（3500 字符按 1000 拆块应多于 2 轮）");

        var done1 = Idx("✅ Turn 1");
        Assert.True(done1 >= 0, "应有第 1 轮完成行");

        Assert.True(thought1 < done1, "第 1 轮思考应先于第 1 轮完成行");
        Assert.True(done1 < thought2, "第 2 轮思考必须出现在第 1 轮完成之后——思考全部提前插队即为回归");
    }
}
