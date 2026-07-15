using Monica.Core.Models;

namespace Monica.Tests;

public sealed partial class PasswordManagementTests
{
    [Fact]
    public async Task Lifecycle_bulk_archive_selection_unarchives_visible_replica_group_once()
    {
        var harness = CreateHarness();
        var first = new PasswordEntry
        {
            Title = "Grouped account",
            Password = "one",
            ReplicaGroupId = "archive-group"
        };
        var second = new PasswordEntry
        {
            Title = "Grouped account",
            Password = "two",
            ReplicaGroupId = "archive-group"
        };
        var other = new PasswordEntry { Title = "Other archive", Password = "three" };
        await harness.Repository.SavePasswordAsync(first);
        await harness.Repository.SavePasswordAsync(second);
        await harness.Repository.SavePasswordAsync(other);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.ArchivePasswordCommand.ExecuteAsync(
            harness.ViewModel.Passwords.First(item => item.Id == first.Id));
        await harness.ViewModel.ArchivePasswordCommand.ExecuteAsync(
            harness.ViewModel.Passwords.First(item => item.Id == other.Id));

        harness.ViewModel.ArchiveSearchText = "Grouped";
        harness.ViewModel.AreAllFilteredArchivedPasswordsSelected = true;

        Assert.Equal(2, harness.ViewModel.SelectedArchivedPasswordCount);
        Assert.True(harness.ViewModel.HasSelectedArchivedPasswords);
        Assert.All(harness.ViewModel.FilteredArchivedPasswords, item => Assert.True(item.IsSelected));

        await harness.ViewModel.UnarchiveSelectedArchivedPasswordsCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.ViewModel.Passwords.Count);
        Assert.Single(harness.ViewModel.ArchivedPasswords, item => item.Id == other.Id);
        Assert.Equal(0, harness.ViewModel.SelectedArchivedPasswordCount);
        Assert.Equal(
            harness.ViewModel.L.Format("UnarchivedSelectedPasswordsFormat", 2),
            harness.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Lifecycle_bulk_recycle_selection_restores_only_visible_entries()
    {
        var harness = CreateHarness();
        var first = new PasswordEntry { Title = "Recover one", Password = "one" };
        var second = new PasswordEntry { Title = "Recover two", Password = "two" };
        var other = new PasswordEntry { Title = "Keep deleted", Password = "three" };
        await harness.Repository.SavePasswordAsync(first);
        await harness.Repository.SavePasswordAsync(second);
        await harness.Repository.SavePasswordAsync(other);
        await harness.Repository.SoftDeletePasswordAsync(first.Id);
        await harness.Repository.SoftDeletePasswordAsync(second.Id);
        await harness.Repository.SoftDeletePasswordAsync(other.Id);
        await harness.ViewModel.LoadAsync();

        harness.ViewModel.RecycleBinSearchText = "Recover";
        harness.ViewModel.AreAllFilteredDeletedPasswordsSelected = true;

        Assert.Equal(2, harness.ViewModel.SelectedDeletedPasswordCount);
        Assert.All(harness.ViewModel.FilteredDeletedPasswords, item => Assert.True(item.IsSelected));

        await harness.ViewModel.RestoreSelectedDeletedPasswordsCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.ViewModel.Passwords.Count);
        Assert.Single(harness.ViewModel.DeletedPasswords, item => item.Id == other.Id);
        Assert.Equal(0, harness.ViewModel.SelectedDeletedPasswordCount);
        Assert.Equal(
            harness.ViewModel.L.Format("RestoredSelectedPasswordsFormat", 2),
            harness.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Lifecycle_bulk_permanent_delete_requires_typed_confirmation()
    {
        var confirmation = new FakeConfirmationDialogService(result: false);
        var harness = CreateHarness(confirmationDialogService: confirmation);
        var first = new PasswordEntry { Title = "Delete one", Password = "one" };
        var second = new PasswordEntry { Title = "Delete two", Password = "two" };
        await harness.Repository.SavePasswordAsync(first);
        await harness.Repository.SavePasswordAsync(second);
        await harness.Repository.SoftDeletePasswordAsync(first.Id);
        await harness.Repository.SoftDeletePasswordAsync(second.Id);
        await harness.ViewModel.LoadAsync();
        harness.ViewModel.AreAllFilteredDeletedPasswordsSelected = true;

        await harness.ViewModel.DeleteSelectedDeletedPasswordsPermanentlyCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.ViewModel.DeletedPasswords.Count);
        Assert.Equal(2, (await harness.Repository.GetPasswordsAsync(includeDeleted: true)).Count);
        Assert.Contains(confirmation.TypedRequests, request =>
            request.Title == harness.ViewModel.L.Get("DeleteSelectedPermanentlyConfirmationTitle") &&
            request.RequiredPhrase == harness.ViewModel.L.Get("PermanentDeleteConfirmationPhrase"));

        confirmation.Result = true;
        await harness.ViewModel.DeleteSelectedDeletedPasswordsPermanentlyCommand.ExecuteAsync(null);

        Assert.Empty(harness.ViewModel.DeletedPasswords);
        Assert.Empty(await harness.Repository.GetPasswordsAsync(includeDeleted: true));
        Assert.Equal(
            harness.ViewModel.L.Format("DeletedSelectedPasswordsPermanentlyFormat", 2),
            harness.ViewModel.StatusMessage);
    }
}
