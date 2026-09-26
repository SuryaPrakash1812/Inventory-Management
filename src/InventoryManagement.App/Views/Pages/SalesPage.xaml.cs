using System.Windows.Controls;
using System.Windows.Input;
using InventoryManagement.App.ViewModels.Sales;

namespace InventoryManagement.App.Views.Pages;

public partial class SalesPage : Page
{
    private readonly SalesViewModel _viewModel;

    public SalesPage(SalesViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();

        PreviewMouseWheel += OnPreviewMouseWheel;

        InputBindings.Add(new KeyBinding(
            _viewModel.SaveDraftCommand, new KeyGesture(Key.S, ModifierKeys.Control)));
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = _viewModel.IsViewingDetail ? DetailScrollViewer : ListScrollViewer;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
