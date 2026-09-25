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

    [RelayCommand]
    private async Task DeleteKeePassEntryAsync()
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
            if (!await session.DeleteEntryAsync(entry.EntryUuid))
            {
                SetStatusFailure("KeePassEntryGone");
                return;
            }

            ClearKeePassSelectedRow();
            await RefreshKeePassAfterManageAsync(session, "KeePassEntryDeletedFormat", entry.Title);
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Deleting the KeePass entry failed", "KeePassManageFailed", error);
        }
    }

    /// <summary>
    /// Entries move the same way folders do - dragged onto the folder that should hold them - and a
    /// drop on the folder the entry already sits in is not a move worth reporting.
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
        _keePassVaultSession is not null
        && request?.Source is KeePassTreeRow { Entry: { } entry }
        && request.Target is KeePassTreeRow { Group: { } target }
        && !string.Equals(entry.GroupUuid, target.Uuid, StringComparison.OrdinalIgnoreCase);

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
        _keePassEntryDetails?.Dispose();
        _keePassEntryDetails = null;
        KeePassEntryDetailsPublic = null;
        KeePassEditorPublic = null;
        _selectedKeePassTreeRow = null;
        SelectedKeePassTreeRowPublic = null;
        RaiseKeePassManageState();
    }
}
