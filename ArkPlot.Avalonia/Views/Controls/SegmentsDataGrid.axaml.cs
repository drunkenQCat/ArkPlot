using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ArkPlot.Avalonia.ViewModels;

namespace ArkPlot.Avalonia.Views.Controls;

public partial class SegmentsDataGrid : UserControl
{
    public SegmentsDataGrid()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 点击 flyout 内角色候选项：直接调用 VM 应用修改，并关闭 flyout。
    /// 比依赖 SelectedItem TwoWay 绑定更可靠（flyout 内焦点路径特殊）。
    /// </summary>
    private async void CharacterCandidate_Tapped(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock tb || tb.DataContext is not string newName)
            return;

        // ItemTemplate 内的 DataContext 是 item（string）。
        // 需要走到显式 DataContext=TtsViewModel 的 DockPanel，或窗口级的 TtsVM。
        var vm = tb.FindAncestorOfType<DockPanel>()?.DataContext as TtsViewModel
                 ?? tb.FindAncestorOfType<Window>()?.DataContext as TtsViewModel;
        if (vm == null) return;

        var target = vm.CharacterEditTarget;

        // 关闭 flyout
        var btn = tb.FindAncestorOfType<Button>();
        if (btn != null && FlyoutBase.GetAttachedFlyout(btn) is { } flyout)
            flyout.Hide();

        // 清理状态（防止下次打开残留）
        vm.CharacterEditTarget = null;
        vm.CharacterEditFilter = "";

        // 应用修改
        if (target != null)
            await vm.ChangeCharacterForSegmentAsync(target, newName);
    }
}
