using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void ClearArchiveSearch() => ArchiveSearchText = "";

    [RelayCommand]
    private void CloseArchivedPasswordDetails() => ArchiveNarrowShowsList = true;

    [RelayCommand]
    private async Task UnarchivePasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var siblings = GetArchivedPasswordSiblings(entry).ToList();
        foreach (var item in siblings)
        {
            item.IsArchived = false;
            item.ArchivedAt = null;
            item.IsSelected = false;
            await _repository.SavePasswordAsync(item);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = item.Id,
                ItemTitle = item.Title,
                OperationType = "UNARCHIVE",
                DeviceName = Environment.MachineName
            });
            ArchivedPasswords.Remove(item);
            var current = ArchivedPasswords.FirstOrDefault(password => password.Id == item.Id);
            if (current is not null)
            {
                ArchivedPasswords.Remove(current);
            }

            RefreshPasswordTotpDisplay(item);
            RefreshPasswordAttachmentState(item);
        }

        ReplacePasswordGroup([], siblings);
        RefreshBoundTotpPresentation(siblings);
        RaiseCounts();
        RaiseFilteredPasswordsChanged();
        ArchiveNarrowShowsList = true;
        InvalidateSecurityAnalysis();
        StatusMessage = _localization.Format("UnarchivedPasswordFormat", entry.Title);
    }

    [RelayCommand]
    private void ShowArchivedPasswordDetails(PasswordEntry? entry)
    {
        if (entry is not null)
        {
            SelectedArchivedPassword = entry;
            ArchiveNarrowShowsList = false;
        }
    }
}
