using Avalonia.Controls;

namespace ArkPlot.Avalonia.Views.Controls;

/// <summary>
/// 工作流日志面板的日志流容器：只负责承载 <see cref="LogRow"/> 列表与自动滚动；
/// 单行的外观与交互都在 LogRow 里（拆开是为了让一行能被单独测量/断言）。
/// </summary>
public partial class WorkflowLogStream : UserControl
{
    public WorkflowLogStream()
    {
        InitializeComponent();
    }
}