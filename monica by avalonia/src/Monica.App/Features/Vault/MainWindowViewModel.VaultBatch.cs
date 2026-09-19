using CommunityToolkit.Mvvm.Input;
using Monica.App.Controls;
using Monica.App.Features.Vault;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

/// The library is the one place that sees every entry type at once, so its batch actions are a
/// dispatch over the checked set: passwords, authenticator codes and wallet items each keep their
/// own bulk command, and a check mark on a tree row means the same thing as one in a list page.
public sealed partial class MainWindowViewModel
{
    private int VaultBatchPasswordCount => Passwords.Count(item => item.IsSelected);

    private int VaultBatchTotpCount => TotpItems.Count(item => item.IsSelected);

    private int VaultBatchWalletCount => WalletItems.Count(item => item.IsSelected);

    public int VaultBatchCount => VaultBatchPasswordCount + VaultBatchTotpCount + VaultBatchWalletCount;

    public bool HasVaultBatchSelection => VaultBatchCount > 0;

    public string VaultBatchLabel => _localization.Format("BatchCountFormat", VaultBatchCount);

    // Favorite exists for passwords and codes, archive for passwords only, so an action that would
    // do nothing to the current set is not offered at all.
    public bool VaultBatchSupportsFavorite => VaultBatchPasswordCount > 0 || VaultBatchTotpCount > 0;

    public bool VaultBatchSupportsArchive => VaultBatchPasswordCount > 0;

    // Stacking only means anything across a set of copies, so one checked password gets no offer.
    public bool VaultBatchSupportsStack => VaultBatchPasswordCount > 1;

    internal void RaiseVaultBatchState()
    {
        OnPropertyChanged(nameof(VaultBatchCount));
        OnPropertyChanged(nameof(HasVaultBatchSelection));
        OnPropertyChanged(nameof(VaultBatchLabel));
        OnPropertyChanged(nameof(VaultBatchSupportsFavorite));
        OnPropertyChanged(nameof(VaultBatchSupportsArchive));
        OnPropertyChanged(nameof(VaultBatchSupportsStack));
    }

    [RelayCommand]
    private void SelectAllVaultRows()
    {
        UpdatePasswordSelectionsInBatch(() =>
        {
            // Only the rows with a bulk command behind them get checked; a note would be checked
            // invisibly and then swept up by an action the user never saw offered.
            foreach (var row in VaultTreeRows.OfType<VaultTreeEntryRow>().Where(row => row.IsBatchable))
            {
                row.IsSelected = true;
            }
        });
        RaiseVaultBatchState();
    }

    [RelayCommand]
    private void ClearVaultBatchSelection()
    {
        ClearPasswordSelectionCommand.Execute(null);
        ClearTotpSelectionCommand.Execute(null);
        ClearWalletSelectionCommand.Execute(null);
        RaiseVaultBatchState();
    }

    [RelayCommand]
    private async Task FavoriteVaultBatchAsync()
    {
        if (VaultBatchPasswordCount > 0)
        {
            await FavoriteSelectedPasswordsCommand.ExecuteAsync(null);
        }

        if (VaultBatchTotpCount > 0)
        {
            await FavoriteSelectedTotpCommand.ExecuteAsync(null);
        }

        RaiseVaultBatchState();
    }

    [RelayCommand]
    private async Task StackVaultBatchAsync()
    {
        if (VaultBatchSupportsStack)
        {
            await StackSelectedPasswordsCommand.ExecuteAsync(null);
        }

        RaiseVaultBatchState();
    }

    [RelayCommand]
    private async Task ArchiveVaultBatchAsync()
    {
        if (VaultBatchPasswordCount > 0)
        {
            await ArchiveSelectedPasswordsCommand.ExecuteAsync(null);
        }

        RaiseVaultBatchState();
    }

    [RelayCommand]
    private async Task DeleteVaultBatchAsync()
    {
        if (VaultBatchPasswordCount > 0)
        {
            await DeleteSelectedPasswordsCommand.ExecuteAsync(null);
        }

        if (VaultBatchTotpCount > 0)
        {
            await DeleteSelectedTotpCommand.ExecuteAsync(null);
        }

        if (VaultBatchWalletCount > 0)
        {
            await DeleteSelectedWalletItemsCommand.ExecuteAsync(null);
        }

        RaiseVaultBatchState();
    }

    // One folder choice for the whole set: the library moves a mixed selection in a single step
    // instead of asking once per entry type.
    [RelayCommand]
    private async Task MoveVaultBatchToFolderAsync()
    {
        var passwords = Passwords.Where(item => item.IsSelected).ToArray();
        var items = TotpItems.Concat(WalletItems).Where(item => item.IsSelected).ToArray();
        if (passwords.Length + items.Length == 0)
        {
            return;
        }

        var folderIds = passwords.Select(item => item.CategoryId)
            .Concat(items.Select(item => item.CategoryId))
            .Distinct()
            .ToArray();
        var currentCategoryId = folderIds.Length == 1 ? folderIds[0] : null;
        var choice = await _categoryPickerDialogService.ShowAsync(Categories.ToList(), currentCategoryId);
        if (choice is null)
        {
            return;
        }

        await MoveVaultBatchCoreAsync(passwords, items, choice);
        RebuildVaultTree();
        RaiseVaultBatchState();
        StatusMessage = _localization.Format(
            "MovedSelectedPasswordsToFolderFormat",
            passwords.Length + items.Length,
            choice.Name);
    }

    private async Task MoveVaultBatchCoreAsync(PasswordEntry[] passwords, SecureItem[] items, PasswordCategoryChoice choice)
    {
        // Stacked copies move with their lead, so a folder never splits a password group in two.
        var handled = new HashSet<long>();
        foreach (var entry in passwords)
        {
            if (!handled.Add(entry.Id))
            {
                continue;
            }

            foreach (var sibling in GetPasswordSiblings(entry))
            {
                handled.Add(sibling.Id);
                sibling.CategoryId = choice.Id;
                await _repository.SavePasswordAsync(sibling);
                await SynchronizeBoundTotpAsync(sibling);
                await LogVaultCategoryMoveAsync("PASSWORD", sibling.Id, sibling.Title);
            }
        }

        foreach (var item in items)
        {
            item.CategoryId = choice.Id;
            await _repository.SaveSecureItemAsync(item);
            await LogVaultCategoryMoveAsync("SECURE_ITEM", item.Id, item.Title);
        }

        foreach (var entry in passwords)
        {
            entry.IsSelected = false;
        }

        foreach (var item in items)
        {
            item.IsSelected = false;
        }
    }
}
