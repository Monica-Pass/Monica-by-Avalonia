using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.ImportExport;
using Monica.App.Features.Sync;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.UiTests;

/// <summary>
/// The KeePass edit surface asked where it is actually seen: a real <see cref="SyncImportView"/> hosted
/// in a shown window, driven by a real view model whose file picker hands it a real <c>.kdbx</c>. The
/// named-control assertions elsewhere in this suite only prove a control exists in a template; these
/// prove the pane opens, the detail it replaces steps out of the way, and the password stays masked
/// until the user asks otherwise.
/// </summary>
[Collection(AvaloniaUiTestCollection.Name)]
public sealed class KeePassEditWorkflowUiTests
{
    private const string FixturePassword = KeePassSmokeVaultWriter.DefaultPassword;

    public KeePassEditWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public async Task KeePass_entry_editor_renders_over_the_detail_and_keeps_the_secret_masked()
    {
        var fixturePath = Path.Combine(
            Path.GetTempPath(),
            "monica-uitests",
            $"keepass-edit-{Guid.NewGuid():N}.kdbx");
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
        try
        {
            // Deriving the key costs a second of Argon2, and this test runs on the UI thread, so the
            // fixture is built off it rather than blocking the thread the assertions pump.
            var info = await Task.Run(() =>
                KeePassSmokeVaultWriter.Write(fixturePath, FixturePassword, entries: 3, groups: 2));
            Assert.Equal(3, info.Entries);
            var content = await File.ReadAllBytesAsync(
                fixturePath,
                TestContext.Current.CancellationToken);
            var picker = new SingleKeePassFileService(new PickedBinaryFile(info.FileName, content, fixturePath));

            var window = new Monica.App.MainWindow();
            using var services = Monica.App.App.ConfigureServices(window, collection =>
            {
                collection.AddSingleton<IFileSystemPickerService>(picker);
            });
            var viewModel = services.GetRequiredService<MainWindowViewModel>();

            await viewModel.SelectKeePassFileCommand.ExecuteAsync(null);
            Assert.Equal(info.FileName, viewModel.KeePassSelectedFileName);

            viewModel.KeePassImportPassword = FixturePassword;
            await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);
            Assert.True(viewModel.HasKeePassImportPreview);

            var view = new SyncImportView { DataContext = viewModel };
            var host = new Window { Width = 1280, Height = 800, Content = view };
            viewModel.SelectedSyncPage = "Import";
            host.Show();
            Dispatcher.UIThread.RunJobs();

            var tabs = view.FindControl<TabControl>("ImportSourceTabs")!;
            tabs.SelectedItem = view.FindControl<TabItem>("KeePassImportTab")!;
            Dispatcher.UIThread.RunJobs();
            try
            {
                var card = view.FindControl<StackPanel>("KeePassImportCard")!;
                Assert.True(card.Bounds.Width > 0 && card.Bounds.Height > 0);
                Assert.True(
                    view.FindControl<Border>("KeePassPreviewCard")!.IsVisible,
                    "the opened database summary is not on screen");
                var tree = view.FindControl<VaultFolderTree>("KeePassBrowseTree")!;
                Assert.True(tree.IsVisible);
                Assert.True(tree.Bounds.Width > 0 && tree.Bounds.Height > 0);

                var editButton = view.FindControl<Button>("EditKeePassEntryButton")!;
                Assert.False(editButton.IsVisible);

                // The tree hides entries until their folder is open, exactly as it does for a user.
                var folder = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.Kind == KeePassTreeRowKind.Folder && row.Group!.Name == "Folder 1");
                await viewModel.ToggleKeePassFolderCommand.ExecuteAsync(folder);
                var entryRow = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.IsEntryRow && row.Entry!.Title == "Entry 000001");
                await viewModel.SelectKeePassRowCommand.ExecuteAsync(entryRow);
                Dispatcher.UIThread.RunJobs();

                Assert.True(editButton.IsVisible);
                var editorPane = view.FindControl<StackPanel>("KeePassEntryEditorPane")!;
                Assert.False(editorPane.IsVisible);

