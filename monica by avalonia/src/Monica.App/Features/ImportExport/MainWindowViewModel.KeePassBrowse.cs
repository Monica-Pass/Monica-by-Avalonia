using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Controls;
using Monica.App.Features.ImportExport;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RebuildKeePassTreeAsync(
        KeePassVaultSession session,
        CancellationToken cancellationToken)
    {
        var childrenByParent = new Dictionary<string, List<KeePassGroupRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in session.Groups)
        {
            var parentKey = group.ParentUuid ?? session.RootGroupUuid;
            if (!childrenByParent.TryGetValue(parentKey, out var bucket))
            {
                bucket = [];
                childrenByParent[parentKey] = bucket;
            }

            bucket.Add(group);
        }

        foreach (var bucket in childrenByParent.Values)
        {
            bucket.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        var rows = new List<KeePassTreeRow>();
        await EmitKeePassFolderAsync(
            session.RootGroupRow,
            session,
            childrenByParent,
            rows,
            depth: 0,
            cancellationToken);

        _keePassTreeRows = rows;
        KeePassTreeRowsPublic = rows;
    }

    private async Task EmitKeePassFolderAsync(
        KeePassGroupRow folder,
        KeePassVaultSession session,
        Dictionary<string, List<KeePassGroupRow>> childrenByParent,
        List<KeePassTreeRow> rows,
        int depth,
        CancellationToken cancellationToken)
    {
        var isOpen = _keePassOpenFolders.Contains(folder.Uuid);
        rows.Add(new KeePassTreeRow
        {
            Kind = KeePassTreeRowKind.Folder,
            Group = folder,
            Entry = null,
            Indent = FolderTreeLayout.IndentFor(depth),
            IsExpanded = isOpen
        });

        if (!isOpen)
        {
            return;
        }

        childrenByParent.TryGetValue(folder.Uuid, out var childFolders);
        if (childFolders is not null)
        {
            foreach (var child in childFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EmitKeePassFolderAsync(child, session, childrenByParent, rows, depth + 1, cancellationToken);
            }
        }

        var entries = await session.ReadGroupRowsAsync(folder.Uuid, cancellationToken);
        var sortedEntries = entries
            .OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.EntryUuid, StringComparer.Ordinal);
        foreach (var entry in sortedEntries)
        {
            rows.Add(new KeePassTreeRow
            {
                Kind = KeePassTreeRowKind.Entry,
                Group = null,
                Entry = entry,
                Indent = FolderTreeLayout.IndentFor(depth + 1)
            });
        }
    }

    [RelayCommand]
    private async Task ToggleKeePassFolderAsync(KeePassTreeRow? row)
    {
        var session = _keePassVaultSession;
        if (session is null || row?.Group is null)
        {
            return;
        }

        var groupUuid = row.Group.Uuid;
        if (!_keePassOpenFolders.Remove(groupUuid))
        {
            _keePassOpenFolders.Add(groupUuid);
        }

        try
        {
            await RebuildKeePassTreeAsync(session, CancellationToken.None);
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Toggling KeePass folder failed", "KeePassFolderToggleFailed", error);
        }
    }

    [RelayCommand]
    private async Task SelectKeePassRowAsync(KeePassTreeRow? row)
    {
        if (row is null)
        {
            _selectedKeePassTreeRow = null;
            SelectedKeePassTreeRowPublic = null;
            KeePassEditorPublic = null;
            RaiseKeePassManageState();
            return;
        }

        _selectedKeePassTreeRow = row;
        SelectedKeePassTreeRowPublic = row;
        KeePassEditorPublic = null;
        RaiseKeePassManageState();

        if (!row.IsEntryRow || row.Entry is null)
        {
            return;
        }

        var session = _keePassVaultSession;
        if (session is null)
        {
            return;
        }

        try
        {
            var detail = await session.ReadDetailAsync(row.Entry.GroupUuid, row.Entry.EntryUuid);
            if (detail is null)
            {
                return;
            }

            ShowKeePassEntryDetail(session, detail);
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Reading KeePass entry detail failed", "KeePassEntryDetailFailed", error);
        }
    }

    [RelayCommand]
    private async Task CopyKeePassRowUsernameAsync(KeePassTreeRow? row)
    {
        if (row?.Entry is null || string.IsNullOrWhiteSpace(row.Entry.UserName))
        {
            return;
        }

        await _clipboardService.SetTextAsync(row.Entry.UserName);
        SetStatusNotice("CopiedToClipboard");
    }

    [RelayCommand]
    private async Task CopyKeePassRowSecretAsync(KeePassTreeRow? row)
    {
        var session = _keePassVaultSession;
        if (row?.Entry is null || session is null)
        {
            return;
        }

        try
        {
            var detail = await session.ReadDetailAsync(row.Entry.GroupUuid, row.Entry.EntryUuid);
            if (detail is null || string.IsNullOrEmpty(detail.Password))
            {
                return;
            }

            await _clipboardService.SetTextAsync(detail.Password);
            SetStatusNotice("CopiedToClipboard");
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Copying KeePass password failed", "KeePassCopySecretFailed", error);
        }
    }
}
