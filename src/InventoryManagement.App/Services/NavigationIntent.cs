namespace InventoryManagement.App.Services;

/// <summary>
/// Carries "what to do on arrival" from the shell to the page it is about to
/// show: pages and their ViewModels are created fresh on every navigation,
/// so there is no existing instance to hand a search term to. The shell sets
/// the intent just before navigating; the target page's ViewModel takes it
/// (once) in its InitializeAsync. Singleton.
/// </summary>
public sealed class NavigationIntent
{
    private Type? _pageType;
    private string? _searchText;
    private bool _createNew;

    public void Set(Type pageType, string? searchText, bool createNew)
    {
        _pageType = pageType;
        _searchText = searchText;
        _createNew = createNew;
    }

    /// <summary>Returns and clears the intent if it was meant for <paramref name="pageType"/>.</summary>
    public bool TryTake(Type pageType, out string? searchText, out bool createNew)
    {
        if (_pageType != pageType)
        {
            searchText = null;
            createNew = false;
            return false;
        }

        searchText = _searchText;
        createNew = _createNew;
        _pageType = null;
        _searchText = null;
        _createNew = false;
        return true;
    }
}
