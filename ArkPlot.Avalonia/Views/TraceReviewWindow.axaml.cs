using ArkPlot.Avalonia.ViewModels;
using SukiUI.Controls;

namespace ArkPlot.Avalonia.Views;

public partial class TraceReviewWindow : SukiWindow
{
    public TraceReviewWindow()
    {
        InitializeComponent();
    }

    public TraceReviewWindow(TraceReviewViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}