using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Auth;

namespace InventoryManagement.App.ViewModels.Auth;

/// <summary>
/// Backs the login window. Shows either a normal sign-in form or a
/// "create the administrator account" form depending on
/// <see cref="IsFirstRun"/>, which is determined once at startup by asking
/// <see cref="IAuthenticationService.IsFirstRunAsync"/> whether any user
/// accounts exist yet.
/// </summary>
public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthenticationService _authenticationService;

    [ObservableProperty]
    private bool _isFirstRun;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Set once sign-in (or first-run setup) succeeds. Read by App.xaml.cs to proceed to the main shell.</summary>
    public AuthenticatedUser? SignedInUser { get; private set; }

    /// <summary>Raised once sign-in succeeds - App.xaml.cs subscribes to know when to close this window and show the shell.</summary>
    public event EventHandler? SignedIn;

    public LoginViewModel(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    public async Task InitializeAsync()
    {
        IsFirstRun = await _authenticationService.IsFirstRunAsync();
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Enter your username and password.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authenticationService.LoginAsync(Username, Password);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            SignedInUser = result.Value;
            SignedIn?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateAdministratorAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password)
            || string.IsNullOrWhiteSpace(FullName))
        {
            ErrorMessage = "Please fill in every field.";
            return;
        }

        if (Password.Length < 8)
        {
            ErrorMessage = "Password must be at least 8 characters.";
            return;
        }

        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authenticationService.CreateFirstAdministratorAsync(Username, Password, FullName);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            SignedInUser = result.Value;
            SignedIn?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
