using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace InventoryManagement.App.Controls;

/// <summary>
/// Attached property: <c>controls:RowDoubleClick.Command="{Binding StartEditCommand}"</c>
/// on a DataGrid runs that command with the row's item when a row (not the
/// header or empty space) is double-clicked - the same action as the row's
/// "Edit"/"Open" menu item.
/// </summary>
public static class RowDoubleClick
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(RowDoubleClick), new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
        {
            return;
        }

        grid.MouseDoubleClick -= OnMouseDoubleClick;
        if (e.NewValue is not null)
        {
            grid.MouseDoubleClick += OnMouseDoubleClick;
        }
    }

    private static void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var grid = (DataGrid)sender;
        for (var current = e.OriginalSource as DependencyObject; current is not null && current != grid; current = VisualTreeHelper.GetParent(current))
        {
            if (current is DataGridRow { Item: { } item } && GetCommand(grid) is { } command && command.CanExecute(item))
            {
                command.Execute(item);
                e.Handled = true;
                return;
            }
        }
    }
}
