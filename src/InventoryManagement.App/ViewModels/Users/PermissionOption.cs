using CommunityToolkit.Mvvm.ComponentModel;
using InventoryManagement.Application.Users;

namespace InventoryManagement.App.ViewModels.Users;

/// <summary>A single permission checkbox item shown while editing a role.</summary>
public sealed partial class PermissionOption : ObservableObject
{
    public Guid Id { get; }

    public string Name { get; }

    public string? Description { get; }

    [ObservableProperty]
    private bool _isSelected;

    public PermissionOption(PermissionInfo permission, bool isSelected)
    {
        Id = permission.Id;
        Name = permission.Name;
        Description = permission.Description;
        _isSelected = isSelected;
    }
}
