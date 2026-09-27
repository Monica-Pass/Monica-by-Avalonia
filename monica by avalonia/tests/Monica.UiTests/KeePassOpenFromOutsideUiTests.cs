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
/// A database that arrives from outside the window - double-clicked in Explorer, or handed over by a second
/// launch - asked where a person sees it. The rules this pane has to honour are that the file comes in
/// through the same door a click does (its name up, the master password asked for, nothing decrypted), that
/// the window is walked to the page that opens a file even when the request landed on another page or at the
/// lock screen, and that the tab selection is a two-way conversation so a request that comes in twice works
/// twice. The master password is only ever typed into a masked box and compared, never printed.
/// </summary>
[Collection(AvaloniaUiTestCollection.Name)]
public sealed class KeePassOpenFromOutsideUiTests
{
    private const string FixturePassword = KeePassSmokeVaultWriter.DefaultPassword;

    public KeePassOpenFromOutsideUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    /// <summary>
    /// A file handed over while the window is still at its lock screen has nowhere to land: the page that
    /// opens a .kdbx sits behind that door. So the request waits, and the unlock is what spends it. Nothing
    /// about the file may be read into the interface before then, and the unlock has to bring the whole
    /// arrival - page, tab, name, masked prompt - rather than a fraction of it.
    /// </summary>
    [Fact]
    public async Task KeePass_file_handed_to_a_locked_window_arrives_when_the_vault_unlocks()
    {
        var fixturePath = NewFixturePath("handed-while-locked");
        var fileName = Path.GetFileName(fixturePath);
        var openedHost = await HostAsync(fixturePath);
        try
        {
            var view = openedHost.View;
            var viewModel = openedHost.ViewModel;
            var keepassTab = view.FindControl<TabItem>("KeePassImportTab")!;
            // Start somewhere else on purpose: a request that only worked because the page already happened
            // to be up would prove nothing about a window that was on another page when the file arrived.
            viewModel.SelectedSyncPage = "Backup";
            Dispatcher.UIThread.RunJobs();
            Assert.False(viewModel.IsUnlocked, "a window meant to be at its lock screen started unlocked");
            var dialogs = openedHost.Picker.OpenCalls;

            viewModel.RequestKeePassFileOpen(fixturePath);
            Dispatcher.UIThread.RunJobs();

            Assert.False(viewModel.HasKeePassSelectedFile, "a locked window let a file reach its interface");
            Assert.Equal("Backup", viewModel.SelectedSyncPage);
            Assert.False(keepassTab.IsSelected);
            Assert.Equal(dialogs, openedHost.Picker.OpenCalls);

            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Import", viewModel.SelectedSyncPage);
            Assert.True(keepassTab.IsSelected, "the file arrived but not on the tab that opens one");
            Assert.True(viewModel.KeePassImportTabSelected);
            var passwordBox = view.InPane<TextBox>("KeePassImportPasswordBox");
            Assert.True(
                passwordBox.IsEffectivelyVisible,
                "the file waited at the lock screen and never came up once the vault unlocked");
            Assert.Equal(fileName, viewModel.KeePassSelectedFileName);
            Assert.Equal('*', passwordBox.PasswordChar);
            Assert.True(passwordBox.Text is null || passwordBox.Text.Length == 0);
            Assert.True(string.IsNullOrEmpty(viewModel.KeePassImportPassword));
            // Arriving by command line is not arriving with a key.
            Assert.False(
                viewModel.HasKeePassImportPreview,
                "a handed-over database opened without anyone typing the master password");
            Assert.False(viewModel.IsStatusMessageFailure);
            Assert.False(
                viewModel.StatusMessage.Contains(FixturePassword, StringComparison.Ordinal),
                "naming the file echoed something that opens it");
            Assert.Equal(dialogs, openedHost.Picker.OpenCalls);
            AssertPainted(view, fileName);
        }
        finally
        {
            openedHost.Dispose();
            TryDelete(fixturePath);
        }
    }

