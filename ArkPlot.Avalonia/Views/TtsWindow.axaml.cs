using Avalonia.Controls;
using ArkPlot.Avalonia.Models;
using ArkPlot.Avalonia.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;

namespace ArkPlot.Avalonia.Views;

public partial class TtsWindow : Window
{
    public TtsWindow()
    {
        InitializeComponent();
        SegmentsDataGrid.SelectionChanged += SegmentsDataGrid_SelectionChanged;
        
        DataContextChanged += OnDataContextChanged;
        
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

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is TtsViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void SegmentsDataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not TtsViewModel vm) return;
        vm.SelectedSegmentRows.Clear();
        foreach (var item in SegmentsDataGrid.SelectedItems.OfType<SegmentRow>())
            vm.SelectedSegmentRows.Add(item);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TtsViewModel.SelectedSegment)) return;
        if (DataContext is TtsViewModel vm && vm.SelectedSegment != null)
        {
            // 使用 Normal 优先级确保在下一帧渲染前执行滚动
            // Background 优先级太低，会被播放循环的 UI 更新阻塞
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                SegmentsDataGrid.ScrollIntoView(vm.SelectedSegment, null);
            }, global::Avalonia.Threading.DispatcherPriority.Normal);
        }
    }
}
