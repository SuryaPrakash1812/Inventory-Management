using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using InventoryManagement.App.Services;
using InventoryManagement.App.Views;
using InventoryManagement.Application;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Settings;
using InventoryManagement.Infrastructure;
using InventoryManagement.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WpfApplication = System.Windows.Application;

namespace InventoryManagement.App;

/// <summary>
/// Composition root. Nothing here belongs to any feature - it only wires
/// hosting, configuration, logging, DI and global error handling together,
/// then hands control to the first window. Feature registration happens
/// inside each layer's own AddXxx() extension method (AddApplication,
/// AddInfrastructure, AddPresentation).
/// </summary>
public partial class App : WpfApplication
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Configure Serilog before anything else so startup failures are
        // captured even if DI construction itself fails.
        Log.Logger = SerilogConfigurator.CreateLogger();

        RegisterGlobalExceptionHandlers();

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
                    services.AddPresentation();
                })
                .Build();

            await _host.StartAsync();

            // IDatabaseInitializer depends on the (Scoped) InventoryDbContext,
            // so it must be resolved through an explicit scope rather than
            // straight from the root provider - the root provider only ever
            // holds singletons.
            using (var scope = _host.Services.CreateScope())
            {
                var databaseInitializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
                await databaseInitializer.InitializeAsync();
            }

            // Load persisted preferences and apply the theme before the first
            // window is shown, so there is no visible "flash" of the default
            // theme followed by a switch to the user's actual preference.
            var settingsService = _host.Services.GetRequiredService<ISettingsService>();
            await settingsService.LoadAsync();
            ThemeApplier.Apply(settingsService.Current.Theme);

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

    /// <summary>
    /// Registers last-resort handlers for the three places an unhandled
    /// exception can surface in a WPF app. Every path logs full details via
    /// Serilog (never shown to the user) and shows a short, friendly message
    /// instead of letting the process crash silently or with a raw dialog.
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        // Exceptions thrown on the UI thread during event handling.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Exceptions on any other thread (background work, timers, etc.).
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        // Exceptions from a Task that was never awaited/observed.
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled exception on the UI thread");

        MessageBox.Show(
            "Something went wrong and the action could not be completed. "
                + "The details have been logged. You can continue using the application.",
            "Unexpected Error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        // Prevent the default behavior (crashing the process) since the UI
        // thread is still in a recoverable state for most exceptions here.
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // ExceptionObject is typed as object because non-Exception throwables
        // are technically possible (e.g. from other languages), even though
        // that is exceedingly rare in a WPF/C# app.
        if (e.ExceptionObject is Exception exception)
        {
            Log.Fatal(exception, "Unhandled exception outside the UI thread (IsTerminating={IsTerminating})", e.IsTerminating);
        }
        else
        {
            Log.Fatal(
                "Unhandled non-Exception object outside the UI thread: {ExceptionObject} (IsTerminating={IsTerminating})",
                e.ExceptionObject,
                e.IsTerminating);
        }

        // If IsTerminating is true the CLR is already tearing the process
        // down and there is nothing more we can do beyond having logged it.
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");

        // Mark it observed so the finalizer thread does not escalate this
        // into a process crash on older .NET behavior.
        e.SetObserved();
    }
}
