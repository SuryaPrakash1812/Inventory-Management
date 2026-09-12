using System.Globalization;
using System.Text;
using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Products;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Products;

public sealed class ProductService : IProductService
{
    private readonly IAppDbContext _context;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUserSession _session;

    public ProductService(
        IAppDbContext context,
        IAuditLogger auditLogger,
        IDateTimeProvider dateTimeProvider,
        ICurrentUserSession session)
    {
        _context = context;
        _auditLogger = auditLogger;
        _dateTimeProvider = dateTimeProvider;
        _session = session;
    }

    public async Task<PagedResult<ProductSummary>> GetProductsAsync(
        ProductQueryParameters query, CancellationToken cancellationToken = default)
    {
        var filtered = BuildFilteredQuery(query);

        var totalCount = await filtered.CountAsync(cancellationToken);

        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = query.PageSize <= 0 ? 25 : query.PageSize;

        var items = await ApplySort(filtered, query.SortColumn, query.SortDescending)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductSummary(
                p.Id, p.Sku, p.Barcode, p.Name, p.Brand, p.CategoryId, p.Category.Name, p.Unit,
                p.CostPrice, p.SellingPrice, p.TaxPercentage, p.ReorderLevel, p.QuantityOnHand,
                p.IsActive, p.CreatedAtUtc, p.ModifiedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductSummary>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<ProductDetail?> GetProductByIdAsync(
        Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .Where(p => p.Id == productId)
            .Select(p => new ProductDetail(
                p.Id, p.Sku, p.Barcode, p.Name, p.Description, p.Brand, p.CategoryId, p.Category.Name, p.Unit,
                p.CostPrice, p.SellingPrice, p.TaxPercentage, p.ReorderLevel, p.QuantityOnHand, p.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Result<ProductSummary>> CreateProductAsync(
        CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateBasics(
            request.Sku, request.Name, request.CategoryId,
            request.PurchasePrice, request.SellingPrice, request.TaxPercentage, request.MinimumStock);
        if (validationError is not null)
        {
            return Result.Failure<ProductSummary>(validationError);
        }

        // IgnoreQueryFilters: the unique index on Sku/Barcode applies at the
        // database level regardless of soft-delete state, so a conflict with
        // a deactivated-and-deleted product would otherwise surface as a raw
        // DbUpdateException instead of this clean validation message.
        var skuTaken = await _context.Products.IgnoreQueryFilters()
            .AnyAsync(p => p.Sku == request.Sku, cancellationToken);
        if (skuTaken)
        {
            return Result.Failure<ProductSummary>($"SKU '{request.Sku}' is already in use.");
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode))
        {
            var barcodeTaken = await _context.Products.IgnoreQueryFilters()
                .AnyAsync(p => p.Barcode == request.Barcode, cancellationToken);
            if (barcodeTaken)
            {
                return Result.Failure<ProductSummary>($"Barcode '{request.Barcode}' is already in use.");
            }
        }

        var categoryExists = await _context.Categories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken);
        if (!categoryExists)
        {
            return Result.Failure<ProductSummary>("Selected category does not exist.");
        }

        var product = new Product
        {
            Sku = request.Sku.Trim(),
            Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim(),
            Name = request.Name.Trim(),
            Description = request.Description,
            Brand = request.Brand,
            CategoryId = request.CategoryId,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "pcs" : request.Unit.Trim(),
            CostPrice = request.PurchasePrice,
            SellingPrice = request.SellingPrice,
            TaxPercentage = request.TaxPercentage,
            ReorderLevel = request.MinimumStock,
            QuantityOnHand = request.InitialStock,
            IsActive = true,
        };

        _context.Products.Add(product);

        if (request.InitialStock != 0)
        {
            _context.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                MovementType = StockMovementType.OpeningBalance,
                QuantityChange = request.InitialStock,
                QuantityBalanceAfter = request.InitialStock,
                ReferenceType = StockReferenceType.Manual,
                ReferenceId = product.Id,
                OccurredAtUtc = _dateTimeProvider.UtcNow,
                PerformedByUserId = _session.CurrentUser?.Id,
                Notes = "Opening stock balance set at product creation.",
            });
        }

        await _auditLogger.LogAsync(
            AuditAction.Created, nameof(Product), product.Id, $"Product '{product.Sku}' created.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToSummaryAsync(product.Id, cancellationToken));
    }

    public async Task<Result<ProductSummary>> UpdateProductAsync(
        UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products.SingleOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);
        if (product is null)
        {
            return Result.Failure<ProductSummary>("Product not found.");
        }

        var validationError = ValidateBasics(
            product.Sku, request.Name, request.CategoryId,
            request.PurchasePrice, request.SellingPrice, request.TaxPercentage, request.MinimumStock);
        if (validationError is not null)
        {
            return Result.Failure<ProductSummary>(validationError);
        }

        var categoryExists = await _context.Categories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken);
        if (!categoryExists)
        {
            return Result.Failure<ProductSummary>("Selected category does not exist.");
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode))
        {
            var barcodeTaken = await _context.Products.IgnoreQueryFilters()
                .AnyAsync(p => p.Barcode == request.Barcode && p.Id != product.Id, cancellationToken);
            if (barcodeTaken)
            {
                return Result.Failure<ProductSummary>($"Barcode '{request.Barcode}' is already in use.");
            }
        }

