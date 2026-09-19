using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;

namespace ArkPlot.Avalonia.Views.Controls;

/// <summary>日志流里的一行：普通日志行 + 分段横幅两种形态；带 turnKey 的行显示为可点击的圆角按钮。</summary>
public partial class LogRow : UserControl
{
    public LogRow()
    {
        InitializeComponent();
    }

    /// <summary>点击日志条目：有关联复盘轮次的（小说化思考/压缩）→ 请求打开复盘面板定位；否则展开/收起详情。</summary>
    private void LogTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border { DataContext: LogEntry entry })
            return;

        // 本行的 DataContext 是 LogEntry（一行一条日志），面板 VM 挂在**祖先**控件上
        // （WorkflowLogPanel → WorkflowLogStream）——不能按自身 DataContext 判断，否则永远 return。
        var vm = this.GetVisualAncestors()
            .Select(ancestor => ancestor.DataContext)
            .OfType<WorkflowLogPanelViewModel>()
            .FirstOrDefault();
        if (vm is null)
            return;

        if (!string.IsNullOrEmpty(entry.TurnKey))
            vm.OpenTraceRequested?.Invoke(entry.TurnKey);
        else
            vm.ToggleExpand(entry);
    }
}