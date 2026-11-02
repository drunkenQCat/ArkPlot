using ArkPlot.Avalonia.ViewModels.Test;
using SukiUI.Controls;

namespace ArkPlot.Avalonia.Views;

public partial class TestWindow : SukiWindow
{
    public TestWindow()
    {
        InitializeComponent();
        DevOptionsTab.DataContext = new DevOptionsViewModel();
        TextModelTestTab.DataContext = new CachedContentTestViewModel(imageTest: false);
        ImageModelTestTab.DataContext = new CachedContentTestViewModel(imageTest: true);
        PortraitTestTab.DataContext = new PortraitTestViewModel();
        AudioTestTab.DataContext = new AudioTestViewModel();
        NetworkFailureTab.DataContext = new NetworkFailureTestViewModel();
        Closed += (_, _) =>
        {
            (TextModelTestTab.DataContext as CachedContentTestViewModel)?.Dispose();
            (ImageModelTestTab.DataContext as CachedContentTestViewModel)?.Dispose();
            (AudioTestTab.DataContext as AudioTestViewModel)?.Dispose();
        };
    }
}
