using CommunityToolkit.Mvvm.Input;
using Monica.App.Features.Vault;
using Monica.Core.Bitwarden;

namespace Monica.App.ViewModels;

/// An entry that belongs to no external vault lives only in this install: if the file is lost, nothing
/// pulls it back. Publishing is the user naming which of those entries a Bitwarden vault owes a copy of,
/// and it stays deliberate rather than automatic, because an entry created while no account was connected
/// has never been offered to any server. The stamp is what commits - the upload itself is the ordinary
/// drift scan, so a publish that cannot reach the network still stands, and the next sync finishes it.
public sealed partial class MainWindowViewModel
{
    private int VaultBatchBitwardenPublishableCount => Passwords.Count(
        item => item.IsSelected &&
                VaultQuickFilters.IsLocalOnly(item) &&
                BitwardenCipherPayloadBuilder.CanEncode(item));

    public bool VaultBatchSupportsBitwardenPublish =>
        VaultBatchBitwardenPublishableCount > 0 && BitwardenAccounts.Any(item => item.IsConnected);

    public string BitwardenPublishMenuText =>
        _localization.Format("BitwardenPublishMenuFormat", VaultBatchBitwardenPublishableCount);

    private void RaiseBitwardenPublishState()
    {
        OnPropertyChanged(nameof(VaultBatchSupportsBitwardenPublish));
        OnPropertyChanged(nameof(BitwardenPublishMenuText));
    }

    [RelayCommand]
    private async Task PublishSelectionToBitwardenAsync()
    {
        var account = ResolveBitwardenPublishTarget();
        if (account is null)
        {
            BitwardenOperationError = _localization.Get("BitwardenPublishNoAccount");
            return;
        }

        var selected = Passwords.Where(item => item.IsSelected && VaultQuickFilters.IsLocalOnly(item))
            .ToArray();
        var publishable = selected.Where(BitwardenCipherPayloadBuilder.CanEncode).ToArray();
        if (publishable.Length == 0)
        {
            return;
        }

        SelectedBitwardenAccount = account;
        foreach (var entry in publishable)
        {
            entry.BitwardenVaultId = account.Id;
            entry.IsSelected = false;
            await _repository.SavePasswordAsync(entry);
        }

        RebuildVaultTree();
        RaiseVaultBatchState();
        if (publishable.Length < selected.Length)
        {
            // Say so before the synchronization takes the status line over: an upload that silently
            // covers fewer entries than were checked reads as though it lost some.
            SetStatusFailure(
                "BitwardenPublishSkippedFormat",
                selected.Length - publishable.Length);
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
