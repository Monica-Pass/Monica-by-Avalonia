using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task RestoreSelectedDeletedPasswordsAsync()
    {
        var selected = DeletedPasswords.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        var handledGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in selected)
        {
            if (handledGroups.Add(BuildSiblingGroupKey(entry)))
            {
                await RestorePasswordAsync(entry);
            }
        }

        ClearDeletedPasswordSelection();
        StatusMessage = _localization.Format("RestoredSelectedPasswordsFormat", selected.Length);
    }

    [RelayCommand]
    private async Task DeleteSelectedDeletedPasswordsPermanentlyAsync()
    {
        var selected = DeletedPasswords.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0 || !await ConfirmSelectedPermanentDeleteAsync(selected.Length))
        {
            return;
        }

        var handledGroups = new HashSet<string>(StringComparer.Ordinal);
        var purged = new List<PasswordEntry>();
        foreach (var entry in selected)
        {
            if (handledGroups.Add(BuildSiblingGroupKey(entry)))
            {
                purged.AddRange(await PurgeDeletedPasswordGroupAsync(entry));
            }
        }

        ClearDeletedPasswordSelection();
        RefreshBoundTotpPresentation(purged);
        RaisePasswordCountState();
        InvalidateSecurityAnalysis();
        RecycleBinNarrowShowsList = true;
        StatusMessage = _localization.Format("DeletedSelectedPasswordsPermanentlyFormat", selected.Length);
    }

    private Task<bool> ConfirmSelectedPermanentDeleteAsync(int count) =>
        _confirmationDialogService.ConfirmTypedAsync(
            _localization.Get("DeleteSelectedPermanentlyConfirmationTitle"),
            _localization.Format("DeleteSelectedPermanentlyConfirmationMessageFormat", count),
            _localization.Get("PermanentDeleteConfirmationPhrase"),
            _localization.Format(
                "PermanentDeleteConfirmationInstructionFormat",
                _localization.Get("PermanentDeleteConfirmationPhrase")),
            _localization.Get("DeletePermanently"),
            _localization.Cancel);
}
