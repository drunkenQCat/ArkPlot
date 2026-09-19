using System.Collections.Generic;
using System.Linq;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.Views.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

/// <summary>
/// 日志行「按钮化」外观的 headless 断言：可复盘日志（小说化思考/压缩）渲染为带底色的圆角按钮；
/// 普通日志背景必须是 Transparent —— Avalonia 只在背景非 null 时命中空隙，null 会让点击穿透，
/// 普通日志的 Tapped（展开详情）随之失效。样式优先级（本地值 > Style setter）也只有实测能验。
/// 直接测单行（LogRow）而不是整条 ItemsControl：headless 下 ItemsControl 的 item 容器不生成。
/// </summary>
public class LogRowStyleTests
{
    private const string ThinkMessage = "🧠 第 1 章「甲」思考 单轮";
    private const string CompressMessage = "🔄 第 1 章「甲」上下文压缩";
    private const string PlainMessage = "普通日志";
    private const string ErrorMessage = "错误日志";

    private static (Window Window, LogRow Think) ShowRows()
    {
        var think = new LogRow { DataContext = new LogEntry(LogLevel.Info, ThinkMessage, turnKey: "run:0") };
        var rows = new List<LogRow>
        {
            think,
            new() { DataContext = new LogEntry(LogLevel.Info, CompressMessage, turnKey: "run:1", isCompress: true) },
            new() { DataContext = new LogEntry(LogLevel.Info, PlainMessage) },
            new() { DataContext = new LogEntry(LogLevel.Error, ErrorMessage) },
        };

        var panel = new StackPanel();
        foreach (var row in rows) panel.Children.Add(row);

        var window = new Window { Content = panel, Width = 480, Height = 600 };
        window.Show();
        // 把 Dispatcher 队列清到最低优先级（含 Layout 优先级上的布局任务），让行完成测量/排列。
        Dispatcher.UIThread.RunJobs(DispatcherPriority.SystemIdle);
        return (window, think);
    }

    private static LogRow RowFor(Window window, string message)
    {
        var all = window.GetVisualDescendants().ToList();
        var rows = all.OfType<LogRow>().ToList();
        Assert.True(rows.Count > 0,
            $"未找到任何 LogRow（视觉树 {all.Count} 节点：{string.Join(" > ", all.Select(v => v.GetType().Name))}）");

        var actual = string.Join(" | ", rows.Select(r => ((LogEntry?)r.DataContext)?.Message ?? "<null>"));
        var row = rows.FirstOrDefault(r => ((LogEntry?)r.DataContext)?.Message == message);
        Assert.True(row is not null, $"未找到消息为「{message}」的日志行；实际行：{actual}");
        return row!;
    }

    /// <summary>取 LogRow 里那条日志行 Border（分段横幅的 Border 不带 logrow 样式类）。</summary>
    private static Border RowBorder(LogRow row)
        => Assert.Single(row.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("logrow"));

    /// <summary>样式里的颜色会被包成 ImmutableSolidColorBrush，所以按接口取色，不要求精确类型。</summary>
    private static Color ColorOf(IBrush? brush)
        => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [AvaloniaFact]
    public void TraceRow_RendersAsBlueButton()
    {
        var (window, think) = ShowRows();

        var border = RowBorder(think);
        Assert.Equal(Color.Parse("#EFF6FF"), ColorOf(border.Background));
        Assert.Equal(Color.Parse("#BFDBFE"), ColorOf(border.BorderBrush));
        Assert.Equal(1d, border.BorderThickness.Left);
        Assert.Equal(6d, border.CornerRadius.TopLeft);

        window.Close();
    }

    [AvaloniaFact]
    public void CompressRow_RendersAsAmberButton_DistinctFromThink()
    {
        var (window, _) = ShowRows();

        var border = RowBorder(RowFor(window, CompressMessage));
        Assert.Equal(Color.Parse("#FFFBEB"), ColorOf(border.Background));
        Assert.Equal(Color.Parse("#FDE68A"), ColorOf(border.BorderBrush));

        window.Close();
    }

    [AvaloniaFact]
    public void PlainRow_KeepsTransparentBackground_SoEmptyAreaStaysClickable()
    {
        var (window, _) = ShowRows();

        Assert.Equal(Colors.Transparent, ColorOf(RowBorder(RowFor(window, PlainMessage)).Background));

        window.Close();
    }

    [AvaloniaFact]
    public void ErrorRow_KeepsRedTint_AndIsNotAButton()
    {
        var (window, _) = ShowRows();

        var border = RowBorder(RowFor(window, ErrorMessage));
        Assert.Equal(Color.Parse("#33EF4444"), ColorOf(border.Background));
        Assert.Equal(0d, border.BorderThickness.Left);

        window.Close();
    }

    [AvaloniaFact]
    public void TraceRow_HoverDeepensBackground()
    {
        var (window, think) = ShowRows();

        var border = RowBorder(think);
        Assert.Equal(Color.Parse("#EFF6FF"), ColorOf(border.Background));

        var center = border.TranslatePoint(
            new Point(border.Bounds.Width / 2, border.Bounds.Height / 2), window);
        Assert.True(center is not null,
            $"无法换算行内坐标（Bounds={border.Bounds}，说明布局未跑）");

        window.MouseMove(center!.Value, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.SystemIdle);

        Assert.Equal(Color.Parse("#DBEAFE"), ColorOf(border.Background));

        window.Close();
    }
}