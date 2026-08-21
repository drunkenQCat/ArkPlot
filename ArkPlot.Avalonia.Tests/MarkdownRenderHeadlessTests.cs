using System;
using System.IO;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Avalonia.Views;
using ArkPlot.Novelizer;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// Markdown 渲染链路的 headless 冒烟测试：TraceReviewWindow 打开后可正常把
/// system/user/思考/输出渲染为控件树（Markdown.Avalonia 引擎 + 窗口级样式注入不炸）。
/// </summary>
public class MarkdownRenderHeadlessTests
{
    private const string SamplePrompt = "—— system ——\n\n你是一位资深小说家。\n\n—— user ——\n\n# 第一章\n\n罗德岛的走廊 **安静** 而漫长。";
    private const string SampleThinking = "场景：**舰内走廊**，夜晚。";
    private const string SampleAnswer = "# 孤星\n\n> 罗德岛的走廊在深夜格外安静\n\n```text\ncode block\n```";

    [AvaloniaFact]
    public void Window_WithSelectedTurn_RendersMarkdownContentControls()
    {
        var traceDir = Path.Combine(Path.GetTempPath(), $"trace_md_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(traceDir);
            var collector = new NovelizerTraceCollector();
            collector.BeginRun("deepseek-v4-flash");
            collector.Add("🧠 第 1 章 思考 单轮", SamplePrompt, SampleThinking, SampleAnswer, "孤星", false);

            var vm = new TraceReviewViewModel { TraceRoot = traceDir, LiveCollector = collector };
            vm.RefreshRuns();
            vm.SelectedRun = vm.Runs[0];
            vm.SelectedTurn = vm.Turns[0];

            // 在头less环境下构建窗口：加载 AXAML + MarkdownStyleStandard 注入，不应抛异常
            var window = new TraceReviewWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // 输入页：system/user 两个子卡都已渲染出控件树（Markdown 转换产物非空）
            var systemContent = window.FindControl<ContentControl>("SystemContent");
            var userContent = window.FindControl<ContentControl>("UserContent");
            Assert.NotNull(systemContent);
            Assert.NotNull(userContent);
            Assert.NotNull(systemContent!.Content);
            Assert.NotNull(userContent!.Content);

            // 翻页到输出页：answer 渲染产物非空
            vm.SelectPageCommand.Execute("Output");
            Dispatcher.UIThread.RunJobs();
            var outputContent = window.FindControl<ContentControl>("OutputContent");
            Assert.NotNull(outputContent);
            Assert.NotNull(outputContent!.Content);
            Assert.True(vm.IsOutputPageVisible);

            window.Close();
        }
        finally
        {
            try { Directory.Delete(traceDir, recursive: true); } catch { }
        }
    }
}