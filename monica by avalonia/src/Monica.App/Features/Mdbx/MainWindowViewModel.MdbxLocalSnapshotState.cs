using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private long? _mdbxSnapshotRecoveryDatabaseId;
    private string? _mdbxSnapshotRecoveryPath;

    public bool CanUseMdbxSnapshotActions => IsUnlocked && _vaultSessionService.IsUnlocked &&
        !IsMdbxBusy && !IsMdbxRestoreInProgress && CanUseFilePicker && _mdbxVaultService.SupportsSnapshots &&
        SelectedMdbxDatabaseItem is { } item && HasMdbxSnapshotSource(item.Database) &&
        !string.IsNullOrWhiteSpace(item.Database.EncryptedPassword);

    public string MdbxSnapshotAvailabilityText => _localization.Get(
        SelectedMdbxDatabaseItem is null ? "MdbxSnapshotAvailabilityNoSelection" :
        !IsUnlocked || !_vaultSessionService.IsUnlocked ? "MdbxSnapshotAvailabilityLocked" :
        IsMdbxBusy || IsMdbxRestoreInProgress ? "MdbxSnapshotAvailabilityBusy" :
        !CanUseFilePicker ? "MdbxSnapshotAvailabilityPickerUnavailable" :
        !_mdbxVaultService.SupportsSnapshots ? "MdbxSnapshotNativeUnavailable" :
        !HasMdbxSnapshotSource(SelectedMdbxDatabaseItem.Database) ? "MdbxSnapshotAvailabilityMissingCopy" :
        string.IsNullOrWhiteSpace(SelectedMdbxDatabaseItem.Database.EncryptedPassword) ? "MdbxSnapshotCredentialOrIntegrity" :
        "MdbxSnapshotAvailabilityReady");

    public string? MdbxSnapshotRecoveryPath => IsUnlocked &&
        SelectedMdbxDatabaseItem?.Database.Id == _mdbxSnapshotRecoveryDatabaseId ? _mdbxSnapshotRecoveryPath : null;
    public bool HasMdbxSnapshotRecovery => !string.IsNullOrWhiteSpace(MdbxSnapshotRecoveryPath);
    public string MdbxSnapshotRecoveryFileName => Path.GetFileName(MdbxSnapshotRecoveryPath) ?? "";

    private static string? MdbxSnapshotLocalSource(LocalMdbxDatabase database) =>
        database.WorkingCopyPath ?? (IsLocalMdbxDatabase(database) ? database.FilePath : null);

    private static bool HasMdbxSnapshotSource(LocalMdbxDatabase database) =>
        MdbxSnapshotLocalSource(database) is { Length: > 0 } path && File.Exists(path);

    private void RememberMdbxSnapshotRecovery(long databaseId, string? path, CancellationToken cancellationToken)
    {
        if (!IsUnlocked || cancellationToken.IsCancellationRequested) return;
        _mdbxSnapshotRecoveryDatabaseId = databaseId;
        _mdbxSnapshotRecoveryPath = path;
        RaiseMdbxSnapshotState();
    }

    private void ClearMdbxSnapshotRecovery()
    {
        _mdbxSnapshotRecoveryDatabaseId = null;
        _mdbxSnapshotRecoveryPath = null;
        RaiseMdbxSnapshotState();
    }

    private void RaiseMdbxSnapshotState()
    {
        OnPropertyChanged(nameof(CanUseMdbxSnapshotActions));
        OnPropertyChanged(nameof(MdbxSnapshotAvailabilityText));
        OnPropertyChanged(nameof(MdbxSnapshotRecoveryPath));
        OnPropertyChanged(nameof(HasMdbxSnapshotRecovery));
        OnPropertyChanged(nameof(MdbxSnapshotRecoveryFileName));
        ExportMdbxSnapshotCommand.NotifyCanExecuteChanged();
        RestoreMdbxSnapshotCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedMdbxDatabaseItemChanged(MdbxDatabaseDisplayItem? value) => RaiseMdbxSnapshotState();
    partial void OnIsMdbxRestoreInProgressChanged(bool value) => RaiseMdbxSnapshotState();
}