                await viewModel.EditKeePassEntryCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();

                Assert.True(viewModel.HasKeePassEditor);
                Assert.True(editorPane.IsVisible);
                var titleBox = view.FindControl<TextBox>("KeePassEditTitleBox")!;
                Assert.Equal("Entry 000001", titleBox.Text);
                Assert.True(titleBox.Bounds.Width > 0);
                Assert.Equal(
                    "user1@example.com",
                    view.FindControl<TextBox>("KeePassEditUserBox")!.Text);

                var maskedBox = view.FindControl<TextBox>("KeePassEditPasswordBox")!;
                var revealedBox = view.FindControl<TextBox>("KeePassEditPasswordRevealedBox")!;
                var maskedTotpBox = view.FindControl<TextBox>("KeePassEditTotpBox")!;
                var revealedTotpBox = view.FindControl<TextBox>("KeePassEditTotpRevealedBox")!;
                Assert.Equal('*', maskedBox.PasswordChar);
                Assert.Equal('*', maskedTotpBox.PasswordChar);
                Assert.True(maskedBox.IsVisible);
                Assert.True(maskedTotpBox.IsVisible);
                Assert.False(revealedBox.IsVisible);
                Assert.False(revealedTotpBox.IsVisible);

                view.FindControl<ToggleButton>("KeePassPasswordVisibilityToggle")!.IsChecked = true;
                Dispatcher.UIThread.RunJobs();

                Assert.False(maskedBox.IsVisible);
                Assert.True(revealedBox.IsVisible);
                Assert.False(string.IsNullOrEmpty(revealedBox.Text));
                Assert.False(maskedTotpBox.IsVisible);
                Assert.True(revealedTotpBox.IsVisible);
                // Only the shape of the key is proved here; printing the text would carry the seed
                // out of the test run.
                Assert.True(revealedTotpBox.Text?.StartsWith("otpauth://", StringComparison.Ordinal) == true);

                view.FindControl<ToggleButton>("KeePassPasswordVisibilityToggle")!.IsChecked = false;
                Dispatcher.UIThread.RunJobs();
                Assert.True(maskedBox.IsVisible);
                Assert.True(maskedTotpBox.IsVisible);
                Assert.False(revealedBox.IsVisible);
                Assert.False(revealedTotpBox.IsVisible);

                // Applying retires the form and puts the unsaved-changes warning on screen; the
                // detail side must come back so the pane never leaves a hole where the entry was.
                viewModel.KeePassEditorPublic!.Title = "Edited in the UI test";
                await viewModel.ApplyKeePassEntryEditCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();

                Assert.False(viewModel.HasKeePassEditor);
                Assert.False(editorPane.IsVisible);
                Assert.True(viewModel.KeePassVaultIsDirty);
                var unsaved = view.FindControl<TextBlock>("KeePassUnsavedChangesText")!;
                Assert.True(unsaved.IsVisible);
                Assert.False(string.IsNullOrWhiteSpace(unsaved.Text));
                Assert.Equal("Edited in the UI test", viewModel.KeePassEntryDetailsPublic?.Title);

                // Nothing in this test reached the file: the bytes on disk are the ones it was
                // opened from, so the rendered edit is still only a stage.
                Assert.True(
                    (await File.ReadAllBytesAsync(
                         fixturePath,
                         TestContext.Current.CancellationToken)).AsSpan().SequenceEqual(content));
            }
            finally
            {
                host.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            TryDelete(fixturePath);
        }
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

    private sealed class SingleKeePassFileService(PickedBinaryFile file) : IFileSystemPickerService
    {
        public PlatformIntegrationCapability Capability { get; } = PlatformIntegrationService.Available(
            PlatformFeatureKeys.FilePicker,
            "Test file picker");

        public Task<PickedBinaryFile?> OpenBinaryFileAsync(
            string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => Task.FromResult<PickedBinaryFile?>(file);

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
    }
}
