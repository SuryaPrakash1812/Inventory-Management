using InventoryManagement.Domain.Common;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// A single business-configuration key/value pair (e.g. company name,
/// default tax rate, invoice number prefix). This is deliberately separate
/// from the per-user UI preferences file (theme, etc.) written by
/// <c>ISettingsService</c> in Stage 1 - that file is local UI state; this
/// table is application/business configuration that belongs in the shared
/// database and will be edited from a future Settings sub-page.
/// </summary>
public class ApplicationSetting : AuditableEntity
{
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public string? Description { get; set; }
}
