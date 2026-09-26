using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// The "new database" entry point, asked through the commands the buttons are wired to. Every claim
/// here is about the disk: a file that appears where the picker named it, one that does not appear
/// when the person backs out, and one that is never overwritten because it was already somebody's
/// vault. The master password is only ever compared, never printed.
/// </summary>
public sealed partial class AppSettingsTests
{
    private const string CreatedVaultPassword = "created-vault-fixture-not-a-secret";
    private const string WrongVaultPassword = "not-the-password";
    private const string DraftSecret = "create-draft-secret";

    [Fact]
    public async Task Creating_a_database_lands_where_the_picker_named_it_and_saves_there_afterwards()
    {
        var path = TestTempPaths.CreateFilePath(".kdbx");
        var picker = new KeePassCreateFilePicker(path);
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        viewModel.NewKeePassVaultCommand.Execute(null);
        Assert.True(viewModel.ShowKeePassCreateForm);
        viewModel.KeePassCreatePassword = CreatedVaultPassword;
        viewModel.KeePassCreateConfirmation = CreatedVaultPassword;
        await viewModel.CreateKeePassVaultCommand.ExecuteAsync(null);

        Assert.Equal(1, picker.TargetCalls);
        Assert.Equal("database.kdbx", picker.ReceivedSuggestedFileName);
        Assert.Equal("*.kdbx", Assert.Single(picker.ReceivedFileTypes).Patterns.Single());
        Assert.True(File.Exists(path));
        Assert.True(viewModel.HasKeePassImportPreview);
        Assert.False(viewModel.KeePassVaultIsDirty);
        Assert.False(viewModel.IsStatusMessageFailure);
        Assert.DoesNotContain(CreatedVaultPassword, viewModel.StatusMessage, StringComparison.Ordinal);
        // The tree opens on the root folder the Android client names, and on nothing else: a database
        // that comes up with rows nobody put there is a database that was not really empty.
        var rootRow = Assert.Single(viewModel.KeePassTreeRowsPublic);
        Assert.False(rootRow.IsEntryRow);
        Assert.Equal("Root", rootRow.Label);
        Assert.Empty(viewModel.KeePassCreatePassword);
        Assert.Empty(viewModel.KeePassCreateConfirmation);
        Assert.False(viewModel.ShowKeePassCreateForm);

        // The point of asking for the location up front: a brand new vault takes an entry and puts it
        // on disk without sending the person back through a save dialog.
        viewModel.NewKeePassEntryCommand.Execute(null);
        var editor = viewModel.KeePassEditorPublic;
        Assert.NotNull(editor);
        editor!.Title = "Written by Monica";
        editor.UserName = "monica@example.com";
        editor.Password = DraftSecret;
        await viewModel.ApplyKeePassEntryEditCommand.ExecuteAsync(null);
        Assert.True(viewModel.KeePassVaultIsDirty);

        await viewModel.SaveKeePassVaultCommand.ExecuteAsync(null);

        Assert.Equal(1, picker.TargetCalls);
        Assert.False(viewModel.KeePassVaultIsDirty);
        var saved = await File.ReadAllBytesAsync(path);
        using var reopened = await new KeePassVaultService()
            .OpenAsync(saved, Path.GetFileName(path), CreatedVaultPassword);
        var written = 0;
        await foreach (var detail in reopened.ReadDetailsAsync())
        {
            written++;
            Assert.Equal("Written by Monica", detail.Row.Title);
            Assert.Equal("monica@example.com", detail.Row.UserName);
            // Compared, not asserted: a failing Assert.Equal would print both sides into the report.
            Assert.True(detail.Password == DraftSecret);
        }

        Assert.Equal(1, written);

        await Assert.ThrowsAsync<KeePassVaultException>(() => new KeePassVaultService()
            .OpenAsync(saved, Path.GetFileName(path), WrongVaultPassword));
    }

