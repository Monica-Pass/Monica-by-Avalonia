using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Controls;
using Monica.App.Features.ImportExport;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

/// <summary>
/// The entry half of an opened .kdbx: added, removed, and moved between folders by dragging. What a
/// user types here lands in the unlocked database and the tree follows at once; the file only changes
/// when they save it. The folder commands live alongside in the folders partial.
/// </summary>
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private string _keePassFolderName = "";

    /// <summary>
    /// The tree hides the folder and entry edit items wholesale rather than showing a menu of dead
    /// commands, so this says what the panel can actually accept: an opened database, idle.
    /// </summary>
    public bool CanManageKeePassRows => _keePassVaultSession is not null && !IsKeePassImportBusy;

    [RelayCommand]
    private void NewKeePassEntry()
    {
        var session = _keePassVaultSession;
        if (session is null)
        {
            SetStatusFailure("KeePassPreviewRequired");
            return;
        }

        var (uuid, path) = ResolveKeePassTargetFolder(session);
        if (uuid is null)
        {
            SetStatusFailure("KeePassFolderGone");
            return;
        }

        _keePassOpenFolders.Add(uuid);
        KeePassEditorPublic = KeePassEntryEditorViewModel.CreateDraft(uuid, path);
    }

    /// <summary>
    /// A new entry belongs to the selected folder, or to the folder the selected entry sits in, or to
    /// the root when nothing is selected - the folder the user is looking at is the one they mean.
    /// </summary>
    private (string? Uuid, string Path) ResolveKeePassTargetFolder(KeePassVaultSession session)
    {
        var row = _selectedKeePassTreeRow;
        if (row?.Group is { } group)
        {
            return (group.Uuid, group.Path);
        }

        if (row?.Entry is { } entry)
        {
            var parent = session.Groups.FirstOrDefault(candidate =>
                string.Equals(candidate.Uuid, entry.GroupUuid, StringComparison.OrdinalIgnoreCase));
            return (entry.GroupUuid, parent?.Path ?? "");
        }

        return (session.RootGroupUuid, session.RootGroupRow.Name);
    }

    /// <summary>
    /// Whether the entry the menu is open on already sits in the recycle bin. Out of there the only way
    /// out is permanent, so the tree drops the recycle item and keeps the other one.
    /// </summary>
    public bool KeePassSelectedEntryInRecycleBin =>
        _keePassVaultSession is { } session
        && _selectedKeePassTreeRow?.Entry is { } entry
        && session.IsInRecycleBin(entry.GroupUuid);

    /// <summary>
    /// Deleting an entry asks first and offers two depths: into the bin, where a KeePass file keeps it
    /// until something else takes it out, and permanent, which also writes the database's record that
    /// this entry is gone rather than merely hidden.
    /// </summary>
    [RelayCommand]
    private Task DeleteKeePassEntryAsync() =>
        DeleteKeePassEntryAsync(KeePassDeleteMode.RecycleBin, ConfirmKeePassRecycleAsync);

    [RelayCommand]
    private Task DeleteKeePassEntryPermanentlyAsync() =>
        DeleteKeePassEntryAsync(KeePassDeleteMode.Permanent, ConfirmKeePassPermanentAsync);

    private Task<bool> ConfirmKeePassRecycleAsync(string entryTitle) =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("MoveToRecycleBin"),
            _localization.Format("KeePassRecycleEntryConfirmFormat", entryTitle),
            _localization.Get("MoveToRecycleBin"),
            _localization.Cancel);

    private Task<bool> ConfirmKeePassPermanentAsync(string entryTitle) =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeletePermanentlyConfirmationTitle"),
            _localization.Format("DeletePermanentlyConfirmationMessageFormat", entryTitle),
            _localization.Get("DeletePermanently"),
            _localization.Cancel);

    /// <summary>
    /// The confirmation arrives as a callback so the artifact smoke run can walk the whole delete - the
    /// one place a shipped binary is asked to prove it - without a modal sitting on the screen it photos.
    /// </summary>
    private async Task DeleteKeePassEntryAsync(
        KeePassDeleteMode mode,
        Func<string, Task<bool>> confirmAsync)
    {
        var session = _keePassVaultSession;
        var entry = _selectedKeePassTreeRow?.Entry;
        if (session is null || entry is null)
        {
            SetStatusFailure("KeePassEntryRequired");
            return;
        }

        if (!await confirmAsync(entry.Title))
        {
            SetStatusNotice("KeePassImportCanceled");
            return;
        }

        try
        {
            var status = await session.DeleteEntryAsync(entry.EntryUuid, mode);
            if (status == KeePassEntryDeleteStatus.NotFound)
            {
                SetStatusFailure("KeePassEntryGone");
                return;
            }

            ClearKeePassSelectedRow();
            if (status == KeePassEntryDeleteStatus.Recycled && session.RecycleBinUuid is { } binUuid)
            {
                // The entry left the folder it was opened in, so the folder it arrived in is the one to
                // leave open - otherwise the row the user just acted on vanishes with no trace.
                _keePassOpenFolders.Add(binUuid);
                await RefreshKeePassAfterManageAsync(session, "KeePassEntryRecycledFormat", entry.Title);
                return;
            }

            await RefreshKeePassAfterManageAsync(session, "KeePassEntryDeletedFormat", entry.Title);
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Deleting the KeePass entry failed", "KeePassManageFailed", error);
        }
    }

    /// <summary>
    /// Entries move the same way folders do - dragged onto the folder that should hold them - and a
    /// drop on the folder the entry already sits in is not a move worth reporting. The recycle bin is
    /// not a destination here: putting an entry in there is deleting it, and the delete path is the one
    /// that says so.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMoveKeePassEntry))]
    private async Task MoveKeePassEntryAsync(FolderMoveRequest? request)
    {
        var session = _keePassVaultSession;
        if (session is null
            || request?.Source is not KeePassTreeRow { Entry: { } entry }
            || request.Target is not KeePassTreeRow { Group: { } target })
        {
            return;
        }

        try
        {
            var moved = await session.MoveEntryAsync(entry.EntryUuid, target.Uuid);
            if (moved is null)
            {
                SetStatusFailure("KeePassEntryGone");
                return;
            }

            _keePassOpenFolders.Add(target.Uuid);
            await RefreshKeePassAfterManageAsync(session, "KeePassEntryMovedFormat", entry.Title, target.Name);
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Moving the KeePass entry failed", "KeePassManageFailed", error);
        }
    }

    private bool CanMoveKeePassEntry(FolderMoveRequest? request) =>
        _keePassVaultSession is { } session
        && request?.Source is KeePassTreeRow { Entry: { } entry }
        && request.Target is KeePassTreeRow { Group: { } target }
        && !string.Equals(entry.GroupUuid, target.Uuid, StringComparison.OrdinalIgnoreCase)
        && !session.IsInRecycleBin(target.Uuid);

    /// <summary>
    /// Publishes a structural change the way every other KeePass write does: the tree is rebuilt from
    /// the session, the unsaved-changes notice follows, and the summary count is re-read.
    /// </summary>
    private async Task RefreshKeePassAfterManageAsync(
        KeePassVaultSession session,
        string messageKey,
        params object[] args)
    {
        await RebuildKeePassTreeAsync(session, CancellationToken.None);
        RaiseKeePassWriteState();
        SetStatusNotice(messageKey, args);
    }

    private void ClearKeePassSelectedRow()
    {
        ClearKeePassEntryDetail();
        KeePassEditorPublic = null;
        _selectedKeePassTreeRow = null;
        SelectedKeePassTreeRowPublic = null;
        RaiseKeePassManageState();
    }
}
