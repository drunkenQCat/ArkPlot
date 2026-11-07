using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ArkPlot.Avalonia.ViewModels.Test;
using ArkPlot.Avalonia.Views.Test;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using SukiUI.Controls;

namespace ArkPlot.Avalonia.Views;

public partial class TestWindow : SukiWindow
{
    private readonly Func<int, Task<Control>> _createContent;
    private readonly HashSet<TabItem> _startedTabs = [];
    private bool _opened;
    private bool _closed;

    public TestWindow() : this(CreateContentAsync) { }

    internal TestWindow(Func<int, Task<Control>> createContent)
    {
        _createContent = createContent;
        InitializeComponent();
        Opened += (_, _) =>
        {
            _opened = true;
            // Let the window paint before doing any work for the selected tab.
            Dispatcher.UIThread.Post(LoadSelectedTab, DispatcherPriority.Background);
        };
        TestTabs.SelectionChanged += (_, _) => LoadSelectedTab();
        Closed += (_, _) =>
        {
            _closed = true;
            foreach (var tab in _startedTabs)
                if (tab.Content is Control content) DisposeContent(content);
        };
    }

    private async void LoadSelectedTab() => await LoadSelectedTabAsync();

    internal async Task LoadSelectedTabAsync()
    {
        if (!_opened || _closed || TestTabs.SelectedItem is not TabItem tab || !_startedTabs.Add(tab)) return;
        tab.Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(24), Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "正在读取测试缓存…", HorizontalAlignment = HorizontalAlignment.Center },
                new ProgressBar { IsIndeterminate = true, Width = 240 }
            }
        };
        try
        {
            var content = await _createContent(TestTabs.SelectedIndex);
            if (_closed) DisposeContent(content);
            else tab.Content = content;
        }
        catch (Exception ex)
        {
            if (_closed) return;
            _startedTabs.Remove(tab);
            var retry = new Button { Content = "重试加载", HorizontalAlignment = HorizontalAlignment.Left };
            retry.Click += (_, _) => LoadSelectedTab();
            tab.Content = new StackPanel
            {
                Margin = new global::Avalonia.Thickness(24), Spacing = 12,
                Children = { new TextBlock { Text = $"加载失败：{ex.Message}", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap }, retry }
            };
        }
    }

    private static async Task<Control> CreateContentAsync(int index)
    {
        // Construct cache-backed models before binding them; database I/O runs off the UI thread.
        // Views and the audio player's DispatcherTimer must still be created on the UI thread.
        switch (index)
        {
            case 0:
            case 1:
                var cached = await Task.Run(() => new CachedContentTestViewModel(imageTest: index == 1));
                return new CachedContentTestPanel { DataContext = cached };
            case 2:
                return new DevOptionsPanel { DataContext = new DevOptionsViewModel() };
            case 3:
                var portrait = await Task.Run(() => new PortraitTestViewModel());
                return new PortraitTestPanel { DataContext = portrait };
            case 4:
                return new AudioTestPanel { DataContext = new AudioTestViewModel() };
            default:
                return new NetworkFailureTestPanel { DataContext = new NetworkFailureTestViewModel() };
        }
    }

    private static void DisposeContent(Control content) => (content.DataContext as IDisposable)?.Dispose();
}
