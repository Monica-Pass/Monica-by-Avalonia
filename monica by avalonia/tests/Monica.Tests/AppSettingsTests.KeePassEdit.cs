using Monica.App.Features.ImportExport;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// Behaviour of the edit/save surface, asked through the commands the buttons are wired to. Every
/// claim here is about the file: an edit that never reaches disk, a save that does, and a save that
/// stands down when something else already wrote the file.
/// </summary>
public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task KeePass_entry_edit_stages_in_memory_and_save_writes_it_through()
    {
        var fixture = KeePassTestVault.Create("stage-then-save");
        var path = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(path, fixture.Content);
        var picker = new KeePassEditFilePicker(new PickedBinaryFile("ledger.kdbx", fixture.Content, path));
        var viewModel = CreateViewModel(GetTempPath(), fileSystemPickerService: picker);
        await OpenKeePassVaultAsync(viewModel, fixture.Password);
        await ExpandKeePassFolderAsync(viewModel, "Personal");
        await SelectKeePassEntryAsync(viewModel, KeePassTestVault.ExistingTitle);

        await viewModel.EditKeePassEntryCommand.ExecuteAsync(null);
        var editor = viewModel.KeePassEditorPublic;
        Assert.NotNull(editor);
        Assert.Equal(KeePassTestVault.ExistingTitle, editor!.Title);
        Assert.Equal("existing@example.com", editor.UserName);
        Assert.Equal("Personal", editor.GroupPath);

        editor.Title = "Renamed by Monica";
        await viewModel.ApplyKeePassEntryEditCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasKeePassEditor);
        Assert.True(viewModel.KeePassVaultIsDirty);
        Assert.Equal("Unsaved changes", viewModel.KeePassUnsavedChangesText);
        Assert.Equal("Renamed by Monica", viewModel.KeePassEntryDetailsPublic?.Title);
        // Applying is not saving: the file on disk must still be the exact bytes it was opened from.
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(fixture.Content));
        using (var untouched = await new KeePassVaultService()
                         .OpenAsync(fixture.Content, "ledger.kdbx", fixture.Password))
        {
            Assert.DoesNotContain("Renamed by Monica", await ReadKeePassTitlesAsync(untouched));
        }

        await viewModel.SaveKeePassVaultCommand.ExecuteAsync(null);

        Assert.False(viewModel.KeePassVaultIsDirty);
        Assert.Empty(viewModel.KeePassUnsavedChangesText);
        Assert.False(viewModel.IsStatusMessageFailure);
        Assert.Contains("bytes", viewModel.StatusMessage, StringComparison.Ordinal);
        var saved = await File.ReadAllBytesAsync(path);
        Assert.False(saved.AsSpan().SequenceEqual(fixture.Content));
        using var reopened = await new KeePassVaultService().OpenAsync(saved, "ledger.kdbx", fixture.Password);
        var entry = fixture.Entries.Single(item => item.Title == KeePassTestVault.ExistingTitle);
        var detail = await reopened.ReadDetailAsync(entry.GroupUuid, entry.Uuid);
        Assert.NotNull(detail);
        Assert.Equal("Renamed by Monica", detail!.Row.Title);
        Assert.Equal("existing@example.com", detail.Row.UserName);
        Assert.True(detail.Password == "source-secret");
        Assert.Equal("Existing note", detail.Notes);
    }

    [Fact]
    public async Task KeePass_edit_canceled_before_applying_changes_nothing()
    {
        var fixture = KeePassTestVault.Create("cancel-the-form");
        var path = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(path, fixture.Content);
        var picker = new KeePassEditFilePicker(new PickedBinaryFile("ledger.kdbx", fixture.Content, path));
        var viewModel = CreateViewModel(GetTempPath(), fileSystemPickerService: picker);
        await OpenKeePassVaultAsync(viewModel, fixture.Password);
        await ExpandKeePassFolderAsync(viewModel, "Personal");
        await SelectKeePassEntryAsync(viewModel, KeePassTestVault.ExistingTitle);

        await viewModel.EditKeePassEntryCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.KeePassEditorPublic);
        viewModel.KeePassEditorPublic!.Title = "Never applied";
        viewModel.CancelKeePassEntryEditCommand.Execute(null);

        Assert.False(viewModel.HasKeePassEditor);
        Assert.False(viewModel.KeePassVaultIsDirty);
        Assert.Empty(viewModel.KeePassUnsavedChangesText);
        Assert.Equal(KeePassTestVault.ExistingTitle, viewModel.KeePassEntryDetailsPublic?.Title);
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(fixture.Content));
    }

    [Fact]
    public async Task KeePass_save_refuses_to_overwrite_a_file_changed_elsewhere()
    {
        var fixture = KeePassTestVault.Create("lost-update-guard");
        var path = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(path, fixture.Content);
        var picker = new KeePassEditFilePicker(new PickedBinaryFile("ledger.kdbx", fixture.Content, path));
        var viewModel = CreateViewModel(GetTempPath(), fileSystemPickerService: picker);
        await OpenKeePassVaultAsync(viewModel, fixture.Password);
        await ExpandKeePassFolderAsync(viewModel, "Personal");
        await SelectKeePassEntryAsync(viewModel, KeePassTestVault.ExistingTitle);
        await viewModel.EditKeePassEntryCommand.ExecuteAsync(null);
        viewModel.KeePassEditorPublic!.Title = "Written by Monica";
        await viewModel.ApplyKeePassEntryEditCommand.ExecuteAsync(null);

        // Another program saves to the same path while Monica is holding its own edit.
        var competing = KeePassTestVault.Create("written-by-someone-else");
        await File.WriteAllBytesAsync(path, competing.Content);
        await viewModel.SaveKeePassVaultCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsStatusMessageFailure);
        Assert.Contains("changed outside", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("ledger.kdbx", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.True(viewModel.KeePassVaultIsDirty);
        var onDisk = await File.ReadAllBytesAsync(path);
        Assert.True(onDisk.AsSpan().SequenceEqual(competing.Content));
        using var reopened = await new KeePassVaultService()
            .OpenAsync(onDisk, "ledger.kdbx", competing.Password);
        Assert.Equal(2, reopened.EntryCount);
        Assert.DoesNotContain("Written by Monica", await ReadKeePassTitlesAsync(reopened));
    }

    [Fact]
    public async Task KeePass_save_without_a_source_file_writes_a_verified_copy()
    {
        var fixture = KeePassTestVault.Create("no-source-file");
        var copyPath = TestTempPaths.CreateFilePath(".kdbx");
        // Opened from bytes with no path behind them, so in-place saving is not on the table.
        var picker = new KeePassEditFilePicker(new PickedBinaryFile("memory-only.kdbx", fixture.Content), copyPath);
        var viewModel = CreateViewModel(GetTempPath(), fileSystemPickerService: picker);
        await OpenKeePassVaultAsync(viewModel, fixture.Password);
        await ExpandKeePassFolderAsync(viewModel, "Personal");
        await ExpandKeePassFolderAsync(viewModel, "Cloud");
        await SelectKeePassEntryAsync(viewModel, KeePassTestVault.CloudTitle);

        await viewModel.EditKeePassEntryCommand.ExecuteAsync(null);
        var editor = viewModel.KeePassEditorPublic!;
        Assert.Contains("Tenant", editor.CustomFields.Select(item => item.Name));
        editor.Notes = "Cloud note, revisited";
        await viewModel.ApplyKeePassEntryEditCommand.ExecuteAsync(null);
        await viewModel.SaveKeePassVaultCommand.ExecuteAsync(null);

        Assert.Contains("Saved a copy", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.True(File.Exists(copyPath));
        var copy = await File.ReadAllBytesAsync(copyPath);
        Assert.False(copy.AsSpan().SequenceEqual(fixture.Content));
        using var reopened = await new KeePassVaultService().OpenAsync(copy, "memory-only.kdbx", fixture.Password);
        var entry = fixture.Entries.Single(item => item.Title == KeePassTestVault.CloudTitle);
        var detail = await reopened.ReadDetailAsync(entry.GroupUuid, entry.Uuid);
        Assert.NotNull(detail);
        Assert.Equal("Cloud note, revisited", detail!.Notes);
        // Saving a copy leaves the original untouched, so the opened database is still ahead of it.
        Assert.True(viewModel.KeePassVaultIsDirty);
        Assert.Equal("Unsaved changes", viewModel.KeePassUnsavedChangesText);
        // The form cannot edit custom fields, so an edit round trip has to carry them along intact.
        var customField = Assert.Single(detail.CustomFields);
        Assert.Equal("Tenant", customField.Name);
        Assert.True(customField.IsProtected);
        Assert.Equal("Production", customField.Value);
    }

    [Fact]
    public async Task KeePass_editing_without_a_selected_entry_fails_without_touching_the_file()
    {
        var fixture = KeePassTestVault.Create("nothing-selected");
        var path = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(path, fixture.Content);
        var picker = new KeePassEditFilePicker(new PickedBinaryFile("ledger.kdbx", fixture.Content, path));
        var viewModel = CreateViewModel(GetTempPath(), fileSystemPickerService: picker);
        await OpenKeePassVaultAsync(viewModel, fixture.Password);

        Assert.False(viewModel.CanEditKeePassEntry);
        await viewModel.EditKeePassEntryCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsStatusMessageFailure);
        Assert.Contains("Select a KeePass entry", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(viewModel.HasKeePassEditor);
        Assert.False(viewModel.KeePassVaultIsDirty);
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(fixture.Content));
    }

    private static async Task OpenKeePassVaultAsync(MainWindowViewModel viewModel, string password)
    {
        await viewModel.SelectKeePassFileCommand.ExecuteAsync(null);
        viewModel.KeePassImportPassword = password;
        await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasKeePassImportPreview);
    }

    private static async Task ExpandKeePassFolderAsync(MainWindowViewModel viewModel, string name)
    {
        var folder = Assert.Single(
            viewModel.KeePassTreeRowsPublic,
            item => item.Kind == KeePassTreeRowKind.Folder && item.Label == name);
        await viewModel.ToggleKeePassFolderCommand.ExecuteAsync(folder);
    }

    private static async Task SelectKeePassEntryAsync(MainWindowViewModel viewModel, string title)
    {
        var row = Assert.Single(
            viewModel.KeePassTreeRowsPublic,
            item => item.IsEntryRow && item.Label == title);
        await viewModel.SelectKeePassRowCommand.ExecuteAsync(row);
    }

    private static async Task<List<string>> ReadKeePassTitlesAsync(KeePassVaultSession session)
    {
        var titles = new List<string>();
        foreach (var groupUuid in new[] { session.RootGroupUuid }.Concat(session.Groups.Select(group => group.Uuid)))
        {
            var rows = await session.ReadGroupRowsAsync(groupUuid, CancellationToken.None);
            titles.AddRange(rows.Select(row => row.Title));
        }

        return titles;
    }

    private sealed class KeePassEditFilePicker(PickedBinaryFile? file, string? saveTarget = null) : IFileSystemPickerService
    {
        public PlatformIntegrationCapability Capability { get; } =
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "Test file picker");

        public Task<PickedTextFile?> OpenTextFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<PickedTextFile?>(null);

        public Task<PickedBinaryFile?> OpenBinaryFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult(file);

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> SaveBinaryFileAsync(string title, string suggestedFileName, ReadOnlyMemory<byte> content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default)
        {
            if (saveTarget is null)
            {
                return Task.FromResult<string?>(null);
            }

            File.WriteAllBytes(saveTarget, content.ToArray());
            return Task.FromResult<string?>(Path.GetFileName(saveTarget));
        }
    }
}
