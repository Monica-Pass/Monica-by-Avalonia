using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class CategoryPickerViewModel : ObservableObject
{
    public CategoryPickerViewModel(ILocalizationService localization, IEnumerable<Category> categories, long? selectedCategoryId = null)
    {
        L = localization;
        foreach (var category in PasswordCategoryChoice.BuildOptions(categories, localization.Get("NoFolder")))
        {
            CategoryOptions.Add(category);
            FilteredCategoryOptions.Add(category);
        }

        SelectedCategory = CategoryOptions.FirstOrDefault(item => item.Id == selectedCategoryId) ?? CategoryOptions[0];
    }

    public ILocalizationService L { get; }
    public ObservableCollection<PasswordCategoryChoice> CategoryOptions { get; } = [];
    public ObservableCollection<PasswordCategoryChoice> FilteredCategoryOptions { get; } = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private PasswordCategoryChoice? _selectedCategory;

    partial void OnSearchTextChanged(string value)
    {
        var query = value.Trim();
        var filtered = query.Length == 0
            ? CategoryOptions
            : CategoryOptions.Where(option =>
                option.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase));
        FilteredCategoryOptions.Clear();
        foreach (var option in filtered)
        {
            FilteredCategoryOptions.Add(option);
        }
    }
}
