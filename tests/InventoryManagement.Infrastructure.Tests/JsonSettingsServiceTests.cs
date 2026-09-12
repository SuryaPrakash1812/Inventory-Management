using InventoryManagement.Application.Settings;
using InventoryManagement.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests;

public class JsonSettingsServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public JsonSettingsServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "InventoryManagementTests_" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private JsonSettingsService CreateSut() =>
        new(NullLogger<JsonSettingsService>.Instance, _tempDirectory);

    [Fact]
    public async Task LoadAsync_WhenNoFileExists_FallsBackToDefaults()
    {
        var sut = CreateSut();

        await sut.LoadAsync();

        Assert.Equal(ThemeMode.System, sut.Current.Theme);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsTheSameValue()
    {
        var sut = CreateSut();
        sut.Current.Theme = ThemeMode.Dark;

        await sut.SaveAsync();

        var reloaded = CreateSut();
        await reloaded.LoadAsync();

        Assert.Equal(ThemeMode.Dark, reloaded.Current.Theme);
    }

    [Fact]
    public async Task LoadAsync_WhenFileIsCorrupt_FallsBackToDefaultsWithoutThrowing()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "settings.json"), "{ not valid json");

        var sut = CreateSut();
        await sut.LoadAsync();

        Assert.Equal(ThemeMode.System, sut.Current.Theme);
    }
}
