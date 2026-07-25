using Monica.Core.Bitwarden;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task<BitwardenAccountDisplayItem> CreateBitwardenAccountDisplayItemAsync(BitwardenAccount account)
    {
        var pendingCount = 0;
        var conflictCount = 0;
        try
        {
            if (_bitwardenPendingOperationStore is not null)
            {
                var pending = await _bitwardenPendingOperationStore.GetAsync(account.Id);
                pendingCount = pending.Count(operation => operation.Status is
                    BitwardenMutationStatus.Pending or BitwardenMutationStatus.InFlight);
            }

            if (_bitwardenConflictBackupStore is not null)
            {
                conflictCount = (await _bitwardenConflictBackupStore.GetUnresolvedAsync(account.Id)).Count;
            }
        }
        catch (Exception exception)
        {
            AppDiagnostics.Error($"Bitwarden account diagnostics could not be loaded for account {account.Id}", exception);
        }

        var displayName = string.IsNullOrWhiteSpace(account.DisplayName)
            ? account.Email
            : account.DisplayName.Trim();
        var connectionStatus = account.IsConnected
            ? _localization.Get("BitwardenConnected")
            : _localization.Get("BitwardenDisconnected");
        var lastSyncText = account.LastSyncAt is { } lastSync
            ? _localization.Format(
                "BitwardenLastSyncFormat",
                lastSync.ToLocalTime().ToString("g", _localization.Culture))
            : _localization.Get("BitwardenNeverSynced");
        var pendingText = pendingCount == 0
            ? _localization.Get("NoPendingChanges")
            : _localization.Format("PendingSyncCountFormat", pendingCount);
        var conflictText = conflictCount == 0
            ? _localization.Get("BitwardenNoConflicts")
            : _localization.Format("BitwardenConflictCountFormat", conflictCount);
        var detail = !string.IsNullOrWhiteSpace(account.LastSyncError)
            ? _localization.Get("BitwardenNeedsAttention")
            : account.IsConnected
                ? _localization.Get("BitwardenReadyToSync")
                : _localization.Get("BitwardenReconnectRequired");

        return new BitwardenAccountDisplayItem(
            account,
            displayName,
            account.Endpoints.WebVault.Host,
            connectionStatus,
            lastSyncText,
            pendingText,
            conflictText,
            detail,
            pendingCount,
            conflictCount);
    }
}
