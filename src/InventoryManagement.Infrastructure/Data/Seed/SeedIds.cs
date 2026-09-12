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
        public static readonly Guid ProductsView = new("11111111-1111-1111-1111-000000000001");
        public static readonly Guid ProductsManage = new("11111111-1111-1111-1111-000000000002");
        public static readonly Guid CategoriesManage = new("11111111-1111-1111-1111-000000000003");
        public static readonly Guid SuppliersManage = new("11111111-1111-1111-1111-000000000004");
        public static readonly Guid CustomersManage = new("11111111-1111-1111-1111-000000000005");
        public static readonly Guid PurchasesView = new("11111111-1111-1111-1111-000000000006");
        public static readonly Guid PurchasesManage = new("11111111-1111-1111-1111-000000000007");
        public static readonly Guid SalesView = new("11111111-1111-1111-1111-000000000008");
        public static readonly Guid SalesManage = new("11111111-1111-1111-1111-000000000009");
        public static readonly Guid InventoryManage = new("11111111-1111-1111-1111-000000000010");
        public static readonly Guid ReportsView = new("11111111-1111-1111-1111-000000000011");
        public static readonly Guid UsersManage = new("11111111-1111-1111-1111-000000000012");
        public static readonly Guid SettingsManage = new("11111111-1111-1111-1111-000000000013");
        public static readonly Guid BackupManage = new("11111111-1111-1111-1111-000000000014");
    }

    public static class Roles
    {
        public static readonly Guid Administrator = new("22222222-2222-2222-2222-000000000001");
        public static readonly Guid StandardUser = new("22222222-2222-2222-2222-000000000002");
    }
}
