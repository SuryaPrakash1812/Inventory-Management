using System.Windows.Controls;
using System.Windows.Input;
using InventoryManagement.App.ViewModels.Purchases;

namespace InventoryManagement.App.Views.Pages;

public partial class PurchasesPage : Page
{
    private readonly PurchasesViewModel _viewModel;

    public PurchasesPage(PurchasesViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();

        PreviewMouseWheel += OnPreviewMouseWheel;

        // Ctrl+S saves the current draft - useful when entering a long list
        // of items and wanting to save progress without reaching for the mouse.
        InputBindings.Add(new KeyBinding(
            _viewModel.SaveDraftCommand, new KeyGesture(Key.S, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(_viewModel.Dialog.DismissCommand, new KeyGesture(Key.Escape)));
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // The dialog scrolls its own list; the page behind it stays put.
        if (_viewModel.Dialog.IsOpen)
        {
            return;
        }

        var scrollViewer = _viewModel.IsViewingDetail ? DetailScrollViewer : ListScrollViewer;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
