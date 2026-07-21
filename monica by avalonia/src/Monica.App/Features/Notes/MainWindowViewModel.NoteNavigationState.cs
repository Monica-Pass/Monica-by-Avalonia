using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Categories;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly HashSet<string> _collapsedNoteFolderKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _collapsedNoteTagKeys = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoteFolderNavigation))]
    [NotifyPropertyChangedFor(nameof(IsNoteTagNavigation))]
    private string _noteNavigationMode = "Folders";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageSelectedNoteFolder))]
    [NotifyPropertyChangedFor(nameof(SelectedNoteFolderPath))]
    private string _selectedNoteFolderKey = "";

    [ObservableProperty]
    private string _newNoteFolderName = "";

    public bool IsNoteFolderNavigation => string.Equals(NoteNavigationMode, "Folders", StringComparison.Ordinal);
    public bool IsNoteTagNavigation => !IsNoteFolderNavigation;
    public string? SelectedNoteFolderPath => GetSelectedNoteFolderPath();
    public bool CanManageSelectedNoteFolder => GetSelectedNoteFolderCategory() is not null;

    private string? GetSelectedNoteFolderPath()
    {
        const string prefix = "folder:";
        return SelectedNoteFolderKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               !SelectedNoteFolderKey.Equals("folder:none", StringComparison.OrdinalIgnoreCase)
            ? LocalCategoryPath.Normalize(SelectedNoteFolderKey[prefix.Length..])
            : null;
    }

    private Category? GetSelectedNoteFolderCategory()
    {
        var selectedPath = GetSelectedNoteFolderPath();
        return string.IsNullOrWhiteSpace(selectedPath)
            ? null
            : Categories.FirstOrDefault(category =>
                LocalCategoryPath.Normalize(category.Name).Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
    }
}
