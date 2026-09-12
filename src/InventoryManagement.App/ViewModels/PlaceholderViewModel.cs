namespace InventoryManagement.App.ViewModels;

/// <summary>
/// Backing data for <see cref="Views.PlaceholderView"/>. Deliberately not an
/// ObservableObject - the values are fixed at construction and never change,
/// so change notification would be pure overhead.
/// </summary>
public sealed class PlaceholderViewModel
{
    public string Title { get; }
    public string Message { get; }

    public PlaceholderViewModel(string title, string message)
    {
        Title = title;
        Message = message;
    }
}
