using InventoryManagement.Core.Common;

namespace InventoryManagement.Application.Users;

public interface IRoleManagementService
{
    Task<IReadOnlyList<PermissionInfo>> GetAllPermissionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a new role (RoleId null) or updates an existing one's name/description/permissions.</summary>
    Task<Result<RoleSummary>> SaveRoleAsync(SaveRoleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fails with a clear error if any user is still assigned to the role - reassign them first.</summary>
    Task<Result> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
}
