using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Core.Common;

namespace InventoryManagement.Application.Customers;

public interface ICustomerService
{
    Task<PagedResult<CustomerSummary>> GetCustomersAsync(
        CustomerQueryParameters query, CancellationToken cancellationToken = default);

    Task<CustomerDetail?> GetCustomerByIdAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<Result<CustomerSummary>> CreateCustomerAsync(
        CreateCustomerRequest request, CancellationToken cancellationToken = default);

    Task<Result<CustomerSummary>> UpdateCustomerAsync(
        UpdateCustomerRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeactivateCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditLogEntry>> GetHistoryAsync(Guid customerId, CancellationToken cancellationToken = default);
}
