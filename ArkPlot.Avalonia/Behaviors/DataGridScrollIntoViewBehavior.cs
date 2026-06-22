using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;

namespace ArkPlot.Avalonia.Behaviors;

/// <summary>
/// Behavior that scrolls a DataGrid row into view when a specified item changes.
/// </summary>
public class DataGridScrollIntoViewBehavior : Behavior<DataGrid>
{
    public static readonly StyledProperty<object?> ItemProperty =
        AvaloniaProperty.Register<DataGridScrollIntoViewBehavior, object?>(nameof(Item));

    public object? Item
    {
        get => GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    private static readonly StyledProperty<bool> SubscribedProperty =
        AvaloniaProperty.Register<DataGridScrollIntoViewBehavior, bool>("Subscribed");

    protected override void OnAttached()
    {
        base.OnAttached();
        if (!GetValue(SubscribedProperty))
        {
            this.GetObservable(ItemProperty).Subscribe(new ItemChangedObserver(this));
            SetValue(SubscribedProperty, true);
        }
    }

    private void OnItemChanged(object? item)
    {
        if (AssociatedObject is { } dataGrid && item != null)
        {
            dataGrid.ScrollIntoView(item, null);
        }
    }

    /// <summary>Observer to avoid requiring System.Reactive.</summary>
    private class ItemChangedObserver : IObserver<object?>
    {
        private readonly DataGridScrollIntoViewBehavior _behavior;
        public ItemChangedObserver(DataGridScrollIntoViewBehavior behavior) => _behavior = behavior;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(object? value) => _behavior.OnItemChanged(value);
    }
}
