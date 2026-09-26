using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Features.ImportExport;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

/// <summary>
/// Getting an entry back out of a .kdbx's own recycle bin. The bin is a folder inside the database, so
/// it needs the two moves a folder of deleted things implies: put one row back, or throw the whole set
/// away for good. Both run against the unlocked session and only reach the file on save.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Whether the selected row is the bin folder itself. Emptying is offered on that folder and
    /// nowhere else, because the action destroys everything under the row it sits on.
    /// </summary>
    public bool KeePassSelectedFolderIsRecycleBin =>
        _keePassVaultSession is { RecycleBinUuid: { } binUuid }
        && _selectedKeePassTreeRow?.Group is { } group
        && string.Equals(group.Uuid, binUuid, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private async Task RestoreKeePassEntryAsync()
    {
        var session = _keePassVaultSession;
        var entry = _selectedKeePassTreeRow?.Entry;
        if (session is null || entry is null)
        {
            SetStatusFailure("KeePassEntryRequired");
            return;
        }

        try
        {
            var restored = await session.RestoreEntryAsync(entry.EntryUuid);
            if (restored is null)
            {
                SetStatusFailure("KeePassNotInRecycleBin");
                return;
            }

            var destination = GroupDisplayName(session, restored.Row.GroupPath);
            ClearKeePassSelectedRow();
            _keePassOpenFolders.Add(restored.Row.GroupUuid);
            await RefreshKeePassAfterManageAsync(
                session,
                "KeePassEntryRestoredFormat",
                entry.Title,
                destination);
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Restoring the KeePass entry failed", "KeePassManageFailed", error);
        }
    }

    /// <summary>
    /// Empties the bin permanently. The confirmation is the one this product already demands before a
    /// whole set of stored secrets is destroyed, reused rather than softened, and the count in it comes
    /// from the database being asked rather than from a number the interface guessed.
    /// </summary>
    [RelayCommand]
    private Task EmptyKeePassRecycleBinAsync() =>
        EmptyKeePassRecycleBinAsync(ConfirmEmptyRecycleBinAsync);

    /// <summary>
    /// The confirmation arrives as a callback for the same reason the delete's does: the artifact smoke
    /// run walks this path on the shipped binary, and a dialog would sit on the frame it photographs.
    /// </summary>
    private async Task EmptyKeePassRecycleBinAsync(Func<int, Task<bool>> confirmAsync)
    {
        var session = _keePassVaultSession;
        if (session is null)
        {
            SetStatusFailure("KeePassPreviewRequired");
            return;
        }

        var count = session.RecycleBinEntryCount;
        if (count == 0)
        {
            SetStatusNotice("KeePassRecycleBinEmpty");
            return;
        }

        if (!await confirmAsync(count))
        {
            SetStatusNotice("KeePassImportCanceled");
            return;
        }

        try
        {
            var emptied = await session.EmptyRecycleBinAsync();
            ClearKeePassSelectedRow();
            await RefreshKeePassAfterManageAsync(
                session,
                "KeePassRecycleBinEmptiedFormat",
                emptied);
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Emptying the KeePass recycle bin failed", "KeePassManageFailed", error);
        }
    }

    private static string GroupDisplayName(KeePassVaultSession session, string path) =>
        string.IsNullOrWhiteSpace(path) ? session.RootGroupRow.Name : path;
}
