using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using InventoryManagement.App.Messages;
using InventoryManagement.App.Services;
using InventoryManagement.App.Views;
using InventoryManagement.App.Views.Auth;
using InventoryManagement.Application;
using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Settings;
using InventoryManagement.Infrastructure;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Logging;
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

            ApplyStagedRestoreIfPending();

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
                    services.AddInfrastructure();

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
            // Theme toggling is disabled - always Light, regardless of any
            // previously saved preference.
            ThemeApplier.Apply(ThemeMode.Light);

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
    /// Registers last-resort handlers for the three places an unhandled
    /// exception can surface in a WPF app. Every path logs full details via
    /// Serilog (never shown to the user) and shows a short, friendly message
    /// instead of letting the process crash silently or with a raw dialog.
    /// </summary>
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
    /// Applies a staged restore, if one exists, BEFORE any database
    /// connection is opened - this is what makes restore safe with this
    /// application's single long-lived connection per session. See
    /// BackupService.StageRestoreAsync's remarks: a restore never touches
    /// the live database file while the app is running, only writes a
    /// ".pending-restore" file alongside it. This runs at the very start
    /// of every launch, finds that file if the user requested a restore
    /// last session, and only then does the actual file swap - at a point
    /// where nothing else has the database open yet, making the swap
    /// completely safe.
    /// </summary>
    private static void ApplyStagedRestoreIfPending()
    {
        var stagedPath = AppPaths.DatabaseFilePath + Infrastructure.Backup.BackupService.PendingRestoreSuffix;

        if (!File.Exists(stagedPath))
        {
            return;
        }

        try
        {
            Log.Information("Pending restore found at {StagedPath} - applying before opening the database", stagedPath);

            // SQLite WAL mode leaves -wal/-shm sidecar files alongside the
            // main database file - these must be removed too, or the
            // restored file could be reconciled against stale WAL data
            // from before the restore, defeating the point of restoring.
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var path = AppPaths.DatabaseFilePath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            File.Move(stagedPath, AppPaths.DatabaseFilePath);

            Log.Information("Restore applied successfully");
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