    /// <summary>
    /// The tab flag is read by the view and written back by it, and this is where both directions are asked.
    /// A one-way flag would put the request on the KeePass tab and then leave the flag lying about which tab
    /// a person is looking at, so the second file of the day would arrive on a tab nobody had selected.
    /// </summary>
    [Fact]
    public async Task KeePass_file_handed_twice_arrives_twice_and_the_tab_answers_for_itself()
    {
        var firstPath = NewFixturePath("handed-first");
        var secondPath = NewFixturePath("handed-second");
        var openedHost = await HostAsync(firstPath, secondPath);
        try
        {
            var view = openedHost.View;
            var viewModel = openedHost.ViewModel;
            var tabs = view.FindControl<TabControl>("ImportSourceTabs")!;
            var keepassTab = view.FindControl<TabItem>("KeePassImportTab")!;
            var bitwardenTab = (TabItem)tabs.Items[0]!;
            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(keepassTab.IsSelected, "the page did not start on the tab it always starts on");

            viewModel.RequestKeePassFileOpen(firstPath);
            Dispatcher.UIThread.RunJobs();

            Assert.True(keepassTab.IsSelected);
            Assert.Equal(Path.GetFileName(firstPath), viewModel.KeePassSelectedFileName);

            // Going back by hand has to be heard, or the flag would only ever say "KeePass".
            tabs.SelectedItem = bitwardenTab;
            Dispatcher.UIThread.RunJobs();
            Assert.False(
                viewModel.KeePassImportTabSelected,
                "the tab was changed on screen and the page did not notice");
            Assert.True(bitwardenTab.IsSelected);

            viewModel.RequestKeePassFileOpen(secondPath);
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                keepassTab.IsSelected,
                "a second file was accepted by the window and never shown to the person");
            Assert.Equal(Path.GetFileName(secondPath), viewModel.KeePassSelectedFileName);
            var passwordBox = view.InPane<TextBox>("KeePassImportPasswordBox");
            Assert.True(passwordBox.IsEffectivelyVisible);
            Assert.False(viewModel.HasKeePassImportPreview);
            Assert.False(viewModel.IsStatusMessageFailure);
            AssertPainted(view, Path.GetFileName(secondPath));
        }
        finally
        {
            openedHost.Dispose();
            TryDelete(firstPath);
            TryDelete(secondPath);
        }
    }

    /// <summary>
    /// What the door does with nothing, and with a file that is no longer there. A blank request must leave
    /// the window exactly as it was - it is the shape of every launch that is not a double-click, and if it
    /// moved anyone the app would jump to an empty tab on starting. A path whose file went between being
    /// handed over and being read is the case the drop zone is built for: a request can wait there while the
    /// window is closed, and the drive it pointed at may be gone by the time the window wakes up. That says
    /// it could not be read, names nothing, offers no master-password prompt over an empty file, and leaves
    /// the pane idle rather than stuck in the middle of an operation that already ended.
    /// </summary>
    [Fact]
    public async Task KeePass_blank_request_moves_nothing_and_a_file_that_went_says_so()
    {
        var gonePath = NewFixturePath("handed-then-gone");
        var openedHost = await HostAsync();
        try
        {
            var view = openedHost.View;
            var viewModel = openedHost.ViewModel;
            var keepassTab = view.FindControl<TabItem>("KeePassImportTab")!;
            viewModel.IsUnlocked = true;
            viewModel.SelectedSyncPage = "Export";
            Dispatcher.UIThread.RunJobs();
            var dialogs = openedHost.Picker.OpenCalls;

            viewModel.RequestKeePassFileOpen(null);
            viewModel.RequestKeePassFileOpen("");
            viewModel.RequestKeePassFileOpen("   ");
            Dispatcher.UIThread.RunJobs();

            Assert.False(keepassTab.IsSelected, "a launch with no file moved the window anyway");
            Assert.Equal("Export", viewModel.SelectedSyncPage);
            Assert.False(viewModel.HasKeePassSelectedFile);
            Assert.Equal(dialogs, openedHost.Picker.OpenCalls);

            // Opened once, then taken away - which is what a request on an unmounted drive looks like.
            await using (File.Create(gonePath))
            {
            }

            File.Delete(gonePath);
            viewModel.RequestKeePassFileOpen(gonePath);
            Dispatcher.UIThread.RunJobs();

            // The page is reached - the person did ask for a file here - but the form is refused.
            Assert.Equal("Import", viewModel.SelectedSyncPage);
            Assert.True(keepassTab.IsSelected);
            Assert.True(
                viewModel.IsStatusMessageFailure,
                "a file that could not be read was swallowed without a word");
            Assert.False(viewModel.HasKeePassSelectedFile);
            Assert.False(viewModel.HasKeePassImportPreview);
            Assert.True(viewModel.IsKeePassImportIdle, "a failed read left the pane busy forever");
            Assert.False(
                view.TryInPane<TextBox>("KeePassImportPasswordBox")?.IsEffectivelyVisible ?? false,
                "a file that could not be read still offered the master password form");
            Assert.Equal(dialogs, openedHost.Picker.OpenCalls);

            // The failed request is spent, not re-armed. Put the file back and go through the lock screen a
            // second time: a request that had stayed standing would name it now, and nobody asked for it
            // again.
            File.WriteAllBytes(gonePath, [1, 2, 3]);
            viewModel.IsUnlocked = false;
            Dispatcher.UIThread.RunJobs();
            viewModel.IsUnlocked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(viewModel.HasKeePassSelectedFile, "a request that already failed came back to life");
            Assert.Equal(dialogs, openedHost.Picker.OpenCalls);
        }
        finally
        {
            openedHost.Dispose();
            TryDelete(gonePath);
        }
    }

    /// <summary>
    /// The name has to be on the screen, and nothing that opens the file may be. Said with booleans and
    /// fixed sentences, because a failing Assert over a collection of painted strings prints that collection
    /// and one of those strings is exactly where a master password would turn up.
    /// </summary>
    private static void AssertPainted(SyncImportView view, string fileName)
    {
        var painted = view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible)
            .Select(text => text.Text ?? "")
            .ToList();
        Assert.True(
            painted.Any(text => text == fileName),
            "the file arrived in the window but was never named on screen");
        Assert.False(
            painted.Any(text => text.Contains(FixturePassword, StringComparison.Ordinal)),
            "the page painted something that opens a vault");
    }

    private static string NewFixturePath(string label)
    {
        var directory = Path.Combine(Path.GetTempPath(), "monica-uitests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"keepass-{label}-{Guid.NewGuid():N}.kdbx");
    }

    /// <summary>
    /// A window at its lock screen, with the import page already built and the plain tab up. Every fixture
    /// file is a real database so that "it was staged and not opened" is a claim about bytes that would have
    /// unlocked, and the picker is wired to fail loudly: no path in this file may send the person through it.
    /// </summary>
    private static async Task<OpenedHost> HostAsync(params string[] fixturePaths)
    {
        // Deriving a key costs a second of Argon2 and this runs on the UI thread, so the fixtures are built
        // off it rather than blocking the thread the assertions pump.
        await Task.Run(() =>
        {
            foreach (var path in fixturePaths)
            {
                KeePassSmokeVaultWriter.Write(path, FixturePassword, entries: 2, groups: 1);
            }
        });
        var picker = new CountingFilePicker();

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
        // The list this machine remembers belongs to whoever ran last in this process, and a row nobody
        // opened would make every count below wrong.
        settings.Current.KeePassRecentVaults.Clear();
        viewModel.SelectedSyncPage = "Export";
        Dispatcher.UIThread.RunJobs();
        viewModel.SelectedSyncPage = "Import";
        Dispatcher.UIThread.RunJobs();
        return new OpenedHost(view, viewModel, host, services, picker, settings);
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
        CountingFilePicker Picker,
        IAppSettingsService Settings) : IDisposable
    {
        public void Dispose()
        {
            Settings.Current.KeePassRecentVaults.Clear();
            Host.Close();
            Dispatcher.UIThread.RunJobs();
            Services.Dispose();
        }
    }

    /// <summary>
    /// Counts the times the window reached for a dialog, because "the file came in without sending the
    /// person through a picker" is only visible as a number. It never answers, so a code path that did ask
    /// would also lose its file and fail somewhere louder.
    /// </summary>
    private sealed class CountingFilePicker : IFileSystemPickerService
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
            return Task.FromResult<PickedBinaryFile?>(null);
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
