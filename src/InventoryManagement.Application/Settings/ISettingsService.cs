namespace InventoryManagement.Application.Settings;

/// <summary>
/// Loads and persists <see cref="AppSettings"/>. Implementations decide where
/// settings live (local JSON file, registry, etc.) - callers only ever see
/// this abstraction, so the storage mechanism can change later without
/// touching any ViewModel.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// The current in-memory settings. Populated by <see cref="LoadAsync"/>;
    /// before that it holds defaults so the app always has something usable.
    /// </summary>
    AppSettings Current { get; }

    /// <summary>Loads settings from storage, falling back to defaults if none exist or the file is corrupt.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the current settings to storage.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
