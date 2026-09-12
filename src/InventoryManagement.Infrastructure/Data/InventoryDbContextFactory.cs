using InventoryManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InventoryManagement.Infrastructure.Data;

/// <summary>
/// Lets EF Core's design-time tooling (`dotnet ef migrations add`, etc.)
/// construct an <see cref="InventoryDbContext"/> on its own. Without this,
/// `dotnet ef` would have to discover the DbContext by finding and running
/// the app's own startup code - but our WPF app builds its host inside
/// App.xaml.cs's OnStartup, not a conventional static Program.CreateHostBuilder
/// the tooling can find by reflection. Implementing this well-known interface
/// sidesteps that entirely: the CLI finds this factory instead.
/// </summary>
public sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>();
        optionsBuilder.UseSqlite($"Data Source={AppPaths.DatabaseFilePath}");

        return new InventoryDbContext(optionsBuilder.Options);
    }
}
