using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Suppliers;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Suppliers;

public sealed class SupplierService : ISupplierService
{
    private readonly IAppDbContext _context;
    private readonly IAuditLogger _auditLogger;

    public SupplierService(IAppDbContext context, IAuditLogger auditLogger)
    {
        _context = context;
        _auditLogger = auditLogger;
    }

    public async Task<PagedResult<SupplierSummary>> GetSuppliersAsync(
        SupplierQueryParameters query, CancellationToken cancellationToken = default)
    {
        var filtered = BuildFilteredQuery(query);

        var totalCount = await filtered.CountAsync(cancellationToken);

        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = query.PageSize <= 0 ? 25 : query.PageSize;

        var sorted = query.SortDescending
            ? filtered.OrderByDescending(s => s.Name)
            : filtered.OrderBy(s => s.Name);

        var items = await sorted
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SupplierSummary(s.Id, s.Name, s.ContactPerson, s.Phone, s.Email, s.TaxId, s.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<SupplierSummary>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<SupplierDetail?> GetSupplierByIdAsync(
        Guid supplierId, CancellationToken cancellationToken = default)
    {
        return await _context.Suppliers
            .Where(s => s.Id == supplierId)
            .Select(s => new SupplierDetail(
                s.Id, s.Name, s.ContactPerson, s.Phone, s.Email, s.Address, s.TaxId, s.Notes, s.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Result<SupplierSummary>> CreateSupplierAsync(
        CreateSupplierRequest request, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var validationError = Validate(request.Name, request.Email);
        if (validationError is not null)
        {
            return Result.Failure<SupplierSummary>(validationError);
        }

        var supplier = new Supplier
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

        _context.Suppliers.Add(supplier);

        await _auditLogger.LogAsync(
            AuditAction.Created, nameof(Supplier), supplier.Id, $"Supplier '{supplier.Name}' created.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToSummaryAsync(supplier.Id, cancellationToken));
    }

    public async Task<Result<SupplierSummary>> UpdateSupplierAsync(
        UpdateSupplierRequest request, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var supplier = await _context.Suppliers
            .SingleOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return Result.Failure<SupplierSummary>("Supplier not found.");
        }

        var validationError = Validate(request.Name, request.Email);
        if (validationError is not null)
        {
            return Result.Failure<SupplierSummary>(validationError);
        }

        supplier.Name = request.Name.Trim();
        supplier.ContactPerson = request.ContactPerson;
        supplier.Phone = request.Phone;
        supplier.Email = request.Email;
        supplier.Address = request.Address;
        supplier.TaxId = request.TaxId;
        supplier.Notes = request.Notes;
        supplier.IsActive = request.IsActive;

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(Supplier), supplier.Id, $"Supplier '{supplier.Name}' updated.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToSummaryAsync(supplier.Id, cancellationToken));
    }

    public async Task<Result> DeactivateSupplierAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var supplier = await _context.Suppliers.SingleOrDefaultAsync(s => s.Id == supplierId, cancellationToken);
        if (supplier is null)
        {
            return Result.Failure("Supplier not found.");
        }

        supplier.IsActive = false;

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(Supplier), supplier.Id, $"Supplier '{supplier.Name}' deactivated.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetHistoryAsync(
        Guid supplierId, CancellationToken cancellationToken = default)
    {
        // SQLite cannot translate ORDER BY on a DateTimeOffset column - see
        // ProductService.ApplySort remarks for the same limitation. Audit
        // history for a single record is small, so an in-memory sort here
        // is cheap and safe.
        var entries = await _context.AuditLogs
            .Where(a => a.EntityName == nameof(Supplier) && a.EntityId == supplierId)
            .Select(a => new AuditLogEntry(a.OccurredAtUtc, a.Action, a.Details))
            .ToListAsync(cancellationToken);

        return entries.OrderByDescending(e => e.OccurredAtUtc).ToList();
    }

    private IQueryable<Supplier> BuildFilteredQuery(SupplierQueryParameters query)
    {
        var suppliers = _context.Suppliers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            suppliers = suppliers.Where(s =>
                s.Name.Contains(term)
                || (s.ContactPerson != null && s.ContactPerson.Contains(term))
                || (s.Phone != null && s.Phone.Contains(term))
                || (s.Email != null && s.Email.Contains(term)));
        }

        if (query.IsActive is { } isActive)
        {
            suppliers = suppliers.Where(s => s.IsActive == isActive);
        }

        return suppliers;
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

    private async Task<SupplierSummary> ToSummaryAsync(Guid supplierId, CancellationToken cancellationToken)
    {
        return await _context.Suppliers
            .Where(s => s.Id == supplierId)
            .Select(s => new SupplierSummary(s.Id, s.Name, s.ContactPerson, s.Phone, s.Email, s.TaxId, s.IsActive))
            .SingleAsync(cancellationToken);
    }
}
