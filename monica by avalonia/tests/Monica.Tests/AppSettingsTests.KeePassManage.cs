using Monica.App.Controls;
using Monica.App.Features.ImportExport;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// Folders and entries added, renamed, moved and removed through the commands the tree's context menu
/// and drag gesture are wired to. As with editing an entry, none of this reaches the file until the
/// user saves, so every test reads back the bytes it opened from and expects them unchanged.
/// </summary>
public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task KeePass_new_entry_lands_only_when_the_draft_is_applied()
    {
        var fixture = KeePassTestVault.Create("manage-draft-entry");
        var path = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(path, fixture.Content);
        var picker = new KeePassEditFilePicker(new PickedBinaryFile("ledger.kdbx", fixture.Content, path));
        var viewModel = CreateViewModel(GetTempPath(), fileSystemPickerService: picker);
        await OpenKeePassVaultAsync(viewModel, fixture.Password);
        await ExpandKeePassFolderAsync(viewModel, "Personal");
        await SelectKeePassFolderAsync(viewModel, "Personal");

        viewModel.NewKeePassEntryCommand.Execute(null);
        var draft = viewModel.KeePassEditorPublic;
        Assert.NotNull(draft);
        Assert.True(draft!.IsDraft);
        Assert.Equal("Personal", draft.GroupPath);
        var labelsBeforeDrafting = KeePassLabels(viewModel);

        // A draft the user walks away from leaves no blank entry behind.
        viewModel.CancelKeePassEntryEditCommand.Execute(null);
        Assert.False(viewModel.KeePassVaultIsDirty);
        Assert.Equal(labelsBeforeDrafting, KeePassLabels(viewModel));

        viewModel.NewKeePassEntryCommand.Execute(null);
        draft = viewModel.KeePassEditorPublic!;
        draft.Title = "Transit card";
        draft.UserName = "me@example.com";
        draft.Password = "drafted-secret";
        await viewModel.ApplyKeePassEntryEditCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasKeePassEditor);
        Assert.True(viewModel.KeePassVaultIsDirty);
        Assert.Contains("Transit card", KeePassLabels(viewModel));
        Assert.Contains("has been added", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(fixture.Content));

        await viewModel.SaveKeePassVaultCommand.ExecuteAsync(null);

        using var reopened = await new KeePassVaultService().OpenAsync(
            await File.ReadAllBytesAsync(path),
            "ledger.kdbx",
            fixture.Password);
        var created = Assert.Single(await CollectKeePassRowsAsync(reopened), row => row.Title == "Transit card");
        Assert.Equal("Personal", created.GroupPath);
        Assert.Equal(3, reopened.EntryCount);
    }

    [Fact]
    public async Task KeePass_folder_create_rename_and_empty_delete_update_the_tree()
    {
        var fixture = KeePassTestVault.Create("manage-folder-lifecycle");
        var path = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(path, fixture.Content);
        var picker = new KeePassEditFilePicker(new PickedBinaryFile("ledger.kdbx", fixture.Content, path));
        var confirms = new FolderDeleteRecorder(result: false);
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            confirmationDialogService: confirms);
        await OpenKeePassVaultAsync(viewModel, fixture.Password);
        Assert.True(viewModel.CanManageKeePassRows);

        await SelectKeePassFolderAsync(viewModel, "Personal");
        Assert.True(viewModel.CanManageSelectedKeePassFolder);
        viewModel.KeePassFolderName = "  Archive  ";
        await viewModel.CreateKeePassFolderCommand.ExecuteAsync(null);

        Assert.Equal("", viewModel.KeePassFolderName);
        Assert.Contains("Archive", KeePassLabels(viewModel));
        Assert.True(viewModel.KeePassVaultIsDirty);

        await SelectKeePassFolderAsync(viewModel, "Archive");
        viewModel.KeePassFolderName = "Vaults";
        await viewModel.RenameKeePassFolderCommand.ExecuteAsync(null);

        var labels = KeePassLabels(viewModel);
        Assert.Contains("Vaults", labels);
        Assert.DoesNotContain("Archive", labels);

        // An empty folder goes without asking: nothing that held a password is being given up.
        await SelectKeePassFolderAsync(viewModel, "Vaults");
        await viewModel.DeleteKeePassFolderCommand.ExecuteAsync(null);

        Assert.False(confirms.WasCalled);
        Assert.DoesNotContain("Vaults", KeePassLabels(viewModel));
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(fixture.Content));
    }

    [Fact]
    public async Task KeePass_closing_the_file_stops_the_tree_from_offering_edits()
    {
        var fixture = KeePassTestVault.Create("manage-closed-not-manageable");
        var path = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(path, fixture.Content);
        var picker = new KeePassEditFilePicker(new PickedBinaryFile("ledger.kdbx", fixture.Content, path));
        var viewModel = CreateViewModel(GetTempPath(), fileSystemPickerService: picker);
        await OpenKeePassVaultAsync(viewModel, fixture.Password);
        await SelectKeePassFolderAsync(viewModel, "Personal");
        Assert.True(viewModel.CanManageKeePassRows);
        Assert.True(viewModel.CanManageSelectedKeePassFolder);

        await viewModel.ResetKeePassImportCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasKeePassImportPreview);
        Assert.False(viewModel.CanManageKeePassRows);
        Assert.False(viewModel.CanManageSelectedKeePassFolder);
    }

    [Fact]
    public async Task KeePass_deleting_a_folder_that_holds_entries_asks_with_its_counts()
    {
        var fixture = KeePassTestVault.Create("manage-folder-delete-asked");
        var path = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(path, fixture.Content);
        var picker = new KeePassEditFilePicker(new PickedBinaryFile("ledger.kdbx", fixture.Content, path));
        var declines = new FolderDeleteRecorder(result: false);
        var declineViewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            confirmationDialogService: declines);
        await OpenKeePassVaultAsync(declineViewModel, fixture.Password);
        await SelectKeePassFolderAsync(declineViewModel, "Personal");

        await declineViewModel.DeleteKeePassFolderCommand.ExecuteAsync(null);

        // Personal holds one entry of its own plus a folder holding another, so the ask names both.
        Assert.True(declines.WasCalled);
        Assert.Contains("2 entries", declines.Message, StringComparison.Ordinal);
        Assert.Contains("1 folders", declines.Message, StringComparison.Ordinal);
        Assert.Contains("Personal", KeePassLabels(declineViewModel));
        Assert.False(declineViewModel.KeePassVaultIsDirty);
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(fixture.Content));

        var approves = new FolderDeleteRecorder(result: true);
        var approvingViewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            confirmationDialogService: approves);
        await OpenKeePassVaultAsync(approvingViewModel, fixture.Password);
        await SelectKeePassFolderAsync(approvingViewModel, "Personal");

        await approvingViewModel.DeleteKeePassFolderCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Personal", KeePassLabels(approvingViewModel));
        Assert.DoesNotContain("Cloud", KeePassLabels(approvingViewModel));
        Assert.True(approvingViewModel.KeePassVaultIsDirty);
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(fixture.Content));

        await approvingViewModel.SaveKeePassVaultCommand.ExecuteAsync(null);

        using var reopened = await new KeePassVaultService().OpenAsync(
            await File.ReadAllBytesAsync(path),
            "ledger.kdbx",
            fixture.Password);
        Assert.Equal(0, reopened.EntryCount);
        Assert.Equal(0, reopened.GroupCount);
    }

    [Fact]
    public async Task KeePass_folder_move_reparents_and_refuses_a_drop_inside_itself()
    {
        var fixture = KeePassTestVault.Create("manage-folder-move");
        var path = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(path, fixture.Content);
        var picker = new KeePassEditFilePicker(new PickedBinaryFile("ledger.kdbx", fixture.Content, path));
        var viewModel = CreateViewModel(GetTempPath(), fileSystemPickerService: picker);
        await OpenKeePassVaultAsync(viewModel, fixture.Password);
        await ExpandKeePassFolderAsync(viewModel, "Personal");
        await SelectKeePassFolderAsync(viewModel, "Personal");
        viewModel.KeePassFolderName = "Ops";
        await viewModel.CreateKeePassFolderCommand.ExecuteAsync(null);

        var cloud = await SelectKeePassFolderAsync(viewModel, "Cloud");
        var ops = await SelectKeePassFolderAsync(viewModel, "Ops");
        var personal = await SelectKeePassFolderAsync(viewModel, "Personal");
        await viewModel.MoveKeePassFolderCommand.ExecuteAsync(new FolderMoveRequest(cloud, ops));

        Assert.True(viewModel.KeePassVaultIsDirty);
        Assert.Contains("is now inside", viewModel.StatusMessage, StringComparison.Ordinal);
        var movedRow = await SelectKeePassFolderAsync(viewModel, "Cloud");
        Assert.Equal("Personal/Ops/Cloud", movedRow.Group?.Path);

        // A folder dropped into its own descendant, or onto itself, would cut it out of the tree it
        // lands in, so the drag never even offers those targets.
        var reopenedPersonal = await SelectKeePassFolderAsync(viewModel, "Personal");
        Assert.False(viewModel.MoveKeePassFolderCommand.CanExecute(
            new FolderMoveRequest(reopenedPersonal, movedRow)));
        Assert.False(viewModel.MoveKeePassFolderCommand.CanExecute(
            new FolderMoveRequest(reopenedPersonal, personal)));
        Assert.False(viewModel.MoveKeePassFolderCommand.CanExecute(
            new FolderMoveRequest(reopenedPersonal, reopenedPersonal)));

        await viewModel.SaveKeePassVaultCommand.ExecuteAsync(null);
        using var reopened = await new KeePassVaultService().OpenAsync(
            await File.ReadAllBytesAsync(path),
            "ledger.kdbx",
            fixture.Password);
        Assert.Equal("Personal/Ops/Cloud", reopened.Groups.Single(group => group.Name == "Cloud").Path);
    }

    [Fact]
    public async Task KeePass_entry_move_puts_it_in_the_target_folder_and_save_keeps_it_there()
    {
        var fixture = KeePassTestVault.Create("manage-entry-move");
        var path = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(path, fixture.Content);
        var picker = new KeePassEditFilePicker(new PickedBinaryFile("ledger.kdbx", fixture.Content, path));
        var viewModel = CreateViewModel(GetTempPath(), fileSystemPickerService: picker);
        await OpenKeePassVaultAsync(viewModel, fixture.Password);
        await ExpandKeePassFolderAsync(viewModel, "Personal");
        await ExpandKeePassFolderAsync(viewModel, "Cloud");
        var entry = Assert.Single(
            viewModel.KeePassTreeRowsPublic,
            row => row.IsEntryRow && row.Label == KeePassTestVault.CloudTitle);
        var personal = Assert.Single(
            viewModel.KeePassTreeRowsPublic,
            row => row.Kind == KeePassTreeRowKind.Folder && row.Label == "Personal");

        await viewModel.MoveKeePassEntryCommand.ExecuteAsync(new FolderMoveRequest(entry, personal));

        Assert.True(viewModel.KeePassVaultIsDirty);
        Assert.Contains("is now in folder", viewModel.StatusMessage, StringComparison.Ordinal);
        var movedEntryRow = Assert.Single(
            viewModel.KeePassTreeRowsPublic,
            row => row.IsEntryRow && row.Label == KeePassTestVault.CloudTitle);
        Assert.Equal(personal.Group?.Uuid, movedEntryRow.Entry?.GroupUuid);
        // Dropping it on the folder it already sits in is not a move.
        Assert.False(viewModel.MoveKeePassEntryCommand.CanExecute(new FolderMoveRequest(movedEntryRow, personal)));
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(fixture.Content));

        await viewModel.SaveKeePassVaultCommand.ExecuteAsync(null);
        using var reopened = await new KeePassVaultService().OpenAsync(
            await File.ReadAllBytesAsync(path),
            "ledger.kdbx",
            fixture.Password);
        var moved = Assert.Single(
            await CollectKeePassRowsAsync(reopened),
            row => row.Title == KeePassTestVault.CloudTitle);
        Assert.Equal("Personal", moved.GroupPath);
    }

    [Fact]
    public async Task KeePass_root_folder_refuses_rename_and_delete()
    {
        var fixture = KeePassTestVault.Create("manage-root-protected");
        var path = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(path, fixture.Content);
        var picker = new KeePassEditFilePicker(new PickedBinaryFile("ledger.kdbx", fixture.Content, path));
        var confirms = new FolderDeleteRecorder(result: true);
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            confirmationDialogService: confirms);
        await OpenKeePassVaultAsync(viewModel, fixture.Password);
        var root = viewModel.KeePassTreeRowsPublic.First(row => !row.IsEntryRow);
        Assert.Equal("Business Root", root.Label);
        await viewModel.SelectKeePassRowCommand.ExecuteAsync(root);
        Assert.False(viewModel.CanManageSelectedKeePassFolder);

        viewModel.KeePassFolderName = "Renamed root";
        await viewModel.RenameKeePassFolderCommand.ExecuteAsync(null);
        var renameStatus = viewModel.StatusMessage;
        Assert.False(viewModel.KeePassVaultIsDirty);

        await viewModel.DeleteKeePassFolderCommand.ExecuteAsync(null);

        // Each command refuses on its own, so neither can borrow the other's refusal.
        Assert.Contains("root folder", renameStatus, StringComparison.Ordinal);
        Assert.True(viewModel.IsStatusMessageFailure);
        Assert.Contains("root folder", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(confirms.WasCalled);
        Assert.False(viewModel.KeePassVaultIsDirty);
        Assert.Contains("Business Root", KeePassLabels(viewModel));
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(fixture.Content));
    }

    private static async Task<KeePassTreeRow> SelectKeePassFolderAsync(MainWindowViewModel viewModel, string name)
    {
        var row = Assert.Single(
            viewModel.KeePassTreeRowsPublic,
            item => item.Kind == KeePassTreeRowKind.Folder && item.Label == name);
        await viewModel.SelectKeePassRowCommand.ExecuteAsync(row);
        return row;
    }

    private static IReadOnlyList<string> KeePassLabels(MainWindowViewModel viewModel) =>
        viewModel.KeePassTreeRowsPublic.Select(row => row.Label).ToArray();

    private static async Task<List<KeePassEntryRow>> CollectKeePassRowsAsync(KeePassVaultSession session)
    {
        var rows = new List<KeePassEntryRow>();
        foreach (var group in new[] { session.RootGroupRow }.Concat(session.Groups))
        {
            rows.AddRange(await session.ReadGroupRowsAsync(group.Uuid));
        }

        return rows;
    }

    private sealed class FolderDeleteRecorder(bool result) : IConfirmationDialogService
    {
        public bool WasCalled { get; private set; }

        public string Message { get; private set; } = "";

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText,
            string? closeButtonText = null,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            Message = message;
            return Task.FromResult(result);
        }

        public Task<bool> ConfirmTypedAsync(
            string title,
            string message,
            string requiredPhrase,
            string instruction,
            string primaryButtonText,
            string? closeButtonText = null,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
