using Avalonia.Controls;
using Avalonia.Input;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;

namespace ArkPlot.Avalonia.Views.Controls;

/// <summary>工作流日志面板的日志流子控件：普通日志行 + 分段横幅两种模板，自动滚动。</summary>
public partial class WorkflowLogStream : UserControl
{
    public WorkflowLogStream()
    {
        InitializeComponent();
    }

    /// <summary>点击日志条目：有关联复盘轮次的（小说化思考/压缩）→ 请求打开复盘面板定位；否则展开/收起详情。</summary>
    private void LogTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border { DataContext: LogEntry entry } ||
            DataContext is not WorkflowLogPanelViewModel vm)
            return;

        if (!string.IsNullOrEmpty(entry.TurnKey))
            vm.OpenTraceRequested?.Invoke(entry.TurnKey);
        else
            vm.ToggleExpand(entry);
    }
}