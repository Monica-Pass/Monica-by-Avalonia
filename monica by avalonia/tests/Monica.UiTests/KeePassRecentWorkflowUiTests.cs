using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.Sync;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.UiTests;

/// <summary>
/// The list of databases this machine has opened, asked where a person sees it: the KeePass tab of a shown
/// window. The commands behind the rows are covered against the disk elsewhere, so everything here is about
/// the screen - that a remembered file comes up as one row named by its file rather than by a path, that the
/// row hands the master password back to the person instead of holding it, that a file which moved says so
/// and shows where it used to be, and that the small dismiss control takes the row off the list without
/// touching the vault. The master password is only ever typed into a masked box and compared, never printed.
/// </summary>
[Collection(AvaloniaUiTestCollection.Name)]
public sealed class KeePassRecentWorkflowUiTests
{
    private const string FixturePassword = KeePassSmokeVaultWriter.DefaultPassword;

    public KeePassRecentWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    /// <summary>
    /// Opens a database through the painted controls, then asks the screen what it kept: a row with the file's
    /// name and the date it was opened, no folder path, no secret. Closing and tapping that row has to bring
    /// back the same masked master-password form the file picker path brings up, and it has to do it without a
    /// dialog - which is the whole promise that a remembered row is a shortcut to a prompt, not a way around
    /// one.
    /// </summary>
    [Fact]
    public async Task KeePass_recent_row_is_painted_by_name_and_hands_the_master_password_back()
    {
        var fixturePath = NewFixturePath("recent-tap");
        var fileName = Path.GetFileName(fixturePath);
        var openedHost = await HostAsync(fixturePath);
        try
        {
            var view = openedHost.View;
            var viewModel = openedHost.ViewModel;
            Assert.Empty(viewModel.KeePassRecentVaultRows);
            AssertRecentSectionHidden(view);

            await UnlockThroughThePaneAsync(view, viewModel);

            var section = view.InPane<StackPanel>("KeePassRecentSection");
            Assert.True(section.IsVisible, "an opened database did not come back as a remembered row");
            Assert.True(section.Bounds.Width > 0 && section.Bounds.Height > 0);
            var openButton = view.InPane<Button>("OpenKeePassRecentVaultButton");
            var row = Assert.Single(viewModel.KeePassRecentVaultRows);
            // A row wired to a neighbour command, or to nobody's row, would pass every visual check below.
            Assert.Same(viewModel.OpenKeePassRecentVaultCommand, openButton.Command);
            // Reference equality reported as a boolean: a row printed by a failing Assert.Same would print
            // whatever its fields hold, and those fields are where a secret would land.
            Assert.True(ReferenceEquals(row, openButton.CommandParameter), "the open control was handed another row");
            Assert.False(row.IsFileMissing);

            AssertPainting(section, fileName, fixturePath, expectFolder: false);
            Assert.False(row.LastOpenedText.Length == 0);

            await viewModel.ResetKeePassImportCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(viewModel.HasKeePassImportPreview);
            var dialogsBeforeTap = openedHost.Picker.OpenCalls;

            await viewModel.OpenKeePassRecentVaultCommand.ExecuteAsync(row);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(dialogsBeforeTap, openedHost.Picker.OpenCalls);
            var passwordBox = view.InPane<TextBox>("KeePassImportPasswordBox");
            Assert.True(
                passwordBox.IsEffectivelyVisible,
                "the remembered row did not bring back the master password form");
            Assert.Equal('*', passwordBox.PasswordChar);
            // Emptiness without a value pair, so that a red run cannot print what the box would have held.
            Assert.True(passwordBox.Text is null || passwordBox.Text.Length == 0);
            Assert.False(viewModel.HasKeePassImportPreview);
            Assert.False(viewModel.IsStatusMessageFailure);
            Assert.Equal(fileName, viewModel.KeePassSelectedFileName);
            Assert.True(section.IsEffectivelyVisible, "the remembered list vanished once a file was picked");

            passwordBox.Text = FixturePassword;
            Dispatcher.UIThread.RunJobs();
            await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(view.InPane<Border>("KeePassPreviewCard").IsVisible);
            Assert.True(viewModel.HasKeePassImportPreview);
            Assert.False(viewModel.IsStatusMessageFailure);
            Assert.DoesNotContain(FixturePassword, viewModel.StatusMessage, StringComparison.Ordinal);

            // The list is rebuilt whenever a database unlocks, so the row to dismiss is read off the screen
            // again rather than from the control handed over before the second unlock.
            var dismissButton = view.InPane<Button>("ForgetKeePassRecentVaultButton");
            var liveRow = Assert.Single(viewModel.KeePassRecentVaultRows);
            Assert.Same(viewModel.ForgetKeePassRecentVaultCommand, dismissButton.Command);
            Assert.True(
                ReferenceEquals(liveRow, dismissButton.CommandParameter),
                "the dismiss control was handed another row");
            dismissButton.Command!.Execute(dismissButton.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            // "Remove from this list" leaves the unlocked database exactly as it was.
            Assert.True(
                viewModel.KeePassRecentVaultRows.Count == 0,
                $"the dismiss control left its row on screen (settings rows={openedHost.Settings.Current.KeePassRecentVaults.Count})");
            AssertRecentSectionHidden(view);
            Assert.True(viewModel.HasKeePassImportPreview);
            Assert.True(File.Exists(fixturePath));
        }
        finally
        {
            openedHost.Dispose();
            TryDelete(fixturePath);
        }
    }

    /// <summary>
    /// A vault that is no longer where it was opened is the one case where the row earns its full location
    /// line, and it stays on the list: a database on a drive that is not mounted right now is still the
    /// person's database. Tapping it says it could not be read rather than pretending otherwise, and the row
    /// survives that failure.
    /// </summary>
    [Fact]
    public async Task KeePass_recent_row_says_where_a_moved_file_used_to_be()
    {
        var fixturePath = NewFixturePath("recent-moved");
        var fileName = Path.GetFileName(fixturePath);
        var openedHost = await HostAsync(fixturePath);
        try
        {
            var view = openedHost.View;
            var viewModel = openedHost.ViewModel;
            await UnlockThroughThePaneAsync(view, viewModel);
            await viewModel.ResetKeePassImportCommand.ExecuteAsync(null);
            File.Delete(fixturePath);

            // Leaving and returning to the page is what asks the disk again.
            viewModel.SelectedSyncPage = "Export";
            Dispatcher.UIThread.RunJobs();
            viewModel.SelectedSyncPage = "Import";
            Dispatcher.UIThread.RunJobs();
            ShowKeePassTab(view);

            var row = Assert.Single(viewModel.KeePassRecentVaultRows);
            Assert.True(row.IsFileMissing);
            var section = view.InPane<StackPanel>("KeePassRecentSection");
            Assert.True(section.IsVisible);
            var missingText = view.InPane<TextBlock>("KeePassRecentFileMissingText");
            Assert.True(missingText.IsEffectivelyVisible, "a remembered file that is gone did not say so on screen");
            Assert.False(string.IsNullOrEmpty(missingText.Text));

            AssertPainting(section, fileName, fixturePath, expectFolder: true);

            await viewModel.OpenKeePassRecentVaultCommand.ExecuteAsync(row);
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.IsStatusMessageFailure);
            Assert.False(viewModel.HasKeePassImportPreview);
            Assert.False(
                view.TryInPane<TextBox>("KeePassImportPasswordBox")?.IsEffectivelyVisible ?? false,
                "a row that could not be read still offered the master password form");
            // A row that cannot be opened is not a row to erase - the file may well come back.
            Assert.Equal(fixturePath, Assert.Single(viewModel.KeePassRecentVaultRows).Path);
        }
        finally
        {
            openedHost.Dispose();
            TryDelete(fixturePath);
        }
    }

