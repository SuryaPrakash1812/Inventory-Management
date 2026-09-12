using InventoryManagement.Application.Common.Interfaces;

namespace InventoryManagement.App.ViewModels;

/// <summary>
/// ViewModel for the main application shell. In Stage 1 this only proves the
/// wiring (DI -> ViewModel -> View, plus an injected Application-layer
/// abstraction). It will grow into real shell/navigation state as features
/// are added.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    public string WindowTitle => "Inventory Management";

    public string StartupMessage { get; }

    public MainViewModel(IDateTimeProvider dateTimeProvider)
    {
        StartupMessage = $"Foundation stage running. Started {dateTimeProvider.UtcNow:g} UTC.";
    }
}
