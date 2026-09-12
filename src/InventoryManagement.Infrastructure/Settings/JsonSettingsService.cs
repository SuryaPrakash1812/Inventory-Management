using System.Text.Json;
using InventoryManagement.Application.Settings;
using InventoryManagement.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace InventoryManagement.Infrastructure.Settings;

/// <summary>
/// Persists <see cref="AppSettings"/> as a single JSON file under the app's
/// AppData folder. Writes go to a temp file first and are then moved into
/// place, so a crash or power loss mid-write can never leave a half-written,
/// unreadable settings file behind.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ILogger<JsonSettingsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _settingsFilePath;
    private readonly string _settingsDirectory;

    public AppSettings Current { get; private set; } = new();

    public JsonSettingsService(ILogger<JsonSettingsService> logger)
        : this(logger, AppPaths.AppDataRoot)
    {
    }

    /// <summary>
    /// Allows pointing the service at a custom directory (used by tests so they
    /// never touch the real user's AppData folder). Production code should use
    /// the single-argument constructor, which DI resolves automatically.
    /// </summary>
    public JsonSettingsService(ILogger<JsonSettingsService> logger, string settingsDirectory)
    {
        _logger = logger;
        _settingsFilePath = Path.Combine(settingsDirectory, "settings.json");
        _settingsDirectory = settingsDirectory;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                Current = new AppSettings();
                return;
            }

            await using var stream = File.OpenRead(_settingsFilePath);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream, SerializerOptions, cancellationToken);

            Current = loaded ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // A corrupt or unreadable settings file must never prevent the
            // app from starting - fall back to defaults and keep going.
            _logger.LogWarning(ex, "Failed to load settings from {Path}; using defaults", _settingsFilePath);
            Current = new AppSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_settingsDirectory);

            var tempFilePath = _settingsFilePath + ".tmp";

            await using (var stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(stream, Current, SerializerOptions, cancellationToken);
            }

            File.Move(tempFilePath, _settingsFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", _settingsFilePath);
        }
        finally
        {
            _lock.Release();
        }
    }
}
