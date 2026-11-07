using ArkPlot.Avalonia.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArkPlot.Avalonia.Tests;

public class TestWindowLoadingTests
{
    [AvaloniaFact]
    public async Task Window_ShowsWhileCacheIsPendingAndLoadsOnlyVisitedTabsOnce()
    {
        var requests = new List<int>();
        var pending = new TaskCompletionSource<Control>();
        var model = new DisposableModel();
        var window = new TestWindow(index =>
        {
            requests.Add(index);
            return index == 0 ? pending.Task : Task.FromResult<Control>(new TextBlock());
        });
        Assert.Empty(requests);
        window.Show();
        try
        {
            var load = window.LoadSelectedTabAsync();
            Assert.True(window.IsVisible);
            Assert.Equal(new[] { 0 }, requests);
            var tabs = window.FindControl<TabControl>("TestTabs")!;
            tabs.SelectedIndex = 2;
            await window.LoadSelectedTabAsync();
            Assert.Equal(new[] { 0, 2 }, requests);
            tabs.SelectedIndex = 0;
            await window.LoadSelectedTabAsync();
            var content = new TextBlock { Text = "缓存已载入", DataContext = model };
            pending.SetResult(content);
            await load;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(content, ((TabItem)tabs.Items[0]!).Content);
            Assert.Equal(new[] { 0, 2 }, requests);
        }
        finally { window.Close(); }
        Assert.True(model.Disposed);
    }

    [AvaloniaFact]
    public async Task ClosingWhileLoading_DiscardsAndDisposesLateContent()
    {
        var pending = new TaskCompletionSource<Control>();
        var window = new TestWindow(_ => pending.Task);
        window.Show();
        var load = window.LoadSelectedTabAsync();
        window.Close();
        var model = new DisposableModel();
        pending.SetResult(new TextBlock { DataContext = model });
        await load;
        Dispatcher.UIThread.RunJobs();
        Assert.True(model.Disposed);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task FailedTab_CanBeRetried()
    {
        var attempts = 0;
        var content = new TextBlock();
        var window = new TestWindow(_ => ++attempts == 1
            ? Task.FromException<Control>(new IOException("缓存暂不可读"))
            : Task.FromResult<Control>(content));
        window.Show();
        try
        {
            await window.LoadSelectedTabAsync();
            await window.LoadSelectedTabAsync();
            var tab = (TabItem)window.FindControl<TabControl>("TestTabs")!.SelectedItem!;
            Assert.Same(content, tab.Content);
            Assert.Equal(2, attempts);
        }
        finally { window.Close(); }
    }

    private sealed class DisposableModel : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
