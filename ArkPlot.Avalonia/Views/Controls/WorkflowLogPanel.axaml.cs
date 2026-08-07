using Avalonia.Controls;
using Avalonia.Input;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;

namespace ArkPlot.Avalonia.Views.Controls;

public partial class WorkflowLogPanel : UserControl
{
    public WorkflowLogPanel()
    {
        InitializeComponent();
    }

    /// <summary>点击阶段圆点，切换右侧日志为对应阶段。</summary>
    private void StageTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: WorkflowStage stage } &&
            DataContext is WorkflowLogPanelViewModel vm)
        {
            vm.SelectStage(stage);
        }
    }

    /// <summary>点击日志条目，展开/收起详细内容（思考过程等）。</summary>
    private void LogTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: LogEntry entry } &&
            DataContext is WorkflowLogPanelViewModel vm)
        {
            vm.ToggleExpand(entry);
        }
    }
}