using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ArkPlot.Avalonia.ViewModels;

namespace ArkPlot.Avalonia.Views.Controls;

public partial class WorkflowLogPanel : UserControl
{
    public WorkflowLogPanel()
    {
        InitializeComponent();
        var stages = this.FindControl<ListBox>("StageList")!;
        stages.AddHandler(PointerPressedEvent, (_, _) => PauseFollowing(), RoutingStrategies.Tunnel);
        stages.AddHandler(KeyDownEvent, (_, _) => PauseFollowing(), RoutingStrategies.Tunnel);
    }

    private void PauseFollowing()
    {
        if (DataContext is WorkflowLogPanelViewModel vm) vm.FollowLatest = false;
    }
}
