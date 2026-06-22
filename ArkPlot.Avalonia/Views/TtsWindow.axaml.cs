using System;
using ArkPlot.Avalonia.ViewModels;
using SukiUI.Controls;

namespace ArkPlot.Avalonia.Views;

public partial class TtsWindow : SukiWindow
{
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
}
