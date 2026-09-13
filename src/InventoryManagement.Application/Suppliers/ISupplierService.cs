using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Core.Common;

namespace InventoryManagement.Application.Suppliers;

public interface ISupplierService
{
    Task<PagedResult<SupplierSummary>> GetSuppliersAsync(
        SupplierQueryParameters query, CancellationToken cancellationToken = default);

    Task<SupplierDetail?> GetSupplierByIdAsync(Guid supplierId, CancellationToken cancellationToken = default);

    Task<Result<SupplierSummary>> CreateSupplierAsync(
        CreateSupplierRequest request, CancellationToken cancellationToken = default);

    Task<Result<SupplierSummary>> UpdateSupplierAsync(
        UpdateSupplierRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeactivateSupplierAsync(Guid supplierId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditLogEntry>> GetHistoryAsync(Guid supplierId, CancellationToken cancellationToken = default);
}
