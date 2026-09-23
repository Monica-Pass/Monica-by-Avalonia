using CommunityToolkit.Mvvm.Input;
using Monica.App.Features.Vault;
using Monica.Core.Bitwarden;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

/// An entry that belongs to no external vault lives only in this install: if the file is lost, nothing
/// pulls it back. Publishing is the user naming which of those entries a Bitwarden vault owes a copy of,
/// and it stays deliberate rather than automatic, because an entry created while no account was connected
/// has never been offered to any server. The stamp is what commits - the upload itself is the ordinary
/// drift scan, so a publish that cannot reach the network still stands, and the next sync finishes it.
public sealed partial class MainWindowViewModel
{
    // Notes are deliberately absent: the library's batch layer does not count, select-all, clear, move or
    // delete them at all, so a note a user ticks by hand is invisible to every other action there. Offering
    // them here would be the one place that quietly disagrees with that.
    private int VaultBatchBitwardenPublishableCount =>
        Passwords.Count(IsPublishable) + WalletItems.Count(IsPublishable);

    public bool VaultBatchSupportsBitwardenPublish =>
        VaultBatchBitwardenPublishableCount > 0 && BitwardenAccounts.Any(item => item.IsConnected);

    public string BitwardenPublishMenuText =>
        _localization.Format("BitwardenPublishMenuFormat", VaultBatchBitwardenPublishableCount);

    private void RaiseBitwardenPublishState()
    {
        OnPropertyChanged(nameof(VaultBatchSupportsBitwardenPublish));
        OnPropertyChanged(nameof(BitwardenPublishMenuText));
    }

    private static bool IsPublishable(PasswordEntry entry) =>
        entry.IsSelected && VaultQuickFilters.IsLocalOnly(entry) && BitwardenCipherPayloadBuilder.CanEncode(entry);

    private static bool IsPublishable(SecureItem item) =>
        item.IsSelected && VaultQuickFilters.IsLocalOnly(item) && BitwardenCipherPayloadBuilder.CanEncode(item);

    [RelayCommand]
    private async Task PublishSelectionToBitwardenAsync()
    {
        var account = ResolveBitwardenPublishTarget();
        if (account is null)
        {
            BitwardenOperationError = _localization.Get("BitwardenPublishNoAccount");
            return;
        }

        var selectedPasswords = Passwords
            .Where(item => item.IsSelected && VaultQuickFilters.IsLocalOnly(item))
            .ToArray();
        var selectedWalletItems = WalletItems
            .Where(item => item.IsSelected && VaultQuickFilters.IsLocalOnly(item))
            .ToArray();
        var publishablePasswords = selectedPasswords.Where(BitwardenCipherPayloadBuilder.CanEncode).ToArray();
        var publishableWalletItems = selectedWalletItems.Where(BitwardenCipherPayloadBuilder.CanEncode).ToArray();
        var skipped = (selectedPasswords.Length - publishablePasswords.Length) +
                      (selectedWalletItems.Length - publishableWalletItems.Length);
        if (publishablePasswords.Length == 0 && publishableWalletItems.Length == 0)
        {
            return;
        }

        SelectedBitwardenAccount = account;
        foreach (var entry in publishablePasswords)
        {
            entry.BitwardenVaultId = account.Id;
            entry.IsSelected = false;
            await _repository.SavePasswordAsync(entry);
        }

        foreach (var item in publishableWalletItems)
        {
            item.BitwardenVaultId = account.Id;
            item.IsSelected = false;
            await _repository.SaveSecureItemAsync(item);
        }

        RebuildVaultTree();
        RaiseVaultBatchState();
        if (skipped > 0)
        {
            // Say so before the synchronization takes the status line over: an upload that silently
            // covers fewer entries than were checked reads as though it lost some.
            SetStatusFailure("BitwardenPublishSkippedFormat", skipped);
        }

        await SyncBitwardenAccountCommand.ExecuteAsync(account);
    }

    // The library publishes to one vault at a time, and the account card already shows which one the
    // sync buttons work on, so the checked entries follow that same choice rather than asking again.
    private BitwardenAccountDisplayItem? ResolveBitwardenPublishTarget() =>
        SelectedBitwardenAccount is { IsConnected: true } selected
            ? selected
            : BitwardenAccounts.FirstOrDefault(item => item.IsConnected);
}
