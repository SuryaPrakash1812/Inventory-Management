using InventoryManagement.Domain.Common;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// A product category. Supports one level of nesting via
/// <see cref="ParentCategoryId"/> (e.g. "Beverages" -> "Soft Drinks") without
/// building a full tree-management feature yet - that can grow later without
/// a schema change.
/// </summary>
public class Category : AuditableSoftDeleteEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid? ParentCategoryId { get; set; }

    public Category? ParentCategory { get; set; }

    public ICollection<Category> ChildCategories { get; set; } = new List<Category>();

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
