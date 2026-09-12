using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Users;

namespace InventoryManagement.App.ViewModels.Users;

/// <summary>
/// Backs the Users page, which has two tabs: user accounts, and configurable
/// roles/permissions. Kept as one ViewModel for now since both tabs are
/// small and share the role list - split into two if either grows
/// significantly in a later stage.
/// </summary>
public sealed partial class UsersViewModel : ViewModelBase
{
    private readonly IUserManagementService _userManagementService;
    private readonly IRoleManagementService _roleManagementService;

    public ObservableCollection<UserSummary> Users { get; } = new();

    public ObservableCollection<RoleSummary> Roles { get; } = new();

    public ObservableCollection<PermissionOption> PermissionOptions { get; } = new();

    [ObservableProperty]
    private string? _errorMessage;

    // --- User edit panel state ---

    [ObservableProperty]
    private bool _isEditingUser;

    [ObservableProperty]
    private Guid? _editingUserId;

    [ObservableProperty]
    private string _userFormUsername = string.Empty;

    [ObservableProperty]
    private string _userFormPassword = string.Empty;

    [ObservableProperty]
    private string _userFormFullName = string.Empty;

    [ObservableProperty]
    private string _userFormEmail = string.Empty;

    [ObservableProperty]
    private RoleSummary? _userFormRole;

    [ObservableProperty]
    private bool _userFormIsActive = true;

    /// <summary>Username can be set on create but never changed afterwards - keeps login identity stable.</summary>
    public bool IsUsernameEditable => EditingUserId is null;

    public bool IsExistingUser => EditingUserId is not null;

    // --- Role edit panel state ---

    [ObservableProperty]
    private bool _isEditingRole;

    [ObservableProperty]
    private Guid? _editingRoleId;

    [ObservableProperty]
    private string _roleFormName = string.Empty;

    [ObservableProperty]
    private string _roleFormDescription = string.Empty;

    public UsersViewModel(IUserManagementService userManagementService, IRoleManagementService roleManagementService)
    {
        _userManagementService = userManagementService;
        _roleManagementService = roleManagementService;
    }

    public async Task InitializeAsync()
    {
        await LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        IsBusy = true;
        try
        {
            var roles = await _roleManagementService.GetRolesAsync();
            var permissions = await _roleManagementService.GetAllPermissionsAsync();
            var users = await _userManagementService.GetUsersAsync();

            Roles.Clear();
            foreach (var role in roles)
            {
                Roles.Add(role);
            }

            PermissionOptions.Clear();
            foreach (var permission in permissions)
            {
                PermissionOptions.Add(new PermissionOption(permission, isSelected: false));
            }

            Users.Clear();
            foreach (var user in users)
            {
                Users.Add(user);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------- Users ----------------

    [RelayCommand]
    private void StartCreateUser()
    {
        ErrorMessage = null;
        EditingUserId = null;
        UserFormUsername = string.Empty;
        UserFormPassword = string.Empty;
        UserFormFullName = string.Empty;
        UserFormEmail = string.Empty;
        UserFormRole = Roles.FirstOrDefault();
        UserFormIsActive = true;
        IsEditingUser = true;

        OnPropertyChanged(nameof(IsUsernameEditable));
        OnPropertyChanged(nameof(IsExistingUser));
    }

    [RelayCommand]
    private void StartEditUser(UserSummary user)
    {
        ErrorMessage = null;
        EditingUserId = user.Id;
        UserFormUsername = user.Username;
        UserFormPassword = string.Empty;
        UserFormFullName = user.FullName;
        UserFormEmail = user.Email ?? string.Empty;
        UserFormRole = Roles.FirstOrDefault(r => r.Id == user.RoleId);
        UserFormIsActive = user.IsActive;
        IsEditingUser = true;

        OnPropertyChanged(nameof(IsUsernameEditable));
        OnPropertyChanged(nameof(IsExistingUser));
    }

    [RelayCommand]
    private void CancelUserEdit()
    {
        IsEditingUser = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task SaveUserAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(UserFormFullName))
        {
            ErrorMessage = "Full name is required.";
            return;
        }

        IsBusy = true;
        try
        {
            if (EditingUserId is null)
            {
                if (string.IsNullOrWhiteSpace(UserFormUsername) || string.IsNullOrWhiteSpace(UserFormPassword))
                {
                    ErrorMessage = "Username and password are required.";
                    return;
                }

                if (UserFormPassword.Length < 8)
                {
                    ErrorMessage = "Password must be at least 8 characters.";
                    return;
                }

                var createResult = await _userManagementService.CreateUserAsync(new CreateUserRequest(
                    UserFormUsername, UserFormPassword, UserFormFullName,
                    string.IsNullOrWhiteSpace(UserFormEmail) ? null : UserFormEmail,
                    UserFormRole?.Id));

                if (createResult.IsFailure)
                {
                    ErrorMessage = createResult.Error;
                    return;
                }
            }
            else
            {
                var updateResult = await _userManagementService.UpdateUserAsync(new UpdateUserRequest(
                    EditingUserId.Value, UserFormFullName,
                    string.IsNullOrWhiteSpace(UserFormEmail) ? null : UserFormEmail,
                    UserFormRole?.Id, UserFormIsActive));

                if (updateResult.IsFailure)
                {
                    ErrorMessage = updateResult.Error;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(UserFormPassword))
                {
                    if (UserFormPassword.Length < 8)
                    {
                        ErrorMessage = "Password must be at least 8 characters.";
                        return;
                    }

                    await _userManagementService.ChangePasswordAsync(
                        new ChangePasswordRequest(EditingUserId.Value, UserFormPassword));
                }
            }

            IsEditingUser = false;
            await LoadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeactivateUserAsync(UserSummary user)
    {
        ErrorMessage = null;

        var result = await _userManagementService.DeactivateUserAsync(user.Id);
        if (result.IsFailure)
        {
            ErrorMessage = result.Error;
            return;
        }

        await LoadAllAsync();
    }

    // ---------------- Roles ----------------

    [RelayCommand]
    private void StartCreateRole()
    {
        ErrorMessage = null;
        EditingRoleId = null;
        RoleFormName = string.Empty;
        RoleFormDescription = string.Empty;

        foreach (var option in PermissionOptions)
        {
            option.IsSelected = false;
        }

        IsEditingRole = true;
    }

    [RelayCommand]
    private void StartEditRole(RoleSummary role)
    {
        ErrorMessage = null;
        EditingRoleId = role.Id;
        RoleFormName = role.Name;
        RoleFormDescription = role.Description ?? string.Empty;

        var granted = role.PermissionIds.ToHashSet();
        foreach (var option in PermissionOptions)
        {
            option.IsSelected = granted.Contains(option.Id);
        }

        IsEditingRole = true;
    }

    [RelayCommand]
    private void CancelRoleEdit()
    {
        IsEditingRole = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task SaveRoleAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(RoleFormName))
        {
            ErrorMessage = "Role name is required.";
            return;
        }

        IsBusy = true;
        try
        {
            var selectedPermissionIds = PermissionOptions
                .Where(p => p.IsSelected)
                .Select(p => p.Id)
                .ToList();

            var result = await _roleManagementService.SaveRoleAsync(new SaveRoleRequest(
                EditingRoleId, RoleFormName, RoleFormDescription, selectedPermissionIds));

            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            IsEditingRole = false;
            await LoadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteRoleAsync(RoleSummary role)
    {
        ErrorMessage = null;

        var result = await _roleManagementService.DeleteRoleAsync(role.Id);
        if (result.IsFailure)
        {
            ErrorMessage = result.Error;
            return;
        }

        await LoadAllAsync();
    }
}
