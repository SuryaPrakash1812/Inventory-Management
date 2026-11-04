using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using InventoryManagement.App.Messages;
using InventoryManagement.App.Services;
using InventoryManagement.App.Views;
using InventoryManagement.App.ViewModels.Licensing;
using InventoryManagement.App.Views.Auth;
using InventoryManagement.App.Views.Licensing;
using InventoryManagement.Application;
using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Settings;
using InventoryManagement.Infrastructure;
using InventoryManagement.Infrastructure.Backup;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Licensing;
using InventoryManagement.Infrastructure.Logging;
using InventoryManagement.Infrastructure.Security;
using InventoryManagement.Licensing;
using InventoryManagement.Sync;
using InventoryManagement.Sync.Connectivity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WpfApplication = System.Windows.Application;

namespace InventoryManagement.App;

/// <summary>
/// Composition root. Nothing here belongs to any feature - it only wires
/// hosting, configuration, logging, DI, global error handling, and the
/// login/logout window flow together. Feature registration happens inside
/// each layer's own AddXxx() extension method (AddApplication,
/// AddInfrastructure, AddPresentation).
///
/// WINDOW LIFECYCLE: ShutdownMode is OnExplicitShutdown (set below) rather
/// than the default OnMainWindowClose, because "the main window closes" is
/// ambiguous here - it happens both when the user actually wants to exit
/// AND when we swap from the login window to the shell (or back, on
/// logout). <see cref="TransitionTo"/> is the single place that tracks
/// which window is "really" current; a window's Closed event only means
/// "the user clicked X" when it fires for whichever window that field still
/// points to at the time - so genuine user-initiated closes fall through to
/// Shutdown(), while our own programmatic transitions don't.
/// </summary>
public partial class App : WpfApplication
{
    private IHost? _host;
    private IServiceScope? _appScope;
    private Window? _currentWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Configure Serilog before anything else so startup failures are
        // captured even if DI construction itself fails.
        Log.Logger = SerilogConfigurator.CreateLogger();

        RegisterGlobalExceptionHandlers();

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            Log.Information("Application starting up");

            // Nothing - not even the database - is touched until this PC
            // holds a valid license issued for it.
            var license = EnsureLicensed();
            if (license is null)
            {
                Log.Information("No valid license and activation was cancelled - exiting");
                Shutdown();
                return;
            }

            ApplyStagedRestoreIfPending();

            var database = await PrepareLocalDatabaseAsync();
            if (database is null)
            {
                Shutdown();
                return;
            }

            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureAppConfiguration(config =>
                {
                    config.SetBasePath(AppContext.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddApplication();
                    services.AddInfrastructure(database, license);

                    // Registered AFTER AddInfrastructure deliberately -
                    // Microsoft.Extensions.DependencyInjection resolves the
                    // LAST registration for a given service type, and
                    // AddSync overrides IPurchaseService to route through
                    // the API when online (see AddSync's own remarks).
                    // Reading Api:BaseAddress from configuration rather than
                    // hardcoding it, since the API's actual port varies
                    // (Visual Studio's auto-generated launchSettings.json
                    // for the Api project, not something to assume a fixed
                    // value for).
                    var apiBaseAddress = context.Configuration["Api:BaseAddress"];
                    if (!string.IsNullOrWhiteSpace(apiBaseAddress))
                    {
                        services.AddSync(new Uri(apiBaseAddress));
                    }
                    else
                    {
                        Log.Warning(
                            "Api:BaseAddress is not configured - online mode is disabled for this run; " +
                            "every Purchase operation will use local SQLite only, exactly as before Sync existed.");
                    }

                    services.AddPresentation();
                })
                .Build();

            await _host.StartAsync();

            // One scope for the whole app run (see DependencyInjection.cs remarks
            // on why: InventoryDbContext is Scoped, but a desktop app has no
            // natural per-request boundary the way a web app does).
            _appScope = _host.Services.CreateScope();

            var databaseInitializer = _appScope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            await databaseInitializer.InitializeAsync();

            TryWireAutomaticSyncOnReconnect();

            // Load persisted preferences and apply the theme before the first
            // window is shown, so there is no visible "flash" of the default
            // theme followed by a switch to the user's actual preference.
            var settingsService = _appScope.ServiceProvider.GetRequiredService<ISettingsService>();
            await settingsService.LoadAsync();

            // Light only: the shell and dashboard use a fixed light palette
            // (Styles/AppTheme.xaml), so a saved Dark/System preference is ignored.
            ThemeApplier.Apply(ThemeMode.Light);

            // Automatic backups run whether or not anyone is signed in.
            _host.Services.GetRequiredService<AutomaticBackupScheduler>().Start();

            WeakReferenceMessenger.Default.Register<LogoutRequestedMessage>(this, OnLogoutRequested);

            TransitionTo(BuildLoginWindow());

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

        _appScope?.Dispose();

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();

        base.OnExit(e);
    }

