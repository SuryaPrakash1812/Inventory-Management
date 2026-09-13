using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Customers;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Customers;

public sealed class CustomerService : ICustomerService
{
    private readonly IAppDbContext _context;
    private readonly IAuditLogger _auditLogger;

    public CustomerService(IAppDbContext context, IAuditLogger auditLogger)
    {
        _context = context;
        _auditLogger = auditLogger;
    }

    public async Task<PagedResult<CustomerSummary>> GetCustomersAsync(
        CustomerQueryParameters query, CancellationToken cancellationToken = default)
    {
        var filtered = BuildFilteredQuery(query);

        var totalCount = await filtered.CountAsync(cancellationToken);

        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = query.PageSize <= 0 ? 25 : query.PageSize;

        var sorted = query.SortDescending
            ? filtered.OrderByDescending(c => c.Name)
            : filtered.OrderBy(c => c.Name);

        var items = await sorted
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerSummary(c.Id, c.Name, c.Phone, c.Email, c.TaxId, c.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<CustomerSummary>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<CustomerDetail?> GetCustomerByIdAsync(
        Guid customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Customers
            .Where(c => c.Id == customerId)
            .Select(c => new CustomerDetail(
                c.Id, c.Name, c.ContactPerson, c.Phone, c.Email, c.Address, c.TaxId, c.Notes, c.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Result<CustomerSummary>> CreateCustomerAsync(
        CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var validationError = Validate(request.Name, request.Email);
        if (validationError is not null)
        {
            return Result.Failure<CustomerSummary>(validationError);
        }

        var customer = new Customer
        {
            Name = request.Name.Trim(),
            ContactPerson = request.ContactPerson,
            Phone = request.Phone,
            Email = request.Email,
            Address = request.Address,
            TaxId = request.TaxId,
            Notes = request.Notes,
            IsActive = true,
        };

        _context.Customers.Add(customer);

        await _auditLogger.LogAsync(
            AuditAction.Created, nameof(Customer), customer.Id, $"Customer '{customer.Name}' created.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToSummaryAsync(customer.Id, cancellationToken));
    }

    public async Task<Result<CustomerSummary>> UpdateCustomerAsync(
        UpdateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers
            .SingleOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
        if (customer is null)
        {
            return Result.Failure<CustomerSummary>("Customer not found.");
        }

        var validationError = Validate(request.Name, request.Email);
        if (validationError is not null)
        {
            return Result.Failure<CustomerSummary>(validationError);
        }

        customer.Name = request.Name.Trim();
        customer.ContactPerson = request.ContactPerson;
        customer.Phone = request.Phone;
        customer.Email = request.Email;
        customer.Address = request.Address;
        customer.TaxId = request.TaxId;
        customer.Notes = request.Notes;
        customer.IsActive = request.IsActive;

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(Customer), customer.Id, $"Customer '{customer.Name}' updated.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToSummaryAsync(customer.Id, cancellationToken));
    }

    public async Task<Result> DeactivateCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers.SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
        {
            return Result.Failure("Customer not found.");
        }

        customer.IsActive = false;

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(Customer), customer.Id, $"Customer '{customer.Name}' deactivated.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetHistoryAsync(
        Guid customerId, CancellationToken cancellationToken = default)
    {
        // See SupplierService.GetHistoryAsync remarks - SQLite cannot
        // translate ORDER BY on a DateTimeOffset column, so this sorts
        // in-memory over what is always a small per-record result set.
        var entries = await _context.AuditLogs
            .Where(a => a.EntityName == nameof(Customer) && a.EntityId == customerId)
            .Select(a => new AuditLogEntry(a.OccurredAtUtc, a.Action, a.Details))
            .ToListAsync(cancellationToken);

        return entries.OrderByDescending(e => e.OccurredAtUtc).ToList();
    }

    private IQueryable<Customer> BuildFilteredQuery(CustomerQueryParameters query)
    {
        var customers = _context.Customers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            customers = customers.Where(c =>
                c.Name.Contains(term)
                || (c.Phone != null && c.Phone.Contains(term))
                || (c.Email != null && c.Email.Contains(term)));
        }

        if (query.IsActive is { } isActive)
        {
            customers = customers.Where(c => c.IsActive == isActive);
        }

        return customers;
    }

    private static string? Validate(string name, string? email)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name is required.";
        }

        if (!string.IsNullOrWhiteSpace(email) && !ContactValidation.LooksLikeValidEmail(email))
        {
            return "Email address doesn't look valid.";
        }

        return null;
    }

    private async Task<CustomerSummary> ToSummaryAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return await _context.Customers
            .Where(c => c.Id == customerId)
            .Select(c => new CustomerSummary(c.Id, c.Name, c.Phone, c.Email, c.TaxId, c.IsActive))
            .SingleAsync(cancellationToken);
    }
}
