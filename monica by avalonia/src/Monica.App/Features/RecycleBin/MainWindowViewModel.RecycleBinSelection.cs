using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public int SelectedDeletedPasswordCount => DeletedPasswords.Count(item => item.IsSelected);
    public bool HasSelectedDeletedPasswords => SelectedDeletedPasswordCount > 0;
    public string SelectedDeletedPasswordCountText =>
        _localization.Format("SelectedPasswordCountFormat", SelectedDeletedPasswordCount);

    public bool AreAllFilteredDeletedPasswordsSelected
    {
        get
        {
            var visible = FilteredDeletedPasswords;
            return visible.Count > 0 && visible.All(item => item.IsSelected);
        }
        set
        {
            UpdatePasswordSelectionsInBatch(() =>
            {
                foreach (var item in FilteredDeletedPasswords)
                {
                    item.IsSelected = value;
                }
            });
        }
    }

    [RelayCommand]
    private void ToggleDeletedPasswordSelection(PasswordEntry? entry)
    {
        if (entry is not null)
        {
            entry.IsSelected = !entry.IsSelected;
        }
    }

    [RelayCommand]
    private void ClearDeletedPasswordSelection()
    {
        UpdatePasswordSelectionsInBatch(() =>
        {
            foreach (var item in DeletedPasswords.Where(item => item.IsSelected))
            {
                item.IsSelected = false;
            }
        });
    }

    private void RaiseRecycleBinSelectionState()
    {
        OnPropertyChanged(nameof(SelectedDeletedPasswordCount));
        OnPropertyChanged(nameof(HasSelectedDeletedPasswords));
        OnPropertyChanged(nameof(SelectedDeletedPasswordCountText));
        OnPropertyChanged(nameof(AreAllFilteredDeletedPasswordsSelected));
    }
}
