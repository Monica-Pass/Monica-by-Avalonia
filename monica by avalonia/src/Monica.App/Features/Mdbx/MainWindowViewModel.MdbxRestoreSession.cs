using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Data.Mdbx;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _isMdbxRestoreInProgress;

    private int _mdbxRestoreActive;

    private bool CanEditVaultDuringMdbxOperation => IsUnlocked && !IsMdbxBusy && !IsMdbxRestoreInProgress;

    private Task<MdbxSnapshotRestoreResult> RestoreIncomingMdbxSnapshotAsync(
        LocalMdbxDatabase database,
        string incomingPath,
        string workingCopyPath,
        RemoteFileVersion version,
        CancellationToken cancellationToken) =>
        RestoreMdbxSnapshotInSessionAsync(database, incomingPath,
            token => CommitMdbxSyncedMetadataAsync(database, workingCopyPath, version, token), cancellationToken);

    private async Task<MdbxSnapshotRestoreResult> RestoreMdbxSnapshotInSessionAsync(
        LocalMdbxDatabase database,
        string incomingPath,
        Func<CancellationToken, Task> commitMetadata,
        CancellationToken cancellationToken)
    {
        if (database.IsDefault && OpenNoteTabs.Any(tab => tab.IsDirty))
        {
            throw new MdbxSnapshotException("unsaved-edits");
        }

        using var quiescence = await BeginMdbxRestoreQuiescenceAsync(cancellationToken);
        MdbxSnapshotRestoreResult result;
        try
        {
            result = await _mdbxVaultService.RestoreSnapshotAsync(
                database,
                incomingPath,
                commitMetadata,
                cancellationToken);
        }
        catch (MdbxSnapshotException exception) when (exception.ReasonCode == "rollback-failed")
        {
            await LockAsync();
            throw;
        }

        // The native transaction is committed. A canceled reload must not report the restore as
        // canceled or forget its recovery receipt; locking still clears the presentation state.
        _mdbxOperationCommitted = true;
        RememberMdbxSnapshotRecovery(database.Id, result.RecoveryPath, cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The service has released its gate. Clear old objects before reloading the workspace.
            if (database.IsDefault)
            {
                ClearVaultCollections();
                ClearEditorAndTransferBuffers();
                ClearSensitiveCaches();
                await LoadAsync();
            }
            else
            {
                await ReloadMdbxVaultStateAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            RememberMdbxSnapshotRecovery(database.Id, result.RecoveryPath, cancellationToken);
        }
    }

    private async Task<IDisposable> BeginMdbxRestoreQuiescenceAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _mdbxRestoreActive, 1, 0) != 0)
        {
            throw new MdbxSnapshotException("vault-busy");
        }

        IsMdbxRestoreInProgress = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // An open modal editor or an operation with no cancellation contract must finish first.
            // Refusing the restore prevents a later dialog result from saving a stale model.
            if (IsLoadingVault || IsImportExportBusy || IsWebDavBusy || IsSecurityMaintenanceBusy ||
                _isCleaningExpiredRecycleBinItems || HasActiveVaultEdits())
            {
                throw new MdbxSnapshotException("vault-busy");
            }

            var pending = new[]
            {
                _bitwardenOnlineOperationCompletion?.Task,
                _bitwardenImportOperationCompletion?.Task,
                _keePassImportOperationCompletion?.Task,
                CheckCompromisedPasswordsCommand.ExecutionTask,
                RefreshSecurityAnalysisCommand.ExecutionTask
            }.OfType<Task>().Where(task => !task.IsCompleted).ToArray();

            _bitwardenSyncOperationCancellation?.Cancel();
            _bitwardenOperationCancellation?.Cancel();
            _keePassOperationCancellation?.Cancel();
            _keePassSearchCancellation?.Cancel();
            ClearBitwardenAuthenticationFields(preserveIdentity: true);
            CancelSensitiveBackgroundWork();
            ClearMdbxUnknownEntryDetails();
            try
            {
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Canceled background commands have completed; only the restore token cancels restore.
            }
            catch (TimeoutException)
            {
                throw new MdbxSnapshotException("vault-busy");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new MdbxRestoreQuiescence(EndMdbxRestoreQuiescence);
        }
        catch
        {
            EndMdbxRestoreQuiescence();
            throw;
        }
    }

    private void EndMdbxRestoreQuiescence()
    {
        IsMdbxRestoreInProgress = false;
        Interlocked.Exchange(ref _mdbxRestoreActive, 0);
        RefreshSecurityAnalysisIfNeeded();
        if (IsUnlocked)
        {
            _ = LoadBitwardenAccountsAsync();
        }
    }

    private bool HasActiveVaultEdits() =>
        SelectedPasswordDetails is { } details &&
        (details.AddAttachmentCommand.IsRunning || details.DeleteAttachmentCommand.IsRunning ||
         details.SaveAttachmentCommand.IsRunning || details.DeleteHistoryPasswordCommand.IsRunning ||
         details.ClearPasswordHistoryCommand.IsRunning) || new IAsyncRelayCommand[]
        {
        ShowPasswordDetailsCommand, AddPasswordAttachmentCommand,
        CreateVaultFolderCommand, RenameSelectedVaultFolderCommand, DeleteSelectedVaultFolderCommand,
        MoveVaultFolderCommand, MoveSelectedVaultEntryCommand, MoveVaultBatchToFolderCommand,
        FavoriteVaultBatchCommand, StackVaultBatchCommand, ArchiveVaultBatchCommand, DeleteVaultBatchCommand,
        AddPasswordCommand, EditPasswordCommand, DeletePasswordCommand, DeleteSelectedPasswordsCommand,
        ToggleFavoriteCommand, FavoriteSelectedPasswordsCommand, ArchivePasswordCommand,
        ArchiveSelectedPasswordsCommand, MoveSelectedPasswordsToCategoryCommand, StackSelectedPasswordsCommand,
        AddTotpCommand, EditTotpCommand, AdvanceTotpCommand, ToggleTotpFavoriteCommand,
        FavoriteSelectedTotpCommand, DeleteTotpCommand, DeleteSelectedTotpCommand,
        AddWalletItemCommand, EditWalletItemCommand, DeleteWalletItemCommand, DeleteSelectedWalletItemsCommand,
        SaveNoteCommand, SaveAllNoteTabsCommand, DeleteNoteCommand,
        RestorePasswordCommand, RestoreSelectedDeletedPasswordsCommand, DeletePasswordPermanentlyCommand,
        DeleteSelectedDeletedPasswordsPermanentlyCommand, EmptyRecycleBinCommand,
        RestoreRecycleBinItemCommand, DeleteRecycleBinItemPermanentlyCommand,
        UnarchivePasswordCommand, UnarchiveSelectedArchivedPasswordsCommand
        }.Any(command => command.IsRunning);

    partial void OnIsMdbxBusyChanged(bool value)
    {
        RaiseMdbxSnapshotState();
        RaiseBitwardenState();
        OnPropertyChanged(nameof(IsImportExportIdle));
        OnPropertyChanged(nameof(IsImportWorkspaceIdle));
        OnPropertyChanged(nameof(IsBitwardenImportIdle));
        OnPropertyChanged(nameof(IsKeePassImportIdle));
        AddPasswordCommand.NotifyCanExecuteChanged();
        EditPasswordCommand.NotifyCanExecuteChanged();
        AddTotpCommand.NotifyCanExecuteChanged();
        EditTotpCommand.NotifyCanExecuteChanged();
        AddWalletItemCommand.NotifyCanExecuteChanged();
        EditWalletItemCommand.NotifyCanExecuteChanged();
    }

    private sealed class MdbxRestoreQuiescence(Action end) : IDisposable
    {
        private Action? _end = end;
        public void Dispose() => Interlocked.Exchange(ref _end, null)?.Invoke();
    }
}
