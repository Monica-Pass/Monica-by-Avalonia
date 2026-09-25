using CommunityToolkit.Mvvm.Input;
using Monica.App.Controls;
using Monica.App.Features.ImportExport;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

/// <summary>
/// The folder half of an opened .kdbx: created, renamed, moved between folders and deleted. A folder
/// that still holds something asks before anything goes, and the root folder is protected outright,
/// because a database without a top is not a database. None of this touches the file until the user
/// saves it.
/// </summary>
public sealed partial class MainWindowViewModel
{
    public bool CanManageSelectedKeePassFolder =>
        SelectedKeePassTreeRowPublic is { IsEntryRow: false } row
        && row.Group is { } group
        && _keePassVaultSession is { } session
        && !string.Equals(group.Uuid, session.RootGroupUuid, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private async Task CreateKeePassFolderAsync()
    {
        var session = _keePassVaultSession;
        var name = KeePassFolderName.Trim();
        if (session is null)
        {
            SetStatusFailure("KeePassPreviewRequired");
            return;
        }

        if (name.Length == 0)
        {
            return;
        }

        var (parentUuid, _) = ResolveKeePassTargetFolder(session);
        if (parentUuid is null)
        {
            SetStatusFailure("KeePassFolderGone");
            return;
        }

        try
        {
            var created = await session.CreateGroupAsync(parentUuid, name);
            if (created is null)
            {
                SetStatusFailure("KeePassFolderGone");
                return;
            }

            KeePassFolderName = "";
            _keePassOpenFolders.Add(parentUuid);
            _keePassOpenFolders.Add(created.Uuid);
            await RefreshKeePassAfterManageAsync(session, "KeePassFolderCreatedFormat", created.Name);
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Creating the KeePass folder failed", "KeePassManageFailed", error);
        }
    }

    [RelayCommand]
    private async Task RenameKeePassFolderAsync()
    {
        var session = _keePassVaultSession;
        var group = SelectedKeePassTreeRowPublic?.Group;
        var name = KeePassFolderName.Trim();
        if (session is null || group is null || name.Length == 0)
        {
            return;
        }

        if (string.Equals(group.Uuid, session.RootGroupUuid, StringComparison.OrdinalIgnoreCase))
        {
            SetStatusFailure("KeePassRootFolderProtected");
            return;
        }

        try
        {
            var renamed = await session.RenameGroupAsync(group.Uuid, name);
            if (renamed is null)
            {
                SetStatusFailure("KeePassFolderGone");
                return;
            }

            KeePassFolderName = "";
            await RefreshKeePassAfterManageAsync(session, "KeePassFolderRenamedFormat", renamed.Name);
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Renaming the KeePass folder failed", "KeePassManageFailed", error);
        }
    }

    /// <summary>
    /// A folder that still holds entries or folders asks once, with the counts it holds, before
    /// anything goes: a click on the folder must not silently take the passwords with it.
    /// </summary>
    [RelayCommand]
    private async Task DeleteKeePassFolderAsync()
    {
        var session = _keePassVaultSession;
        var group = SelectedKeePassTreeRowPublic?.Group;
        if (session is null || group is null)
        {
            SetStatusFailure("SelectFolderToManage");
            return;
        }

        if (string.Equals(group.Uuid, session.RootGroupUuid, StringComparison.OrdinalIgnoreCase))
        {
            SetStatusFailure("KeePassRootFolderProtected");
            return;
        }

        try
        {
            var first = await session.DeleteGroupAsync(group.Uuid, deleteContents: false);
            if (first.Status == KeePassGroupDeleteStatus.NotFound)
            {
                SetStatusFailure("KeePassFolderGone");
                return;
            }

            if (first.Status == KeePassGroupDeleteStatus.NotEmpty)
            {
                var confirmed = await _confirmationDialogService.ConfirmAsync(
                    _localization.Get("DeleteFolderConfirmationTitle"),
                    _localization.Format("KeePassFolderNotEmptyConfirmFormat", group.Name, first.EntryCount, first.GroupCount),
                    _localization.Get("DeleteFolder"),
                    _localization.Cancel);
                if (!confirmed)
                {
                    SetStatusNotice("KeePassImportCanceled");
                    return;
                }

                var second = await session.DeleteGroupAsync(group.Uuid, deleteContents: true);
                if (second.Status != KeePassGroupDeleteStatus.Deleted)
                {
                    SetStatusFailure("KeePassFolderGone");
                    return;
                }
            }

            ClearKeePassSelectedRow();
            await RefreshKeePassAfterManageAsync(session, "KeePassFolderDeletedFormat", group.Name);
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Deleting the KeePass folder failed", "KeePassManageFailed", error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMoveKeePassFolder))]
    private async Task MoveKeePassFolderAsync(FolderMoveRequest? request)
    {
        var session = _keePassVaultSession;
        if (session is null
            || request?.Source is not KeePassTreeRow { Group: { } source }
            || request.Target is not KeePassTreeRow { Group: { } target })
        {
            return;
        }

        try
        {
            if (!await session.MoveGroupAsync(source.Uuid, target.Uuid))
            {
                SetStatusFailure("KeePassFolderGone");
                return;
            }

            _keePassOpenFolders.Add(target.Uuid);
            await RefreshKeePassAfterManageAsync(session, "KeePassFolderMovedFormat", source.Name, target.Name);
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Moving the KeePass folder failed", "KeePassManageFailed", error);
        }
    }

    private bool CanMoveKeePassFolder(FolderMoveRequest? request) =>
        _keePassVaultSession is not null
        && request?.Source is KeePassTreeRow { Group: { } source }
        && request.Target is KeePassTreeRow { Group: { } target }
        && !string.Equals(source.Uuid, target.Uuid, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(source.Uuid, _keePassVaultSession.RootGroupUuid, StringComparison.OrdinalIgnoreCase)
        && !target.Path.Equals(source.Path, StringComparison.OrdinalIgnoreCase)
        && !target.Path.StartsWith(source.Path + "/", StringComparison.OrdinalIgnoreCase);
}
