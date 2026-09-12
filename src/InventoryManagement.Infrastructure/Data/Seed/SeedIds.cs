namespace InventoryManagement.Infrastructure.Data.Seed;

/// <summary>
/// Well-known ids for the reference data seeded via EF Core's HasData
/// (Permissions and the two starter Roles). These must be treated as
/// immutable constants once released - HasData seeding works by diffing
/// against these exact keys in migrations, so changing a value here would
/// look like "delete this row, insert a different one" to EF Core.
/// </summary>
internal static class SeedIds
{
    public static class Permissions
    {
        public static readonly Guid DashboardView = new("11111111-1111-1111-1111-000000000001");
        public static readonly Guid ProductsView = new("11111111-1111-1111-1111-000000000002");
        public static readonly Guid ProductsCreate = new("11111111-1111-1111-1111-000000000003");
        public static readonly Guid ProductsEdit = new("11111111-1111-1111-1111-000000000004");
        public static readonly Guid ProductsDelete = new("11111111-1111-1111-1111-000000000005");
        public static readonly Guid PurchasesView = new("11111111-1111-1111-1111-000000000006");
        public static readonly Guid PurchasesCreate = new("11111111-1111-1111-1111-000000000007");
        public static readonly Guid PurchasesEdit = new("11111111-1111-1111-1111-000000000008");
        public static readonly Guid SalesView = new("11111111-1111-1111-1111-000000000009");
        public static readonly Guid SalesCreate = new("11111111-1111-1111-1111-000000000010");
        public static readonly Guid SalesEdit = new("11111111-1111-1111-1111-000000000011");
        public static readonly Guid InventoryView = new("11111111-1111-1111-1111-000000000012");
        public static readonly Guid InventoryAdjust = new("11111111-1111-1111-1111-000000000013");
        public static readonly Guid ReportsView = new("11111111-1111-1111-1111-000000000014");
        public static readonly Guid UsersView = new("11111111-1111-1111-1111-000000000015");
        public static readonly Guid UsersManage = new("11111111-1111-1111-1111-000000000016");
        public static readonly Guid BackupCreate = new("11111111-1111-1111-1111-000000000017");
        public static readonly Guid BackupRestore = new("11111111-1111-1111-1111-000000000018");
        public static readonly Guid SettingsManage = new("11111111-1111-1111-1111-000000000019");
    }

    public static class Roles
    {
        public static readonly Guid Administrator = new("22222222-2222-2222-2222-000000000001");
        public static readonly Guid StandardUser = new("22222222-2222-2222-2222-000000000002");
    }
}