    [Fact]
    public async Task Creating_refuses_to_overwrite_a_file_that_is_already_there()
    {
        var path = TestTempPaths.CreateFilePath(".kdbx");
        var alreadyOnDisk = KeePassTestVault.Create("someone-elses-vault");
        await File.WriteAllBytesAsync(path, alreadyOnDisk.Content);
        var picker = new KeePassCreateFilePicker(path);
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        viewModel.KeePassCreatePassword = CreatedVaultPassword;
        viewModel.KeePassCreateConfirmation = CreatedVaultPassword;
        await viewModel.CreateKeePassVaultCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsStatusMessageFailure);
        Assert.False(viewModel.HasKeePassImportPreview);
        Assert.False(viewModel.ShowKeePassCreateForm);
        Assert.Empty(viewModel.KeePassCreatePassword);
        Assert.Equal(1, picker.TargetCalls);
        // The whole point of the refusal: the bytes that unlock somebody's vault are still those bytes.
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(alreadyOnDisk.Content));
        using var untouched = await new KeePassVaultService()
            .OpenAsync(alreadyOnDisk.Content, Path.GetFileName(path), "someone-elses-vault");
        Assert.Equal(alreadyOnDisk.Entries.Count, untouched.EntryCount);
    }

    [Fact]
    public async Task Creating_a_database_says_so_when_the_location_is_not_a_local_file()
    {
        var picker = new KeePassCreateFilePicker(targetPath: null, namedTargetOnly: "icloud-drive.kdbx");
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        viewModel.KeePassCreatePassword = CreatedVaultPassword;
        viewModel.KeePassCreateConfirmation = CreatedVaultPassword;
        await viewModel.CreateKeePassVaultCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsStatusMessageFailure);
        Assert.False(viewModel.HasKeePassImportPreview);
        Assert.Equal(1, picker.TargetCalls);
        Assert.Empty(viewModel.KeePassCreatePassword);
    }

    [Fact]
    public async Task Backing_out_of_the_location_prompt_creates_nothing_and_is_not_a_failure()
    {
        var path = TestTempPaths.CreateFilePath(".kdbx");
        var picker = new KeePassCreateFilePicker(null);
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        viewModel.KeePassCreatePassword = CreatedVaultPassword;
        viewModel.KeePassCreateConfirmation = CreatedVaultPassword;
        await viewModel.CreateKeePassVaultCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsStatusMessageFailure);
        Assert.False(viewModel.HasKeePassImportPreview);
        Assert.False(File.Exists(path));
        Assert.Empty(viewModel.KeePassCreatePassword);
    }

    [Fact]
    public async Task The_create_form_waits_for_two_passwords_that_agree()
    {
        var path = TestTempPaths.CreateFilePath(".kdbx");
        var picker = new KeePassCreateFilePicker(path);
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        viewModel.NewKeePassVaultCommand.Execute(null);

        Assert.False(viewModel.CanCreateKeePassVault);
        Assert.False(viewModel.ShowsKeePassCreatePasswordMismatch);
        Assert.Empty(viewModel.KeePassCreatePasswordErrorText);

        viewModel.KeePassCreatePassword = CreatedVaultPassword;

        // The second line is still empty, so nothing has gone wrong yet.
        Assert.False(viewModel.ShowsKeePassCreatePasswordMismatch);
        Assert.False(viewModel.CanCreateKeePassVault);

        viewModel.KeePassCreateConfirmation = WrongVaultPassword;

        Assert.True(viewModel.ShowsKeePassCreatePasswordMismatch);
        Assert.False(viewModel.CanCreateKeePassVault);
        Assert.NotEmpty(viewModel.KeePassCreatePasswordErrorText);
        Assert.DoesNotContain(WrongVaultPassword, viewModel.KeePassCreatePasswordErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(CreatedVaultPassword, viewModel.KeePassCreatePasswordErrorText, StringComparison.Ordinal);

        await viewModel.CreateKeePassVaultCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsStatusMessageFailure);
        Assert.Equal(0, picker.TargetCalls);
        Assert.False(File.Exists(path));

        viewModel.KeePassCreateConfirmation = CreatedVaultPassword;

        Assert.True(viewModel.CanCreateKeePassVault);
        await viewModel.CreateKeePassVaultCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasKeePassImportPreview);
        Assert.Equal(1, picker.TargetCalls);
    }

    [Fact]
    public async Task A_master_password_of_only_spaces_never_reaches_the_disk()
    {
        var path = TestTempPaths.CreateFilePath(".kdbx");
        var picker = new KeePassCreateFilePicker(path);
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        viewModel.KeePassCreatePassword = "   ";
        viewModel.KeePassCreateConfirmation = "   ";

        Assert.False(viewModel.CanCreateKeePassVault);
        await viewModel.CreateKeePassVaultCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsStatusMessageFailure);
        Assert.Equal(0, picker.TargetCalls);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Leaving_the_import_workspace_wipes_a_typed_master_password()
    {
        var picker = new KeePassCreateFilePicker(TestTempPaths.CreateFilePath(".kdbx"));
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        viewModel.NewKeePassVaultCommand.Execute(null);
        viewModel.KeePassCreatePassword = CreatedVaultPassword;
        viewModel.KeePassCreateConfirmation = CreatedVaultPassword;

        viewModel.SelectedSyncPage = "Export";

        Assert.Empty(viewModel.KeePassCreatePassword);
        Assert.Empty(viewModel.KeePassCreateConfirmation);
        Assert.False(viewModel.ShowKeePassCreateForm);
        Assert.Equal(0, picker.TargetCalls);
    }

    [Fact]
    public async Task Canceling_the_create_form_leaves_an_open_database_unlocked()
    {
        var fixture = KeePassTestVault.Create(CreatedVaultPassword);
        var openPath = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(openPath, fixture.Content);
        var picker = new KeePassCreateFilePicker(TestTempPaths.CreateFilePath(".kdbx"))
        {
            FileToOpen = new PickedBinaryFile("ledger.kdbx", fixture.Content, openPath)
        };
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        await OpenKeePassVaultAsync(viewModel, CreatedVaultPassword);
        Assert.True(viewModel.HasKeePassImportPreview);
        var rowsBeforeCancel = viewModel.KeePassTreeRowsPublic;

        viewModel.NewKeePassVaultCommand.Execute(null);
        viewModel.KeePassCreatePassword = WrongVaultPassword;
        viewModel.CancelKeePassVaultCreateCommand.Execute(null);

        // Opening a form is not a decision about the file that is already unlocked; backing out of it
        // has to leave the browse tree exactly where it was rather than re-derive it.
        Assert.True(viewModel.HasKeePassImportPreview);
        Assert.Same(rowsBeforeCancel, viewModel.KeePassTreeRowsPublic);
        Assert.False(viewModel.ShowKeePassCreateForm);
        Assert.Empty(viewModel.KeePassCreatePassword);
    }

    private sealed class KeePassCreateFilePicker(
        string? targetPath,
        string? namedTargetOnly = null) : IFileSystemPickerService
    {
        public int TargetCalls { get; private set; }

        public string? ReceivedSuggestedFileName { get; private set; }

        public IReadOnlyList<PlatformFilePickerFileType> ReceivedFileTypes { get; private set; } = [];

        public PickedBinaryFile? FileToOpen { get; init; }

        public PlatformIntegrationCapability Capability { get; } =
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "Test file picker");

        public Task<PickedTextFile?> OpenTextFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<PickedTextFile?>(null);

        public Task<PickedBinaryFile?> OpenBinaryFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult(FileToOpen);

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> SaveBinaryFileAsync(string title, string suggestedFileName, ReadOnlyMemory<byte> content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<PickedSaveTarget?> PickSaveFileTargetAsync(string title, string suggestedFileName, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default)
        {
            TargetCalls++;
            ReceivedSuggestedFileName = suggestedFileName;
            ReceivedFileTypes = fileTypes;
            if (targetPath is { } path)
            {
                return Task.FromResult<PickedSaveTarget?>(new PickedSaveTarget(Path.GetFileName(path), path));
            }

            return Task.FromResult<PickedSaveTarget?>(
                namedTargetOnly is null ? null : new PickedSaveTarget(namedTargetOnly));
        }
    }
}
