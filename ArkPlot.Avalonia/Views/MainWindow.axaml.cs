using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SukiUI.Controls;

namespace ArkPlot.Avalonia.Views;

public partial class MainWindow : SukiWindow
{
    private WorkflowLogWindow? _logWindow;
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => { _logWindow?.Close(); (DataContext as System.IDisposable)?.Dispose(); };
        Activated += (_, _) =>
        {
            if (DataContext is ViewModels.MainWindowViewModel vm && !vm.LoadMdCommand.IsRunning) vm.RefreshPlan();
        };
    }

    private void Theme_Click(object? sender, RoutedEventArgs e) => SukiUI.SukiTheme.GetInstance().SwitchBaseTheme();

    private void OpenLogs_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainWindowViewModel vm) return;
        if (_logWindow != null) { _logWindow.Activate(); return; }
        _logWindow = new WorkflowLogWindow { DataContext = vm.WorkflowLog };
        _logWindow.Closed += (_, _) => _logWindow = null;
        _logWindow.Show(this);
    }

    private void StorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Defer until LoadActs has finished replacing the activity and chapter collection.
        Dispatcher.UIThread.Post(async () =>
        {
            if (DataContext is not ViewModels.MainWindowViewModel vm || vm.LoadMdCommand.IsRunning) return;
            try { await vm.LoadChaptersCommand.ExecuteAsync(null); }
            catch (System.Exception ex) { vm.Status = $"章节加载失败：{ex.Message}"; }
        });
    }

    private void BannerImage_Tapped(object? sender, TappedEventArgs e)
    {
        var testWindow = new TestWindow();
        testWindow.Show();
    }
}
