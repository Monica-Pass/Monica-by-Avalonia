using Monica.Platform.Services;

namespace Monica.App.ViewModels;

/// <summary>
/// The one door a database comes in through from outside this window: a double-clicked file, a second
/// launch that handed its argument to the copy already running, a remembered row, and a request that
/// waited on disk until the vault was unlocked. It stops where a person's own click stops - the file
/// named, the master password asked for, nothing decrypted - because arriving by command line buys a
/// file no more key than arriving by picker.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Takes a path a hand-over left behind. Kept separate from the queue that reads it so the queue
    /// stays a file on disk and this stays the window's own business.
    /// </summary>
    public void RequestKeePassFileOpen(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _incomingKeePassFilePath = path;
        _ = ApplyIncomingKeePassFilesAsync();
    }

    /// <summary>
    /// Walks the waiting requests to the page that opens a file, one at a time, and leaves the rest
    /// standing when the vault is locked: the page is behind that door, and a file asked for before it
    /// is reachable is still asked for afterwards.
    /// </summary>
    private async Task ApplyIncomingKeePassFilesAsync()
    {
        if (_applyingIncomingKeePassFiles || !IsUnlocked)
        {
            return;
        }

        _applyingIncomingKeePassFiles = true;
        try
        {
            while (IsUnlocked && _incomingKeePassFilePath is { } path)
            {
                _incomingKeePassFilePath = null;
                SelectedSection = "Sync";
                SelectedSyncPage = "Import";
                KeePassImportTabSelected = true;
                await StageKeePassFileForOpenAsync(path);
            }
        }
        finally
        {
            _applyingIncomingKeePassFiles = false;
        }
    }

    /// <summary>
    /// Brings the page to the state a person reaches by choosing this file themselves: the name is up,
    /// the open form is asking for the master password, and the only thing read is the bytes on disk.
    /// </summary>
    private async Task StageKeePassFileForOpenAsync(string path)
    {
        if (IsKeePassImportBusy)
        {
            SetStatusFailure("KeePassOperationBusy");
            return;
        }

        if (KeePassVaultIsDirty)
        {
            SetStatusFailure("KeePassDiscardBeforeOpening");
            return;
        }

        if (_keePassVaultSession?.SourcePath is { } openPath &&
            string.Equals(openPath, path, StringComparison.OrdinalIgnoreCase))
        {
            SetStatusNotice("KeePassFileSelectedFormat", System.IO.Path.GetFileName(path));
            return;
        }

        if (!TryBeginKeePassOperation(out var cancellationToken))
        {
            SetStatusFailure("KeePassOperationBusy");
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            ClearKeePassImportPreview();
            _keePassPendingFile = new PickedBinaryFile(
                System.IO.Path.GetFileName(path),
                bytes,
                path);
            KeePassSelectedFileName = _keePassPendingFile.FileName;
            SetStatusNotice("KeePassFileSelectedFormat", _keePassPendingFile.FileName);
        }
        catch (OperationCanceledException)
        {
            SetStatusNotice("KeePassImportCanceled");
        }
        catch (Exception error)
        {
            // The file was not there, or was not ours to read. Say so, and let the remembered list show a
            // row that cannot be opened rather than a status line nobody can act on.
            RefreshKeePassRecentVaults();
            ReportImportExportFailure("Opening a KeePass database failed", "KeePassRecentReadFailed", error);
        }
        finally
        {
            EndKeePassOperation();
        }
    }
}
