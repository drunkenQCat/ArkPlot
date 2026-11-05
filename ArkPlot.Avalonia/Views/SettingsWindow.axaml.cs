using Avalonia.Controls;
using Avalonia.Interactivity;
using SukiUI.Controls;
using ArkPlot.Avalonia.ViewModels;

namespace ArkPlot.Avalonia;

public partial class SettingsWindow : SukiWindow
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void SettingsNav_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var index) && DataContext is SettingsViewModel vm)
            vm.SelectedTabIndex = index;
    }
}