    private LoginWindow BuildLoginWindow()
    {
        var loginWindow = _appScope!.ServiceProvider.GetRequiredService<LoginWindow>();

        loginWindow.ViewModel.SignedIn += (_, _) => TransitionTo(BuildMainWindow());

        return loginWindow;
    }

    private MainWindow BuildMainWindow() => _appScope!.ServiceProvider.GetRequiredService<MainWindow>();

    private async void OnLogoutRequested(object recipient, LogoutRequestedMessage message)
    {
        var authenticationService = _appScope!.ServiceProvider.GetRequiredService<IAuthenticationService>();
        await authenticationService.LogoutAsync();

        TransitionTo(BuildLoginWindow());
    }

    /// <summary>
    /// Shows <paramref name="newWindow"/> and closes whichever window was
    /// current before it. Order matters: <see cref="_currentWindow"/> is
    /// updated to point at the new window *before* the old one is closed, so
    /// the old window's Closed handler (registered below) sees that it is no
    /// longer "the current window" and correctly does nothing, instead of
    /// mistaking this transition for the user wanting to exit.
    /// </summary>
    private void TransitionTo(Window newWindow)
    {
        var oldWindow = _currentWindow;
        _currentWindow = newWindow;

        newWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_currentWindow, newWindow))
            {
                // Still "current" when it closed - this was a genuine
                // user-initiated close (the title bar X button), not one of
                // our own login/logout transitions. Time to exit for real.
                Shutdown();
            }
        };

        newWindow.Show();
        oldWindow?.Close();
    }

    /// <summary>
    /// Validates the installed license against this PC's Machine ID and, if
    /// there is none (or it was issued for another PC), shows the activation
    /// window until the user loads a valid one or exits. Returns null if they
    /// exit. Copying the program folder, the AppData folder or the license
    /// file to another PC ends up here on that PC, because its Machine ID
    /// differs from the one the license was signed for.
    /// </summary>
    private static ActiveLicense? EnsureLicensed()
    {
        var store = new LicenseStore(AppPaths.LicenseFilePath, new LicenseValidator(), MachineIdentity.GetCurrentMachineId());

        var result = store.ValidateInstalled();
        if (result.IsValid)
        {
            Log.Information("Licensed to {Customer} ({CustomerId}), license {LicenseId}",
                result.License!.CustomerName, result.License.CustomerId, result.License.LicenseId);
            return new ActiveLicense(result.License);
        }

        Log.Warning("No valid license on this PC: {Reason}", result.Error);

        var window = new ActivationWindow(new ActivationViewModel(store, result.Error));
        if (window.ShowDialog() != true || window.ViewModel.ActivatedLicense is null)
        {
            return null;
        }

        Log.Information("Activated for {Customer} ({CustomerId})",
            window.ViewModel.ActivatedLicense.CustomerName, window.ViewModel.ActivatedLicense.CustomerId);
        return new ActiveLicense(window.ViewModel.ActivatedLicense);
    }

    /// <summary>
    /// Unlocks (and, on the first run after upgrading, encrypts) the local
    /// database. If the data on disk cannot be unlocked on this PC - it was
    /// copied from another PC, or its key file was deleted - nobody can read
    /// it, so the user is offered a fresh start: the unreadable files are
    /// moved aside (never deleted) and an empty database is created, into
    /// which they can then restore one of their backups. Returns null if
    /// they decline.
    /// </summary>
    private static async Task<LocalDatabaseOptions?> PrepareLocalDatabaseAsync()
    {
        var keyProvider = new DatabaseKeyProvider(AppPaths.DatabaseKeyFilePath);

        try
        {
            return await LocalDatabaseBootstrapper.PrepareAsync(AppPaths.DatabaseFilePath, keyProvider);
        }
        catch (DatabaseKeyUnavailableException ex)
        {
            Log.Error(ex, "Local database cannot be unlocked on this PC");

            var answer = MessageBox.Show(
                "The inventory data on this PC cannot be opened. This happens when the data files were copied "
                    + "from another PC, or the database key file was deleted.\n\n"
                    + "Start with an empty database? The unreadable files will be moved to a separate folder, "
                    + "not deleted. You can then restore one of your backups from the Backup page.",
                "Inventory Management",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                return null;
            }

            var quarantine = Path.Combine(AppPaths.AppDataRoot, $"unreadable-data-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(quarantine);
            foreach (var path in new[] { AppPaths.DatabaseFilePath, AppPaths.DatabaseFilePath + "-wal", AppPaths.DatabaseFilePath + "-shm", AppPaths.DatabaseKeyFilePath })
            {
                if (File.Exists(path))
                {
                    File.Move(path, Path.Combine(quarantine, Path.GetFileName(path)));
                }
            }

            Log.Warning("Unreadable data moved to {Folder}; starting with an empty database", quarantine);
            return await LocalDatabaseBootstrapper.PrepareAsync(AppPaths.DatabaseFilePath, keyProvider);
        }
    }

    /// <summary>
    /// The RECONNECT half of online/offline support: without this,
    /// pending Outbox operations would only ever be sent by manually
    /// calling ISyncEngine.SyncPendingOperationsAsync() from somewhere -
    /// nothing did, anywhere in the app, before this. Subscribes once, for
    /// the whole app run, to IConnectivityService.ConnectivityChanged and
    /// triggers a background sync attempt whenever it fires true (came
    /// online) - fire-and-forget on purpose, so the UI thread is never
    /// blocked waiting for synchronization (explicit requirement: "the UI
    /// must remain responsive while synchronization happens").
    ///
    /// A no-op if Api:BaseAddress was not configured (see
    /// ConfigureServices above) - IConnectivityService/ISyncEngine are
    /// simply not registered in that case, so GetService returns null
    /// rather than GetRequiredService throwing.
    /// </summary>
    private void TryWireAutomaticSyncOnReconnect()
    {
        var connectivity = _appScope!.ServiceProvider.GetService<IConnectivityService>();
        var syncEngine = _appScope.ServiceProvider.GetService<ISyncEngine>();

        if (connectivity is null || syncEngine is null)
        {
            return;
        }

        connectivity.ConnectivityChanged += (_, isOnline) =>
        {
            if (!isOnline)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var summary = await syncEngine.SyncPendingOperationsAsync();
                    Log.Information(
                        "Sync run after reconnect: {Attempted} attempted, {Succeeded} succeeded, "
                            + "{Failed} failed, {Skipped} skipped (no server handler yet)",
                        summary.Attempted, summary.Succeeded, summary.Failed, summary.Skipped);
                }
                catch (Exception ex)
                {
                    // A failed sync attempt must never crash the app -
                    // it just tries again on the next reconnect event (or
                    // the ConnectivityService's own periodic backstop
                    // check firing ConnectivityChanged again).
                    Log.Warning(ex, "Sync run after reconnect failed");
                }
            });
        };
    }

    /// <summary>
    /// Swaps in a restore the user staged last session - see
    /// <see cref="StagedRestore"/> for why this must happen before any
    /// database connection is opened.
    /// </summary>
    private static void ApplyStagedRestoreIfPending()
    {
        try
        {
            if (StagedRestore.ApplyIfPending(AppPaths.DatabaseFilePath))
            {
                Log.Information("Staged restore applied; the previous database was kept as inventory.before-restore.db");
            }
        }
        catch (Exception ex)
        {
            // A failed restore must not prevent the app from starting at
            // all - log it clearly and leave the staged file in place so
            // the user can see something is wrong and try again, rather
            // than silently losing their restore request.
            Log.Error(ex, "Failed to apply staged restore - the application will start with the existing database");
        }
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
