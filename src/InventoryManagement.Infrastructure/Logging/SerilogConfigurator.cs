using Serilog;
using Serilog.Events;

namespace InventoryManagement.Infrastructure.Logging;

/// <summary>
/// Builds the application's Serilog logger. Kept as a single static entry point
/// so there is exactly one place that decides where logs go, how long they are
/// kept, and at what level - every other layer just injects ILogger&lt;T&gt;.
///
/// IMPORTANT: never log passwords, tokens, connection strings with credentials,
/// or full backup-encryption passwords. Log identifiers (user id, product id)
/// instead of raw sensitive values.
/// </summary>
public static class SerilogConfigurator
{
    /// <summary>
    /// Root folder for all application data (database, logs, backups), rooted
    /// under the current user's AppData so no admin rights are ever required.
    /// </summary>
    public static string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "InventoryManagement");

    public static string LogsFolder { get; } = Path.Combine(AppDataRoot, "logs");

    public static Serilog.ILogger CreateLogger()
    {
        Directory.CreateDirectory(LogsFolder);

        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .WriteTo.Debug(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(LogsFolder, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true,
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
