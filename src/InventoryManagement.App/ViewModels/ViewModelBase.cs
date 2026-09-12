using CommunityToolkit.Mvvm.ComponentModel;

namespace InventoryManagement.App.ViewModels;

/// <summary>
/// Base class for every ViewModel in the application. Kept intentionally thin -
/// shared behaviour (e.g. a common IsBusy/IsLoading pattern) is added here only
/// once at least two ViewModels actually need it, to avoid speculative
/// abstraction.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;
}