    /// <summary>
    /// Selects the file with the tab's own button, types the master password into the masked box the tab
    /// paints, and unlocks through its inspect button - so the row under test was recorded by the same path a
    /// person takes.
    /// </summary>
    private static async Task UnlockThroughThePaneAsync(
        SyncImportView view,
        MainWindowViewModel viewModel)
    {
        var selectButton = view.InPane<Button>("SelectKeePassFileButton");
        Assert.Same(viewModel.SelectKeePassFileCommand, selectButton.Command);
        await viewModel.SelectKeePassFileCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var passwordBox = view.InPane<TextBox>("KeePassImportPasswordBox");
        Assert.Equal('*', passwordBox.PasswordChar);
        passwordBox.Text = FixturePassword;
        Dispatcher.UIThread.RunJobs();

        var inspectButton = view.InPane<Button>("PreviewKeePassImportButton");
        Assert.Same(viewModel.PreviewKeePassImportCommand, inspectButton.Command);
        await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.HasKeePassImportPreview, "the fixture vault did not unlock");
    }

    /// <summary>
    /// What a remembered row is allowed to put on the screen, said with booleans and fixed sentences. A
    /// failing Assert over a collection of painted strings prints that collection, and one of those strings is
    /// exactly where a master password would turn up if a row ever held one - so nothing here can echo a value.
    /// The folder belongs on a row only when the file has stopped living in it.
    /// </summary>
    private static void AssertPainting(
        StackPanel section,
        string fileName,
        string fixturePath,
        bool expectFolder)
    {
        var painted = PaintedText(section);
        Assert.True(
            painted.Any(text => text == fileName),
            "the row did not paint the name of the file it remembers");
        Assert.False(
            painted.Any(text => text.Contains(FixturePassword, StringComparison.Ordinal)),
            "a remembered row painted something that opens a vault");
        Assert.Equal(
            expectFolder,
            painted.Any(text => text.Contains(
                Path.GetDirectoryName(fixturePath)!, StringComparison.Ordinal)));
    }

    private static void AssertRecentSectionHidden(SyncImportView view)
    {
        var section = view.TryInPane<StackPanel>("KeePassRecentSection");
        Assert.True(
            section is null || !section.IsEffectivelyVisible,
            "the remembered list showed rows nobody opened");
    }

    /// <summary>
    /// The strings a subtree really renders, with the hidden ones left out: a control under a collapsed panel
    /// keeps its own text and its own IsVisible, so only what the window would paint counts here.
    /// </summary>
    private static List<string> PaintedText(Control subtree)
    {
        var painted = subtree.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible)
            .Select(text => text.Text ?? "")
            .ToList();
        Assert.NotEmpty(painted);
        return painted;
    }

    private static void ShowKeePassTab(SyncImportView view)
    {
        var tabs = view.FindControl<TabControl>("ImportSourceTabs")!;
        tabs.SelectedItem = view.FindControl<TabItem>("KeePassImportTab")!;
        Dispatcher.UIThread.RunJobs();
    }

    private static string NewFixturePath(string label)
    {
        var directory = Path.Combine(Path.GetTempPath(), "monica-uitests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"keepass-{label}-{Guid.NewGuid():N}.kdbx");
    }

    /// <summary>
    /// Puts the KeePass tab on screen and starts from an empty remembered list. Every test in this collection
    /// shares one process and one settings store, so the KeePass panes that ran before left their files on
    /// the list; those rows go before the screen is read, because a list that starts with somebody else's
    /// vault cannot be counted.
    /// </summary>
    private static async Task<OpenedHost> HostAsync(string fixturePath)
    {
        // Deriving the key costs a second of Argon2 and this runs on the UI thread, so the fixture is built
        // off it rather than blocking the thread the assertions pump.
        await Task.Run(() => KeePassSmokeVaultWriter.Write(fixturePath, FixturePassword, entries: 2, groups: 1));
        var content = await File.ReadAllBytesAsync(fixturePath, TestContext.Current.CancellationToken);
        var picker = new RecentFilePicker(new PickedBinaryFile(
            Path.GetFileName(fixturePath),
            content,
            fixturePath));

        var window = new Monica.App.MainWindow();
        var services = Monica.App.App.ConfigureServices(window, collection =>
        {
            collection.AddSingleton<IFileSystemPickerService>(picker);
        });
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var settings = services.GetRequiredService<IAppSettingsService>();

        var view = new SyncImportView { DataContext = viewModel };
        var host = new Window { Width = 1280, Height = 800, Content = view };
        viewModel.SelectedSyncPage = "Import";
        host.Show();
        Dispatcher.UIThread.RunJobs();
        ShowKeePassTab(view);
        ClearRememberedRows(viewModel, settings);
        return new OpenedHost(view, viewModel, host, services, picker, settings);
    }

    /// <summary>
    /// Starts the pane from an empty remembered list. The list is emptied through the settings store rather
    /// than the dismiss control, because this is the harness getting to a known state - the control's own
    /// promise is what a test body goes on to check.
    /// </summary>
    private static void ClearRememberedRows(MainWindowViewModel viewModel, IAppSettingsService settings)
    {
        settings.Current.KeePassRecentVaults.Clear();
        viewModel.SelectedSyncPage = "Export";
        Dispatcher.UIThread.RunJobs();
        viewModel.SelectedSyncPage = "Import";
        Dispatcher.UIThread.RunJobs();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed record OpenedHost(
        SyncImportView View,
        MainWindowViewModel ViewModel,
        Window Host,
        ServiceProvider Services,
        RecentFilePicker Picker,
        IAppSettingsService Settings) : IDisposable
    {
        public void Dispose()
        {
            ClearRememberedRows(ViewModel, Settings);
            Host.Close();
            Dispatcher.UIThread.RunJobs();
            Services.Dispose();
        }
    }

    /// <summary>
    /// Hands the view model one file and counts the times it was asked, because "the remembered row did not
    /// send the person back through a file picker" is only visible as a number.
    /// </summary>
    private sealed class RecentFilePicker(PickedBinaryFile file) : IFileSystemPickerService
    {
        public int OpenCalls { get; private set; }

        public PlatformIntegrationCapability Capability { get; } = PlatformIntegrationService.Available(
            PlatformFeatureKeys.FilePicker,
            "Test file picker");

        public Task<PickedBinaryFile?> OpenBinaryFileAsync(
            string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default)
        {
            OpenCalls++;
            return Task.FromResult<PickedBinaryFile?>(file);
        }

        public Task<PickedTextFile?> OpenTextFileAsync(
            string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => Task.FromResult<PickedTextFile?>(null);

        public Task<string?> SaveTextFileAsync(
            string title,
            string suggestedFileName,
            string content,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> SaveBinaryFileAsync(
            string title,
            string suggestedFileName,
            ReadOnlyMemory<byte> content,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<PickedSaveTarget?> PickSaveFileTargetAsync(
            string title,
            string suggestedFileName,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => Task.FromResult<PickedSaveTarget?>(null);
    }
}
