using System.Collections.Generic;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;
using ArkPlot.Avalonia.Views.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// 日志行点击行为的 headless 断言。这里覆盖一处曾经出过 bug 的接线：LogRow 的 DataContext 是
/// <see cref="LogEntry"/>（一行一条日志），面板 VM 在**祖先**控件上 —— 从 WorkflowLogStream 搬进来时
/// 若仍按「自身 DataContext 是 VM」判断，会直接 return 导致所有行都点不动。
/// </summary>
public class LogRowInteractionTests
{
    /// <summary>把单行放进一个 DataContext 为面板 VM 的宿主里，模拟它在日志面板中的真实位置。</summary>
    private static (Window Window, LogRow Row) ShowRow(WorkflowLogPanelViewModel vm, LogEntry entry)
    {
        var row = new LogRow { DataContext = entry };
        var host = new ContentControl { DataContext = vm, Content = row };

        var window = new Window { Content = host, Width = 480, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.SystemIdle);
        return (window, row);
    }

    private static WorkflowLogPanelViewModel NewPanelVm()
    {
        var vm = new WorkflowLogPanelViewModel();
        vm.BeginPipeline(new[] { "阶段一" });
        vm.EnterStage(0);
        return vm;
    }

    private static Border RowBorder(LogRow row)
        => Assert.Single(row.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("logrow"));

    /// <summary>在行内某个相对位置模拟一次左键点击（按下+抬起，Tapped 由手势合成）。</summary>
    private static void ClickAt(Window window, LogRow row, double x, double y)
    {
        var border = RowBorder(row);
        var point = border.TranslatePoint(new Point(x, y), window);
        Assert.True(point is not null, $"无法换算行内坐标（Bounds={border.Bounds}，说明布局未跑）");

        window.MouseDown(point!.Value, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point!.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.SystemIdle);
    }

    private static void ClickCenter(Window window, LogRow row)
    {
        var border = RowBorder(row);
        ClickAt(window, row, border.Bounds.Width / 2, border.Bounds.Height / 2);
    }

    [AvaloniaFact]
    public void ClickingTraceRow_RequestsTraceReviewWithItsTurnKey()
    {
        var vm = NewPanelVm();
        string? requested = null;
        vm.OpenTraceRequested = key => requested = key;

        var (window, row) = ShowRow(vm, new LogEntry(LogLevel.Info, "🧠 思考", turnKey: "run:0"));

        ClickCenter(window, row);

        Assert.Equal("run:0", requested);
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingPlainRow_TogglesDetailExpansion()
    {
        var vm = NewPanelVm();
        var entry = new LogEntry(LogLevel.Info, "普通日志", detail: "详情内容");
        var (window, row) = ShowRow(vm, entry);

        Assert.False(entry.IsExpanded);
        ClickCenter(window, row);
        Assert.True(entry.IsExpanded);

        window.Close();
    }

    [AvaloniaFact]
    public void ClickingBlankAreaOfRow_StillToggles_RowBackgroundMustStayHittable()
    {
        var vm = NewPanelVm();
        var entry = new LogEntry(LogLevel.Info, "短", detail: "详情内容");
        var (window, row) = ShowRow(vm, entry);

        var border = RowBorder(row);
        // 贴右边缘点：那里没有任何文字。背景若是 null（而非 Transparent），空隙不参与命中 → 点不动。
        ClickAt(window, row, border.Bounds.Width - 4, border.Bounds.Height / 2);

        Assert.True(entry.IsExpanded, "行内空白处未响应点击（背景可能为 null 导致命中穿透）");
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingTraceRow_DoesNotExpandDetail_ButPlainRowDoes()
    {
        var vm = NewPanelVm();
        string? requested = null;
        vm.OpenTraceRequested = key => requested = key;

        var traceEntry = new LogEntry(LogLevel.Info, "🔄 压缩", turnKey: "run:1", isCompress: true);
        var (window, row) = ShowRow(vm, traceEntry);

        ClickCenter(window, row);

        Assert.Equal("run:1", requested);
        Assert.False(traceEntry.IsExpanded); // trace 行走复盘分支，不应同时展开详情

        window.Close();
    }
}