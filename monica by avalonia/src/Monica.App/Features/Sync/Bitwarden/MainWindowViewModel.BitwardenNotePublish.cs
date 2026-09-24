using CommunityToolkit.Mvvm.Input;
using Monica.Core.Bitwarden;

namespace Monica.App.ViewModels;

/// A note has no way into the library's batch selection - the tree offers no row checkbox and select all
/// skips row kinds with no bulk command behind them - so the one place that can offer a note to a vault is
/// the editor already holding it. Saving first is not a formality: the encoder judges the stored row, and a
/// draft is not that row yet.
public sealed partial class MainWindowViewModel
{
    public bool BitwardenNotePublishOffered => BitwardenAccounts.Any(item => item.IsConnected);

    [RelayCommand]
    private async Task PublishCurrentNoteToBitwardenAsync()
    {
        var tab = SelectedNoteTab;
        var account = ResolveBitwardenPublishTarget();
        if (tab is null || account is null)
        {
            BitwardenOperationError = _localization.Get("BitwardenPublishNoAccount");
            return;
        }

        CaptureNoteEditorState(tab, markDirty: tab.IsDirty);
        if (!CanSaveNoteTab(tab))
        {
            return;
        }

        var item = await SaveNoteTabAsync(tab);
        RaiseNoteCountState();
        if (!BitwardenCipherPayloadBuilder.CanEncode(item))
        {
            // Nothing left the device and nothing was rewritten; naming the reason is the whole point of
            // offering this here rather than hiding the door, because the note stays perfectly usable.
            SetStatusFailure("BitwardenPublishNoteNotCarriable");
            return;
        }

        SelectedBitwardenAccount = account;
        item.BitwardenVaultId = account.Id;
        await _repository.SaveSecureItemAsync(item);
        RebuildVaultTree();
        RaiseVaultBatchState();
        await SyncBitwardenAccountCommand.ExecuteAsync(account);
    }
}
