using System;
using ArkPlot.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SukiUI.Controls;

namespace ArkPlot.Avalonia.Views;

public partial class TtsWindow : SukiWindow
{
    private DateTime _lastPortraitClick = DateTime.MinValue;
    private int _portraitClickCount;

    public TtsWindow()
    {
        InitializeComponent();

        Opened += async (_, _) =>
        {
            if (DataContext is TtsViewModel vm)
                await vm.InitializeAsync();
        };
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    public TtsWindow(TtsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void PortraitPanel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastPortraitClick).TotalMilliseconds < 500)
            _portraitClickCount++;
        else
            _portraitClickCount = 1;
        _lastPortraitClick = now;

        if (_portraitClickCount >= 3)
        {
            _portraitClickCount = 0;
            if (DataContext is TtsViewModel vm)
                vm.IsDebugMode = !vm.IsDebugMode;
        }
    }

    private async void DebugInfo_Tapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TtsViewModel vm && !string.IsNullOrEmpty(vm.DebugInfo))
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(vm.DebugInfo);
        }
    }
}
