using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using Avalonia.Threading;
using System;

namespace ArkPlot.Avalonia.Styles;
/// <summary>
/// 让 ScrollViewer 在内容高度变化时自动滚到底部。
/// </summary>
public class AutoScrollBehavior : Behavior<ScrollViewer>
{
    /// <summary>
    /// 启用或禁用自动滚动
    /// </summary>
    public bool Enabled { get; set; } = true;

    private bool _shouldScroll = true;

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject != null)
        {
            // 用户手动滚动时，如果没滚到底，暂停自动滚动
            AssociatedObject.PropertyChanged += ScrollViewer_PropertyChanged;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.PropertyChanged -= ScrollViewer_PropertyChanged;
        }

        base.OnDetaching();
    }

    private void ScrollViewer_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!Enabled || AssociatedObject == null) return;

        if (e.Property == ScrollViewer.OffsetProperty)
        {
            var scroll = AssociatedObject;
            // 当用户滚动到顶部或中间时，暂停自动滚动
            _shouldScroll = Math.Abs(scroll.Offset.Y - scroll.ScrollBarMaximum.Y) < 1e-2;
        }
        else if (e.Property == ScrollViewer.ExtentProperty)
        {
            if (_shouldScroll)
            {
                // 延迟执行，保证内容渲染完成后再滚动
                Dispatcher.UIThread.Post(() =>
                {
                    AssociatedObject?.ScrollToEnd();
                }, DispatcherPriority.Background);
            }
        }
    }
}

public static class ScrollViewerExtensions
{
    public static void ScrollToEnd(this ScrollViewer scroll)
    {
        scroll.Offset = new Vector(scroll.Offset.X, scroll.ScrollBarMaximum.Y);
    }
}

