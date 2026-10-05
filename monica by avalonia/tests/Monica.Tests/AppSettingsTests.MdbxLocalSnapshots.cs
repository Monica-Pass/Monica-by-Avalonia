using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task Mdbx_export_saves_a_snapshot_and_preserves_an_existing_backup()
    {
        var fixture = await CreateMdbxSnapshotCommandFixtureAsync(MdbxStorageLocation.Internal);
        var destination = TestTempPaths.CreateFilePath(".mdbx");
        fixture.Picker.SaveTarget = new PickedSaveTarget(Path.GetFileName(destination), destination);
        var original = await File.ReadAllBytesAsync(fixture.Database.WorkingCopyPath!);

        await fixture.ViewModel.ExportMdbxSnapshotCommand.ExecuteAsync(Assert.Single(fixture.ViewModel.MdbxDatabaseItems));

        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.False(fixture.ViewModel.IsStatusMessageFailure);
        Assert.Contains(Path.GetFileName(destination), fixture.ViewModel.StatusMessage, StringComparison.Ordinal);
        var newer = await CreateChangedMdbxFixtureAsync(original);
        await File.WriteAllBytesAsync(fixture.Database.WorkingCopyPath!, newer);

        await fixture.ViewModel.ExportMdbxSnapshotCommand.ExecuteAsync(Assert.Single(fixture.ViewModel.MdbxDatabaseItems));

        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Equal(newer, await File.ReadAllBytesAsync(fixture.Database.WorkingCopyPath!));
        Assert.True(fixture.ViewModel.IsStatusMessageFailure);
        Assert.Contains(fixture.ViewModel.L.Get("MdbxSnapshotDestinationExists"), fixture.ViewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Picker.EagerReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mdbx_snapshot_actions_require_a_local_path_from_the_picker(bool export)
    {
        var fixture = await CreateMdbxSnapshotCommandFixtureAsync(MdbxStorageLocation.Internal);
        var original = await File.ReadAllBytesAsync(fixture.Database.WorkingCopyPath!);
        fixture.Picker.SaveTarget = new PickedSaveTarget("cloud-only.mdbx");
        fixture.Picker.OpenTarget = new PickedOpenTarget("cloud-only.mdbx");

        if (export)
        {
            await fixture.ViewModel.ExportMdbxSnapshotCommand.ExecuteAsync(Assert.Single(fixture.ViewModel.MdbxDatabaseItems));
        }
        else
        {
            await fixture.ViewModel.RestoreMdbxSnapshotCommand.ExecuteAsync(Assert.Single(fixture.ViewModel.MdbxDatabaseItems));
        }

        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Database.WorkingCopyPath!));
        Assert.Equal(0, fixture.Vault.CreateSnapshotCalls);
        Assert.Equal(0, fixture.Vault.RestoreSnapshotCalls);
        Assert.Equal(0, fixture.Picker.EagerReads);
        Assert.Contains(fixture.ViewModel.L.Get("MdbxSnapshotLocalPathRequired"), fixture.ViewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mdbx_restore_canceling_picker_or_declining_confirmation_changes_nothing(bool cancelPicker)
    {
        var fixture = await CreateMdbxSnapshotCommandFixtureAsync(MdbxStorageLocation.Internal,
            approveRestore: cancelPicker);
        var original = await File.ReadAllBytesAsync(fixture.Database.WorkingCopyPath!);
        var incoming = TestTempPaths.CreateFilePath(".mdbx");
        await File.WriteAllBytesAsync(incoming, await CreateChangedMdbxFixtureAsync(original));
        fixture.Picker.OpenTarget = cancelPicker ? null : new PickedOpenTarget(Path.GetFileName(incoming), incoming);

        await fixture.ViewModel.RestoreMdbxSnapshotCommand.ExecuteAsync(Assert.Single(fixture.ViewModel.MdbxDatabaseItems));

        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Database.WorkingCopyPath!));
        Assert.True(File.Exists(incoming));
        Assert.Equal(0, fixture.Vault.RestoreSnapshotCalls);
        Assert.False(fixture.ViewModel.IsStatusMessageFailure);
        Assert.Contains(fixture.ViewModel.L.Get("MdbxOperationCanceled"), fixture.ViewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Picker.EagerReads);
    }

    [Theory]
    [InlineData(MdbxStorageLocation.Internal, SyncStatus.LocalOnly)]
    [InlineData(MdbxStorageLocation.External, SyncStatus.LocalOnly)]
    [InlineData(MdbxStorageLocation.RemoteWebDav, SyncStatus.PendingUpload)]
    [InlineData(MdbxStorageLocation.RemoteOneDrive, SyncStatus.PendingUpload)]
    public async Task Mdbx_local_restore_preserves_remote_revisions_and_sets_the_next_sync_direction(
        MdbxStorageLocation location, SyncStatus expectedStatus)
    {
        var fixture = await CreateMdbxSnapshotCommandFixtureAsync(location);
        var path = fixture.Database.WorkingCopyPath!;
        var original = await File.ReadAllBytesAsync(path);
        var incoming = TestTempPaths.CreateFilePath(".mdbx");
        var restored = await CreateChangedMdbxFixtureAsync(original);
        await File.WriteAllBytesAsync(incoming, restored);
        fixture.Picker.OpenTarget = new PickedOpenTarget(Path.GetFileName(incoming), incoming);
        var originalETag = fixture.Database.RemoteETag;
        var originalModified = fixture.Database.RemoteLastModifiedAt;
        var originalSynced = fixture.Database.LastSyncedAt;

        await fixture.ViewModel.RestoreMdbxSnapshotCommand.ExecuteAsync(Assert.Single(fixture.ViewModel.MdbxDatabaseItems));

        Assert.Equal(restored, await File.ReadAllBytesAsync(path));
        Assert.Equal(restored, await File.ReadAllBytesAsync(incoming));
        var database = Assert.Single(await fixture.Repository.GetMdbxDatabasesAsync());
        Assert.Equal(expectedStatus, database.LastSyncStatus);
        Assert.Equal(originalETag, database.RemoteETag);
        Assert.Equal(originalModified, database.RemoteLastModifiedAt);
        Assert.Equal(originalSynced, database.LastSyncedAt);
        var recovery = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}.local-conflict-*{Path.GetExtension(path)}"));
        Assert.Equal(original, await File.ReadAllBytesAsync(recovery));
        Assert.Contains(recovery, fixture.ViewModel.StatusMessage, StringComparison.Ordinal);
        Assert.True(fixture.ViewModel.HasMdbxSnapshotRecovery);
        Assert.Equal(recovery, fixture.ViewModel.MdbxSnapshotRecoveryPath);
        Assert.Equal(1, fixture.Vault.RestoreSnapshotCalls);
        Assert.Equal(0, fixture.Picker.EagerReads);
    }

    [Fact]
    public async Task Mdbx_local_restore_preserves_unsaved_notes_in_the_default_vault()
    {
        var fixture = await CreateMdbxSnapshotCommandFixtureAsync(MdbxStorageLocation.Internal, isDefault: true);
        var original = await File.ReadAllBytesAsync(fixture.Database.WorkingCopyPath!);
        var incoming = TestTempPaths.CreateFilePath(".mdbx");
        await File.WriteAllBytesAsync(incoming, await CreateChangedMdbxFixtureAsync(original));
        fixture.Picker.OpenTarget = new PickedOpenTarget(Path.GetFileName(incoming), incoming);
        var draft = new NoteEditorTab(-1, null, "Unsaved note")
        {
            IsDirty = true,
            DraftInitialized = true,
            DraftContent = "Preserve this note draft"
        };
        fixture.ViewModel.OpenNoteTabs.Add(draft);

        await fixture.ViewModel.RestoreMdbxSnapshotCommand.ExecuteAsync(Assert.Single(fixture.ViewModel.MdbxDatabaseItems));

        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Database.WorkingCopyPath!));
        Assert.Same(draft, Assert.Single(fixture.ViewModel.OpenNoteTabs));
        Assert.True(draft.IsDirty);
        Assert.Equal("Preserve this note draft", draft.DraftContent);
        Assert.Equal(0, fixture.Vault.RestoreSnapshotCalls);
        Assert.Contains(fixture.ViewModel.L.Get("MdbxSnapshotUnsavedEdits"), fixture.ViewModel.StatusMessage, StringComparison.Ordinal);
    }

    private static async Task<MdbxSnapshotCommandFixture> CreateMdbxSnapshotCommandFixtureAsync(
        MdbxStorageLocation location, bool approveRestore = true, bool isDefault = false)
    {
        var factory = new SqliteConnectionFactory(TestTempPaths.CreateFilePath(".db"));
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var vault = new MdbxSnapshotTestVaultService();
        var database = await vault.CreateLocalMetadataAsync("Snapshot fixture", TestTempPaths.CreateFilePath(".mdbx"));
        database.StorageLocation = location;
        database.IsDefault = isDefault;
        database.LastSyncStatus = SyncStatus.Synced;
        database.RemoteETag = "\"existing-revision\"";
        database.RemoteLastModifiedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        database.LastSyncedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_100);
        if (location is MdbxStorageLocation.RemoteWebDav or MdbxStorageLocation.RemoteOneDrive)
        {
            database.FilePath = "/Monica/snapshot-fixture.mdbx";
            database.SourceType = location == MdbxStorageLocation.RemoteWebDav ? "REMOTE_WEBDAV" : "REMOTE_ONEDRIVE";
            database.RemoteAccountId = "snapshot-test-account";
        }
        else
        {
            database.SourceType = location == MdbxStorageLocation.External ? "LOCAL_EXTERNAL" : "LOCAL_INTERNAL";
        }

        await repository.SaveMdbxDatabaseAsync(database);
        var picker = new MdbxSnapshotPathPicker();
        var viewModel = CreateViewModel(GetTempPath(), repository: repository, mdbxVaultService: vault,
            platformIntegrationService: FilePickerIntegration(), fileSystemPickerService: picker,
            confirmationDialogService: new MdbxSnapshotConfirmation(approveRestore));
        viewModel.IsUnlocked = true;
        await viewModel.RefreshMdbxVaultsCommand.ExecuteAsync(null);
        Assert.True(viewModel.CanUseMdbxSnapshotActions);
        return new MdbxSnapshotCommandFixture(viewModel, repository, vault, picker, database);
    }

    private sealed record MdbxSnapshotCommandFixture(MainWindowViewModel ViewModel, MonicaRepository Repository,
        MdbxSnapshotTestVaultService Vault, MdbxSnapshotPathPicker Picker, LocalMdbxDatabase Database);

    private sealed class MdbxSnapshotPathPicker : IFileSystemPickerService
    {
        public PickedSaveTarget? SaveTarget { get; set; }
        public PickedOpenTarget? OpenTarget { get; set; }
        public int EagerReads { get; private set; }
        public PlatformIntegrationCapability Capability => FilePickerIntegration().GetCapability(PlatformFeatureKeys.FilePicker);

        public Task<PickedSaveTarget?> PickSaveFileTargetAsync(string title, string suggestedFileName,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult(SaveTarget);

        public Task<PickedOpenTarget?> PickOpenFileTargetAsync(string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult(OpenTarget);

        public Task<PickedTextFile?> OpenTextFileAsync(string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default)
        {
            EagerReads++;
            throw new NotSupportedException("Snapshot selection must use the path-only picker.");
        }

        public Task<PickedBinaryFile?> OpenBinaryFileAsync(string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default)
        {
            EagerReads++;
            throw new NotSupportedException("Snapshot selection must use the path-only picker.");
        }

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string content,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> SaveBinaryFileAsync(string title, string suggestedFileName, ReadOnlyMemory<byte> content,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MdbxSnapshotConfirmation(bool approved) : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText,
            string? closeButtonText = null, CancellationToken cancellationToken = default) => Task.FromResult(approved);

        public Task<bool> ConfirmTypedAsync(string title, string message, string requiredPhrase, string instruction,
            string primaryButtonText, string? closeButtonText = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(approved);
    }
}
