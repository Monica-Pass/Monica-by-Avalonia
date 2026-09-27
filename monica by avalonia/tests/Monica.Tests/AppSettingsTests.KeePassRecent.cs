using System.Globalization;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// The list of .kdbx files this machine has opened, asked through the commands the rows are wired to.
/// Two claims hold this whole slice together and both are about what is NOT kept: a master password
/// never reaches the settings file, and a remembered row never unlocks anything by itself - it names
/// the file and hands the typing back to the person.
/// </summary>
public sealed partial class AppSettingsTests
{
    private const string RememberedPassword = "remembered-fixture-not-a-secret";
    private const string OtherRememberedPassword = "other-remembered-fixture-not-a-secret";

    [Fact]
    public async Task Unlocking_a_file_remembers_where_it_is_and_nothing_that_opens_it()
    {
        var settingsPath = GetTempPath();
        var settings = new Monica.App.Services.AppSettingsService(settingsPath);
        var path = TestTempPaths.CreateFilePath(".kdbx");
        var fixture = KeePassTestVault.Create(RememberedPassword);
        await File.WriteAllBytesAsync(path, fixture.Content);
        var picker = new KeePassRecentFilePicker();
        var viewModel = CreateViewModel(
            settingsPath,
            settingsService: settings,
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        picker.FileToOpen = await ReadAsPickedFileAsync(path);
        await OpenKeePassVaultAsync(viewModel, RememberedPassword);

        // The leak check comes before anything about the shape of a row, so that a change which puts a
        // secret in the row trips here first. Booleans, not a value pair: a failing equality would put
        // the file's contents into the report, and what this asserts is that no password is among them.
        await settings.SaveAsync();
        var saved = await File.ReadAllTextAsync(settingsPath);
        Assert.False(saved.Contains(RememberedPassword, StringComparison.Ordinal));
        Assert.False(saved.Contains(OtherRememberedPassword, StringComparison.Ordinal));

        var remembered = Assert.Single(settings.Current.KeePassRecentVaults);
        Assert.Equal(path, remembered.Path);
        Assert.Equal(Path.GetFileName(path), remembered.DisplayName);
        Assert.True(
            DateTimeOffset.TryParseExact(
                remembered.LastOpenedAtUtc,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _));

        var row = Assert.Single(viewModel.KeePassRecentVaultRows);
        Assert.Equal(path, row.Path);
        Assert.False(row.IsFileMissing);
        Assert.False(row.Label.Contains(RememberedPassword, StringComparison.Ordinal));
        Assert.False(viewModel.StatusMessage.Contains(RememberedPassword, StringComparison.Ordinal));

        var reloaded = new Monica.App.Services.AppSettingsService(settingsPath);
        await reloaded.LoadAsync();
        var survived = Assert.Single(reloaded.Current.KeePassRecentVaults);
        Assert.Equal(path, survived.Path);
        Assert.Equal(remembered.DisplayName, survived.DisplayName);
        Assert.Equal(remembered.LastOpenedAtUtc, survived.LastOpenedAtUtc);
    }

    [Fact]
    public async Task Opening_a_file_again_puts_it_back_at_the_top_and_stays_one_row()
    {
        var settingsPath = GetTempPath();
        var settings = new Monica.App.Services.AppSettingsService(settingsPath);
        var firstPath = await WriteRememberedFixtureAsync(RememberedPassword);
        var secondPath = await WriteRememberedFixtureAsync(OtherRememberedPassword);
        var picker = new KeePassRecentFilePicker();
        var viewModel = CreateViewModel(
            settingsPath,
            settingsService: settings,
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        picker.FileToOpen = await ReadAsPickedFileAsync(secondPath);
        await OpenKeePassVaultAsync(viewModel, OtherRememberedPassword);
        var secondArrived = settings.Current.KeePassRecentVaults[0].AddedAtUtc;

        picker.FileToOpen = await ReadAsPickedFileAsync(firstPath);
        await CloseKeePassVaultAsync(viewModel);
        await OpenKeePassVaultAsync(viewModel, RememberedPassword);

        picker.FileToOpen = await ReadAsPickedFileAsync(secondPath);
        await CloseKeePassVaultAsync(viewModel);
        await OpenKeePassVaultAsync(viewModel, OtherRememberedPassword);

        // Which of the two rows came first is the registry's own claim, tested there with fixed
        // stamps; what is asked here is that the wiring keeps one row per file and moves the opened
        // one to the front, and that the date it first appeared is not rewritten on the way.
        var kept = settings.Current.KeePassRecentVaults;
        Assert.Equal(2, kept.Count);
        Assert.Equal(secondPath, kept[0].Path);
        Assert.Equal(firstPath, kept[1].Path);
        Assert.Equal(secondArrived, kept[0].AddedAtUtc);

        var rows = viewModel.KeePassRecentVaultRows;
        Assert.Equal(2, rows.Count);
        Assert.Equal(secondPath, rows[0].Path);
        Assert.Equal(firstPath, rows[1].Path);
        Assert.False(rows[0].IsFileMissing);
        Assert.False(rows[1].IsFileMissing);
    }

    [Fact]
    public async Task Tapping_a_remembered_row_asks_for_the_password_it_never_had()
    {
        var settingsPath = GetTempPath();
        var settings = new Monica.App.Services.AppSettingsService(settingsPath);
        var path = await WriteRememberedFixtureAsync(RememberedPassword);
        var picker = new KeePassRecentFilePicker();
        var viewModel = CreateViewModel(
            settingsPath,
            settingsService: settings,
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        picker.FileToOpen = await ReadAsPickedFileAsync(path);
        await OpenKeePassVaultAsync(viewModel, RememberedPassword);
        await CloseKeePassVaultAsync(viewModel);
        Assert.False(viewModel.HasKeePassImportPreview);
        var opensBeforeTap = picker.OpenCalls;

        var row = Assert.Single(viewModel.KeePassRecentVaultRows);
        await viewModel.OpenKeePassRecentVaultCommand.ExecuteAsync(row);

        // The row names the file and stops there: the master password form is what appears, and it is
        // the same form the file picker path uses. Nothing on this machine unlocked anything.
        Assert.Equal(opensBeforeTap, picker.OpenCalls);
        Assert.True(viewModel.ShowKeePassOpenForm);
        Assert.False(viewModel.HasKeePassImportPreview);
        Assert.Equal(Path.GetFileName(path), viewModel.KeePassSelectedFileName);
        Assert.False(viewModel.IsStatusMessageFailure);
        Assert.Equal(0, viewModel.KeePassImportPassword.Length);

        viewModel.KeePassImportPassword = RememberedPassword;
        await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasKeePassImportPreview);
        Assert.False(viewModel.IsStatusMessageFailure);
        Assert.Equal(path, Assert.Single(settings.Current.KeePassRecentVaults).Path);
    }

    [Fact]
    public async Task Removing_a_row_leaves_the_file_and_the_unlocked_database_alone()
    {
        var settingsPath = GetTempPath();
        var settings = new Monica.App.Services.AppSettingsService(settingsPath);
        var path = await WriteRememberedFixtureAsync(RememberedPassword);
        var picker = new KeePassRecentFilePicker();
        var viewModel = CreateViewModel(
            settingsPath,
            settingsService: settings,
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        picker.FileToOpen = await ReadAsPickedFileAsync(path);
        await OpenKeePassVaultAsync(viewModel, RememberedPassword);
        var bytesBefore = await File.ReadAllBytesAsync(path);

        var row = Assert.Single(viewModel.KeePassRecentVaultRows);
        viewModel.ForgetKeePassRecentVaultCommand.Execute(row);

        Assert.Empty(settings.Current.KeePassRecentVaults);
        Assert.Empty(viewModel.KeePassRecentVaultRows);
        Assert.False(viewModel.HasKeePassRecentVaults);
        // "Remove from this list" means exactly that. The vault is still on disk, byte for byte, and
        // still unlocked in this session - a row is not a handle on the file.
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(bytesBefore));
        Assert.True(viewModel.HasKeePassImportPreview);
        Assert.False(viewModel.IsStatusMessageFailure);
        Assert.DoesNotContain(RememberedPassword, viewModel.StatusMessage, StringComparison.Ordinal);

        await settings.SaveAsync();
        var reloaded = new Monica.App.Services.AppSettingsService(settingsPath);
        await reloaded.LoadAsync();
        Assert.Empty(reloaded.Current.KeePassRecentVaults);
    }

    [Fact]
    public async Task A_remembered_file_that_moved_stays_on_the_list_and_says_so()
    {
        var settingsPath = GetTempPath();
        var settings = new Monica.App.Services.AppSettingsService(settingsPath);
        var path = await WriteRememberedFixtureAsync(RememberedPassword);
        var picker = new KeePassRecentFilePicker();
        var viewModel = CreateViewModel(
            settingsPath,
            settingsService: settings,
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        picker.FileToOpen = await ReadAsPickedFileAsync(path);
        await OpenKeePassVaultAsync(viewModel, RememberedPassword);
        await CloseKeePassVaultAsync(viewModel);
        File.Delete(path);

        // Leaving and returning to the page is what asks the disk again: the rows were last rebuilt
        // while the file was still in place.
        viewModel.SelectedSyncPage = "Export";
        viewModel.SelectedSyncPage = "Import";
        Assert.True(viewModel.HasKeePassRecentVaults);
        var row = Assert.Single(viewModel.KeePassRecentVaultRows);
        Assert.True(row.IsFileMissing);
        Assert.Equal(path, row.Path);
        // Where it used to be is the one piece of the path worth showing, and only now that it is gone.
        Assert.Equal(Path.GetDirectoryName(path), row.DirectoryText);
        Assert.False(row.ShowsLastOpened);

        await viewModel.OpenKeePassRecentVaultCommand.ExecuteAsync(row);

        Assert.True(viewModel.IsStatusMessageFailure);
        Assert.False(viewModel.HasKeePassImportPreview);
        Assert.False(viewModel.ShowKeePassOpenForm);
        // A row that cannot be opened is not a row to erase: the user may well move the file back.
        Assert.Equal(path, Assert.Single(viewModel.KeePassRecentVaultRows).Path);
        Assert.Single(settings.Current.KeePassRecentVaults);
    }

    [Fact]
    public async Task A_database_that_never_unlocked_is_never_remembered()
    {
        var settingsPath = GetTempPath();
        var settings = new Monica.App.Services.AppSettingsService(settingsPath);
        var path = await WriteRememberedFixtureAsync(RememberedPassword);
        var picker = new KeePassRecentFilePicker();
        var viewModel = CreateViewModel(
            settingsPath,
            settingsService: settings,
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        picker.FileToOpen = await ReadAsPickedFileAsync(path);
        await viewModel.SelectKeePassFileCommand.ExecuteAsync(null);
        viewModel.KeePassImportPassword = WrongVaultPassword;
        await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasKeePassImportPreview);
        Assert.True(viewModel.IsStatusMessageFailure);
        Assert.Empty(settings.Current.KeePassRecentVaults);
    }

    [Fact]
    public async Task A_file_with_no_local_path_has_no_row_to_remember()
    {
        var settingsPath = GetTempPath();
        var settings = new Monica.App.Services.AppSettingsService(settingsPath);
        var fixture = KeePassTestVault.Create(RememberedPassword);
        var picker = new KeePassRecentFilePicker();
        var viewModel = CreateViewModel(
            settingsPath,
            settingsService: settings,
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        // Bytes with nowhere to save back to - what a picker over a cloud provider hands over.
        picker.FileToOpen = new PickedBinaryFile("cloud-vault.kdbx", fixture.Content, null);
        await viewModel.SelectKeePassFileCommand.ExecuteAsync(null);
        viewModel.KeePassImportPassword = RememberedPassword;
        await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasKeePassImportPreview);
        Assert.Empty(settings.Current.KeePassRecentVaults);
        Assert.Empty(viewModel.KeePassRecentVaultRows);
    }

    [Fact]
    public async Task Creating_a_database_remembers_the_file_it_wrote()
    {
        var settingsPath = GetTempPath();
        var settings = new Monica.App.Services.AppSettingsService(settingsPath);
        var path = TestTempPaths.CreateFilePath(".kdbx");
        var picker = new KeePassRecentFilePicker { CreateTarget = path };
        var viewModel = CreateViewModel(
            settingsPath,
            settingsService: settings,
            fileSystemPickerService: picker,
            keePassVaultService: new KeePassVaultService());

        viewModel.NewKeePassVaultCommand.Execute(null);
        viewModel.KeePassCreatePassword = CreatedVaultPassword;
        viewModel.KeePassCreateConfirmation = CreatedVaultPassword;
        await viewModel.CreateKeePassVaultCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasKeePassImportPreview);
        var remembered = Assert.Single(settings.Current.KeePassRecentVaults);
        Assert.Equal(path, remembered.Path);
        Assert.Equal(Path.GetFileName(path), remembered.DisplayName);
        var row = Assert.Single(viewModel.KeePassRecentVaultRows);
        Assert.False(row.IsFileMissing);
    }

    private static async Task<string> WriteRememberedFixtureAsync(string password)
    {
        var path = TestTempPaths.CreateFilePath(".kdbx");
        await File.WriteAllBytesAsync(path, KeePassTestVault.Create(password).Content);
        return path;
    }

    private static async Task<PickedBinaryFile> ReadAsPickedFileAsync(string path) =>
        new(Path.GetFileName(path), await File.ReadAllBytesAsync(path), path);

    private static async Task CloseKeePassVaultAsync(MainWindowViewModel viewModel)
    {
        await viewModel.ResetKeePassImportCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasKeePassImportPreview);
    }

    /// <summary>
    /// The picker this slice needs is one whose file can change between steps, because opening a
    /// second database means a second file. What the dialog would have handed over is returned
    /// unchanged; nothing here is recorded, since the point of the file is where it says it came from.
    /// </summary>
    private sealed class KeePassRecentFilePicker : IFileSystemPickerService
    {
        public string? CreateTarget { get; set; }

        public PickedBinaryFile? FileToOpen { get; set; }

        public int OpenCalls { get; private set; }

        public PlatformIntegrationCapability Capability { get; } =
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "Test file picker");

        public Task<PickedTextFile?> OpenTextFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<PickedTextFile?>(null);

        public Task<PickedBinaryFile?> OpenBinaryFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default)
        {
            OpenCalls++;
            return Task.FromResult(FileToOpen);
        }

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> SaveBinaryFileAsync(string title, string suggestedFileName, ReadOnlyMemory<byte> content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<PickedSaveTarget?> PickSaveFileTargetAsync(string title, string suggestedFileName, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default)
        {
            if (CreateTarget is not { } target)
            {
                return Task.FromResult<PickedSaveTarget?>(null);
            }

            return Task.FromResult<PickedSaveTarget?>(
                new PickedSaveTarget(System.IO.Path.GetFileName(target), target));
        }
    }
}