        product.Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
        product.Name = request.Name.Trim();
        product.Description = request.Description;
        product.Brand = request.Brand;
        product.CategoryId = request.CategoryId;
        product.Unit = string.IsNullOrWhiteSpace(request.Unit) ? "pcs" : request.Unit.Trim();
        product.CostPrice = request.PurchasePrice;
        product.SellingPrice = request.SellingPrice;
        product.TaxPercentage = request.TaxPercentage;
        product.ReorderLevel = request.MinimumStock;
        product.IsActive = request.IsActive;

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(Product), product.Id, $"Product '{product.Sku}' updated.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToSummaryAsync(product.Id, cancellationToken));
    }

    public async Task<Result> DeactivateProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            return Result.Failure("Product not found.");
        }

        product.IsActive = false;

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(Product), product.Id, $"Product '{product.Sku}' deactivated.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<string> ExportToCsvAsync(
        ProductQueryParameters query, CancellationToken cancellationToken = default)
    {
        var products = await BuildFilteredQuery(query)
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Sku,
                p.Barcode,
                p.Name,
                p.Description,
                p.Brand,
                CategoryName = p.Category.Name,
                p.Unit,
                p.CostPrice,
                p.SellingPrice,
                p.TaxPercentage,
                p.ReorderLevel,
                p.QuantityOnHand,
                p.IsActive,
            })
            .ToListAsync(cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine(
            "Sku,Barcode,Name,Description,Brand,Category,Unit,PurchasePrice,SellingPrice,TaxPercentage,MinimumStock,CurrentStock,IsActive");

        foreach (var p in products)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                CsvEscape(p.Sku),
                CsvEscape(p.Barcode),
                CsvEscape(p.Name),
                CsvEscape(p.Description),
                CsvEscape(p.Brand),
                CsvEscape(p.CategoryName),
                CsvEscape(p.Unit),
                p.CostPrice.ToString(CultureInfo.InvariantCulture),
                p.SellingPrice.ToString(CultureInfo.InvariantCulture),
                p.TaxPercentage.ToString(CultureInfo.InvariantCulture),
                p.ReorderLevel.ToString(CultureInfo.InvariantCulture),
                p.QuantityOnHand.ToString(CultureInfo.InvariantCulture),
                p.IsActive.ToString(),
            }));
        }

        return csv.ToString();
    }

    public async Task<ProductImportResult> ImportFromCsvAsync(
        string csvContent, CancellationToken cancellationToken = default)
    {
        var rows = ParseCsv(csvContent);
        if (rows.Count == 0)
        {
            return new ProductImportResult(0, 0, Array.Empty<ProductImportRowError>());
        }

        var header = rows[0].Select(h => h.Trim()).ToList();
        int IndexOf(string name) => header.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));

        var skuIdx = IndexOf("Sku");
        var barcodeIdx = IndexOf("Barcode");
        var nameIdx = IndexOf("Name");
        var descriptionIdx = IndexOf("Description");
        var brandIdx = IndexOf("Brand");
        var categoryIdx = IndexOf("Category");
        var unitIdx = IndexOf("Unit");
        var purchasePriceIdx = IndexOf("PurchasePrice");
        var sellingPriceIdx = IndexOf("SellingPrice");
        var taxPercentageIdx = IndexOf("TaxPercentage");
        var minimumStockIdx = IndexOf("MinimumStock");
        var currentStockIdx = IndexOf("CurrentStock");
        var isActiveIdx = IndexOf("IsActive");

        if (skuIdx < 0 || nameIdx < 0 || categoryIdx < 0)
        {
            return new ProductImportResult(0, 0, new[]
            {
                new ProductImportRowError(0, "CSV must include at least Sku, Name, and Category columns."),
            });
        }

        var categoryByName = (await _context.Categories.ToListAsync(cancellationToken))
            .ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);

        var productBySku = (await _context.Products.ToListAsync(cancellationToken))
            .ToDictionary(p => p.Sku, StringComparer.OrdinalIgnoreCase);

        var existingBarcodes = productBySku.Values
            .Where(p => !string.IsNullOrWhiteSpace(p.Barcode))
            .ToDictionary(p => p.Barcode!, p => p.Id, StringComparer.OrdinalIgnoreCase);

        var errors = new List<ProductImportRowError>();
        var createdCount = 0;
        var updatedCount = 0;

        static string? Get(List<string> row, int idx) => idx >= 0 && idx < row.Count ? row[idx] : null;

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 1;

            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var sku = Get(row, skuIdx)?.Trim();
            var name = Get(row, nameIdx)?.Trim();
            var categoryName = Get(row, categoryIdx)?.Trim();

            if (string.IsNullOrWhiteSpace(sku))
            {
                errors.Add(new ProductImportRowError(rowNumber, "SKU is required."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ProductImportRowError(rowNumber, "Name is required."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(categoryName) || !categoryByName.TryGetValue(categoryName, out var categoryId))
            {
                errors.Add(new ProductImportRowError(rowNumber, $"Category '{categoryName}' does not exist."));
                continue;
            }

            if (!TryParseRequiredDecimal(Get(row, purchasePriceIdx), out var purchasePrice))
            {
                errors.Add(new ProductImportRowError(rowNumber, "PurchasePrice is required and must be a number."));
                continue;
            }

            if (!TryParseRequiredDecimal(Get(row, sellingPriceIdx), out var sellingPrice))
            {
                errors.Add(new ProductImportRowError(rowNumber, "SellingPrice is required and must be a number."));
                continue;
            }

            var taxPercentage = ParseOptionalDecimal(Get(row, taxPercentageIdx));
            var minimumStock = ParseOptionalDecimal(Get(row, minimumStockIdx));
            var barcode = Get(row, barcodeIdx)?.Trim();
            var description = Get(row, descriptionIdx);
            var brand = Get(row, brandIdx)?.Trim();
            var unit = Get(row, unitIdx)?.Trim();
            var isActive = ParseBool(Get(row, isActiveIdx), defaultValue: true);

            if (!string.IsNullOrWhiteSpace(barcode)
                && existingBarcodes.TryGetValue(barcode, out var ownerId)
                && (!productBySku.TryGetValue(sku, out var sameProduct) || sameProduct.Id != ownerId))
            {
                errors.Add(new ProductImportRowError(rowNumber, $"Barcode '{barcode}' is already used by another product."));
                continue;
            }

            if (productBySku.TryGetValue(sku, out var existingProduct))
            {
                existingProduct.Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode;
                existingProduct.Name = name;
                existingProduct.Description = description;
                existingProduct.Brand = brand;
                existingProduct.CategoryId = categoryId;
                existingProduct.Unit = string.IsNullOrWhiteSpace(unit) ? existingProduct.Unit : unit;
                existingProduct.CostPrice = purchasePrice;
                existingProduct.SellingPrice = sellingPrice;
                existingProduct.TaxPercentage = taxPercentage;
                existingProduct.ReorderLevel = minimumStock;
                existingProduct.IsActive = isActive;
                // Stock is deliberately never touched by import for an
                // existing product - see IProductService.ImportFromCsvAsync remarks.

                updatedCount++;
            }
            else
            {
                var newProduct = new Product
                {
                    Sku = sku,
                    Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode,
                    Name = name,
                    Description = description,
                    Brand = brand,
                    CategoryId = categoryId,
                    Unit = string.IsNullOrWhiteSpace(unit) ? "pcs" : unit,
                    CostPrice = purchasePrice,
                    SellingPrice = sellingPrice,
                    TaxPercentage = taxPercentage,
                    ReorderLevel = minimumStock,
                    QuantityOnHand = ParseOptionalDecimal(Get(row, currentStockIdx)),
                    IsActive = isActive,
                };

                _context.Products.Add(newProduct);
                productBySku[sku] = newProduct;
                if (!string.IsNullOrWhiteSpace(barcode))
                {
                    existingBarcodes[barcode] = newProduct.Id;
                }

                createdCount++;
            }
        }

        await _auditLogger.LogAsync(
            AuditAction.Other,
            nameof(Product),
            null,
            $"CSV import: {createdCount} created, {updatedCount} updated, {errors.Count} error(s).",
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new ProductImportResult(createdCount, updatedCount, errors);
    }

    private IQueryable<Product> BuildFilteredQuery(ProductQueryParameters query)
    {
        var products = _context.Products.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            products = products.Where(p =>
                p.Sku.Contains(term)
                || (p.Barcode != null && p.Barcode.Contains(term))
                || p.Name.Contains(term));
        }

        if (query.CategoryId is { } categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (query.IsActive is { } isActive)
        {
            products = products.Where(p => p.IsActive == isActive);
        }

        return products;
    }

    private static IQueryable<Product> ApplySort(IQueryable<Product> products, ProductSortColumn column, bool descending)
    {
        return column switch
        {
            ProductSortColumn.Sku => descending ? products.OrderByDescending(p => p.Sku) : products.OrderBy(p => p.Sku),
            ProductSortColumn.CategoryName => descending
                ? products.OrderByDescending(p => p.Category.Name)
                : products.OrderBy(p => p.Category.Name),
            ProductSortColumn.SellingPrice => descending
                ? products.OrderByDescending(p => p.SellingPrice)
                : products.OrderBy(p => p.SellingPrice),
            ProductSortColumn.CurrentStock => descending
                ? products.OrderByDescending(p => p.QuantityOnHand)
                : products.OrderBy(p => p.QuantityOnHand),
            ProductSortColumn.CreatedAtUtc => descending
                ? products.OrderByDescending(p => p.CreatedAtUtc)
                : products.OrderBy(p => p.CreatedAtUtc),
            _ => descending ? products.OrderByDescending(p => p.Name) : products.OrderBy(p => p.Name),
        };
    }

    private static string? ValidateBasics(
        string sku, string name, Guid categoryId,
        decimal purchasePrice, decimal sellingPrice, decimal taxPercentage, decimal minimumStock)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return "SKU is required.";
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name is required.";
        }

        if (categoryId == Guid.Empty)
        {
            return "A category must be selected.";
        }

        if (purchasePrice < 0)
        {
            return "Purchase price cannot be negative.";
        }

        if (sellingPrice < 0)
        {
            return "Selling price cannot be negative.";
        }

        if (taxPercentage is < 0 or > 100)
        {
            return "Tax percentage must be between 0 and 100.";
        }

        if (minimumStock < 0)
        {
            return "Minimum stock cannot be negative.";
        }

        return null;
    }

    private async Task<ProductSummary> ToSummaryAsync(Guid productId, CancellationToken cancellationToken)
    {
        return await _context.Products
            .Where(p => p.Id == productId)
            .Select(p => new ProductSummary(
                p.Id, p.Sku, p.Barcode, p.Name, p.Brand, p.CategoryId, p.Category.Name, p.Unit,
                p.CostPrice, p.SellingPrice, p.TaxPercentage, p.ReorderLevel, p.QuantityOnHand,
                p.IsActive, p.CreatedAtUtc, p.ModifiedAtUtc))
            .SingleAsync(cancellationToken);
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static bool TryParseRequiredDecimal(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static decimal ParseOptionalDecimal(string? text, decimal defaultValue = 0) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : defaultValue;

    private static bool ParseBool(string? text, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "y" => true,
            "false" or "0" or "no" or "n" => false,
            _ => defaultValue,
        };
    }

    /// <summary>
    /// Minimal RFC 4180-ish CSV parser: handles quoted fields, escaped quotes
    /// (""), and embedded commas/newlines within quotes. Kept in-house rather
    /// than adding a CSV library dependency for what is, structurally, a
    /// simple flat table.
    /// </summary>
    private static List<List<string>> ParseCsv(string content)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    currentRow.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    currentRow.Add(field.ToString());
                    field.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(field.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }
}
