using ArkPlot.Avalonia.ViewModels.Test;
using SukiUI.Controls;

namespace ArkPlot.Avalonia.Views;

public partial class TestWindow : SukiWindow
{
    public TestWindow()
    {
        InitializeComponent();
        PortraitTestTab.DataContext = new PortraitTestViewModel();
        AudioTestTab.DataContext = new AudioTestViewModel();
        NetworkFailureTab.DataContext = new NetworkFailureTestViewModel();
    }
}
