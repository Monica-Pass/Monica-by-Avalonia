using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// What the screen does when the file moved on underneath an open database. The platform's half is
/// measured by <see cref="KeePassTwoSessionsOnOneFileTests"/>; this is the half a person sees: the
/// save stands down, the edits stay put, and there is somewhere else to write them to. A refusal with
/// no exit can only be answered by giving up the changes, which is not a choice to offer.
/// </summary>
public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task KeePass_refused_save_can_still_be_written_out_as_a_copy()
    {
        var fixture = KeePassTestVault.Create("refused-then-copied");
        var path = TestTempPaths.CreateFilePath(".kdbx");
        var copyPath = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(path, fixture.Content);
        var picker = new KeePassEditFilePicker(
            new PickedBinaryFile("ledger.kdbx", fixture.Content, path),
            copyPath);
        var viewModel = CreateViewModel(GetTempPath(), fileSystemPickerService: picker);
        await OpenKeePassVaultAsync(viewModel, fixture.Password);
        await ExpandKeePassFolderAsync(viewModel, "Personal");
        await SelectKeePassEntryAsync(viewModel, KeePassTestVault.ExistingTitle);
        await viewModel.EditKeePassEntryCommand.ExecuteAsync(null);
        viewModel.KeePassEditorPublic!.Title = "Owed a place to go";
        await viewModel.ApplyKeePassEntryEditCommand.ExecuteAsync(null);

        var competing = KeePassTestVault.Create("written-by-someone-else");
        await File.WriteAllBytesAsync(path, competing.Content);
        await viewModel.SaveKeePassVaultCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsStatusMessageFailure);
        Assert.True(viewModel.KeePassVaultIsDirty);

        await viewModel.SaveKeePassVaultCopyCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsStatusMessageFailure);
        Assert.Contains("Saved a copy", viewModel.StatusMessage, StringComparison.Ordinal);
        var copy = await File.ReadAllBytesAsync(copyPath);
        Assert.False(copy.AsSpan().SequenceEqual(competing.Content));
        using (var reopened = await new KeePassVaultService().OpenAsync(copy, "copy.kdbx", fixture.Password))
        {
            Assert.Contains("Owed a place to go", await ReadKeePassTitlesAsync(reopened));
        }

        // Writing a copy is not saving: the file this database came from still holds somebody else's
        // bytes, so the session still owes it its own changes and has to keep saying so.
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(competing.Content));
        Assert.True(viewModel.KeePassVaultIsDirty);
        Assert.Equal("Unsaved changes", viewModel.KeePassUnsavedChangesText);
    }
}
