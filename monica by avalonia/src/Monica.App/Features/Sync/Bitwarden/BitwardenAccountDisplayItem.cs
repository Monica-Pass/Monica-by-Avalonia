using Monica.Core.Bitwarden;

namespace Monica.App.ViewModels;

public sealed record BitwardenAccountDisplayItem(
    BitwardenAccount Account,
    string DisplayName,
    string ServerText,
    string ConnectionStatusText,
    string LastSyncText,
    string PendingChangesText,
    string ConflictText,
    string DetailText,
    int PendingChangeCount,
    int ConflictCount)
{
    public long Id => Account.Id;
    public string Email => Account.Email;
    public bool IsConnected => Account.IsConnected;
    public bool HasPendingChanges => PendingChangeCount > 0;
    public bool HasConflicts => ConflictCount > 0;
    public bool HasError => !string.IsNullOrWhiteSpace(Account.LastSyncError);
}
