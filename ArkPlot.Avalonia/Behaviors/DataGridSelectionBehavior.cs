using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;

namespace ArkPlot.Avalonia.Behaviors;

/// <summary>
/// Behavior that attaches to a DataGrid and invokes a command when selection changes.
/// Passes the list of selected items as the command parameter.
/// </summary>
public class DataGridSelectionBehavior : Behavior<DataGrid>
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<DataGridSelectionBehavior, ICommand?>(nameof(Command));

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is { } dataGrid)
        {
            dataGrid.SelectionChanged += OnSelectionChanged;
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject is { } dataGrid)
        {
            dataGrid.SelectionChanged -= OnSelectionChanged;
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (AssociatedObject is { } dataGrid && Command?.CanExecute(null) == true)
        {
            var selectedItems = new List<object>();
            foreach (var item in dataGrid.SelectedItems)
            {
                if (item != null)
                    selectedItems.Add(item);
            }
            Command.Execute(selectedItems);
        }
    }
}
