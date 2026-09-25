using Monica.Core.Bitwarden;

namespace Monica.App.ViewModels;

/// A pull writes straight into the database, so every on-screen collection is stale the moment it
/// returns: what another device added, renamed or trashed stays invisible until the vault is read
/// again, which in practice meant lock and unlock. This reloads through the same pass the import
/// flows use - a pull can move passwords, secure items and folders at once, so there is nothing
/// narrower that stays honest - and it only runs when the merge actually moved something, because a
/// sync that found no difference must not rebuild a library nobody changed.
public sealed partial class MainWindowViewModel
{
    private async Task<bool> ApplyBitwardenPullToVaultAsync(BitwardenSyncResult result)
    {
        // Both Bitwarden paths hand their whole result to this one method, so it is the only place a list
        // taken from that result can be written without a second call site forgetting it. It runs ahead of
        // the early return below on purpose: that return asks whether the library needs re-reading, which
        // is a different question from whether anything was declined, and a round that changed no rows at
        // all still has to say so.
        ApplyBitwardenUnsyncableChanges(result.Account.Id, result.Unsyncable);
        var merge = result.Merge;
        if (merge.Added + merge.Updated + merge.Deleted + merge.ConflictsBackedUp == 0)
        {
            return false;
        }

        await ReloadVaultKeepingSelectionAsync();
        SetStatusNotice("BitwardenPullAppliedFormat", merge.Added, merge.Updated, merge.Deleted);
        return true;
    }

    /// Shared by every Bitwarden path that writes through the repository instead of through the screen:
    /// the database has the new value, the collections on display do not. The open entry is carried back
    /// by its own row key, because the reload clears the detail pane along with the collections - an
    /// operation the user asked for must not close what they were reading.
    private async Task ReloadVaultKeepingSelectionAsync()
    {
        var openRowKey = SelectedVaultRow?.Key;
        await LoadAsync();
        ReopenVaultRowAfterReload(openRowKey);
    }
}
