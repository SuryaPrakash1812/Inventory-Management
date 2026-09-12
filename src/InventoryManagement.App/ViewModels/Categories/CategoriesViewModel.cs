using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Categories;

namespace InventoryManagement.App.ViewModels.Categories;

public sealed partial class CategoriesViewModel : ViewModelBase
{
    private readonly ICategoryService _categoryService;

    public ObservableCollection<CategorySummary> Categories { get; } = new();

    /// <summary>Choices for the "parent category" dropdown - every category except whichever one is currently being edited (a category can't be its own parent).</summary>
    public ObservableCollection<CategorySummary> ParentCategoryOptions { get; } = new();

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private Guid? _editingCategoryId;

    [ObservableProperty]
    private string _formName = string.Empty;

    [ObservableProperty]
    private string _formDescription = string.Empty;

    [ObservableProperty]
    private CategorySummary? _formParentCategory;

    public CategoriesViewModel(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    public async Task InitializeAsync() => await LoadAsync();

    [RelayCommand]
    private async Task SearchAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var categories = await _categoryService.GetCategoriesAsync(
                string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm);

            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartCreateAsync()
    {
        ErrorMessage = null;
        EditingCategoryId = null;
        FormName = string.Empty;
        FormDescription = string.Empty;
        FormParentCategory = null;

        await RefreshParentOptionsAsync(excludingCategoryId: null);

        IsEditing = true;
    }

    [RelayCommand]
    private async Task StartEditAsync(CategorySummary category)
    {
        ErrorMessage = null;
        EditingCategoryId = category.Id;
        FormName = category.Name;
        FormDescription = category.Description ?? string.Empty;

        await RefreshParentOptionsAsync(excludingCategoryId: category.Id);
        FormParentCategory = ParentCategoryOptions.FirstOrDefault(c => c.Id == category.ParentCategoryId);

        IsEditing = true;
    }

    private async Task RefreshParentOptionsAsync(Guid? excludingCategoryId)
    {
        var all = await _categoryService.GetCategoriesAsync();

        ParentCategoryOptions.Clear();
        foreach (var category in all.Where(c => c.Id != excludingCategoryId))
        {
            ParentCategoryOptions.Add(category);
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(FormName))
        {
            ErrorMessage = "Category name is required.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _categoryService.SaveCategoryAsync(new SaveCategoryRequest(
                EditingCategoryId, FormName, FormDescription, FormParentCategory?.Id));

            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            IsEditing = false;
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(CategorySummary category)
    {
        ErrorMessage = null;

        var result = await _categoryService.DeleteCategoryAsync(category.Id);
        if (result.IsFailure)
        {
            ErrorMessage = result.Error;
            return;
        }

        await LoadAsync();
    }
}
