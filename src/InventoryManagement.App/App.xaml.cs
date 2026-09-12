using System.Windows;
using InventoryManagement.App.ViewModels;
using InventoryManagement.App.Views;
using InventoryManagement.Application;
using InventoryManagement.Infrastructure;
using InventoryManagement.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace InventoryManagement.App;

/// <summary>
/// Composition root. Nothing here belongs to any feature - it only wires
/// hosting, configuration, logging and DI together and hands control to the
/// first window. Feature registration happens inside each layer's own
/// AddXxx() extension method (AddApplication, AddInfrastructure, ...).
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Configure Serilog before anything else so startup failures are
        // captured even if DI construction itself fails.
        Log.Logger = SerilogConfigurator.CreateLogger();

        try
        {
            Log.Information("Application starting up");

            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureAppConfiguration(config =>
                {
                    config.SetBasePath(AppContext.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                })
                .ConfigureServices((_, services) =>
                {
                    services.AddApplication();
                    services.AddInfrastructure();

                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<MainViewModel>();
                })
                .Build();

            await _host.StartAsync();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application failed to start");

            MessageBox.Show(
                "Inventory Management failed to start. Check the log file in "
                    + SerilogConfigurator.LogsFolder + " for details.",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();

        base.OnExit(e);
    }
}
