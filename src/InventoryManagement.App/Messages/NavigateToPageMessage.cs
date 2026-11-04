namespace InventoryManagement.App.Messages;

/// <summary>
/// Asks the shell to switch to another page - used by pages (e.g. the
/// Dashboard's "View All" links) and the global search, none of which have
/// access to the shell's navigation frame. <paramref name="SearchText"/>
/// pre-fills the target page's search box; <paramref name="CreateNew"/>
/// opens its "new record" form. See NavigationIntent for how the target page
/// picks these up.
/// </summary>
public sealed record NavigateToPageMessage(Type PageType, string? SearchText = null, bool CreateNew = false);
