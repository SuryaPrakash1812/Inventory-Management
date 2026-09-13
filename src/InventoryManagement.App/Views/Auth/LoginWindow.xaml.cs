using InventoryManagement.App.ViewModels.Auth;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace InventoryManagement.App.Views.Auth;

public partial class LoginWindow : FluentWindow
{
    public LoginViewModel ViewModel { get; }

    public LoginWindow(LoginViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        ApplicationThemeManager.Apply(this);

        // See MainWindow's constructor remarks: Mica is a per-window native
        // effect that doesn't refresh on its own when the app-wide theme
        // changes, so each window re-applies itself when notified.
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        Closed += (_, _) => ApplicationThemeManager.Changed -= OnApplicationThemeChanged;

        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private void OnApplicationThemeChanged(ApplicationTheme currentApplicationTheme, System.Windows.Media.Color systemAccent) =>
        ApplicationThemeManager.Apply(this);
}
