using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public int SelectedArchivedPasswordCount => ArchivedPasswords.Count(item => item.IsSelected);
    public bool HasSelectedArchivedPasswords => SelectedArchivedPasswordCount > 0;
    public string SelectedArchivedPasswordCountText =>
        _localization.Format("SelectedPasswordCountFormat", SelectedArchivedPasswordCount);

    public bool AreAllFilteredArchivedPasswordsSelected
    {
        get
        {
            var visible = FilteredArchivedPasswords;
            return visible.Count > 0 && visible.All(item => item.IsSelected);
        }
        set
        {
            UpdatePasswordSelectionsInBatch(() =>
            {
                foreach (var item in FilteredArchivedPasswords)
                {
                    item.IsSelected = value;
                }
            });
        }
    }

    [RelayCommand]
    private void ToggleArchivedPasswordSelection(PasswordEntry? entry)
    {
        if (entry is not null)
        {
            entry.IsSelected = !entry.IsSelected;
        }
    }

    [RelayCommand]
    private void ClearArchivedPasswordSelection()
    {
        UpdatePasswordSelectionsInBatch(() =>
        {
            foreach (var item in ArchivedPasswords.Where(item => item.IsSelected))
            {
                item.IsSelected = false;
            }
        });
    }

    private void RaiseArchiveSelectionState()
    {
        OnPropertyChanged(nameof(SelectedArchivedPasswordCount));
        OnPropertyChanged(nameof(HasSelectedArchivedPasswords));
        OnPropertyChanged(nameof(SelectedArchivedPasswordCountText));
        OnPropertyChanged(nameof(AreAllFilteredArchivedPasswordsSelected));
    }
}
