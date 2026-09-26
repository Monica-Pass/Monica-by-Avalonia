using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
                var tree = view.InPane<VaultFolderTree>("KeePassBrowseTree")!;
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
                var editorPane = view.InPane<StackPanel>("KeePassEntryEditorPane")!;
                Assert.False(editorPane.IsVisible);

                await viewModel.EditKeePassEntryCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();

                Assert.True(viewModel.HasKeePassEditor);
                Assert.True(editorPane.IsVisible);
                var titleBox = view.InPane<TextBox>("KeePassEditTitleBox")!;
                Assert.Equal("Entry 000001", titleBox.Text);
                Assert.True(titleBox.Bounds.Width > 0);
                Assert.Equal(
                    "user1@example.com",
                    view.InPane<TextBox>("KeePassEditUserBox")!.Text);

                var maskedBox = view.InPane<TextBox>("KeePassEditPasswordBox")!;
                var revealedBox = view.InPane<TextBox>("KeePassEditPasswordRevealedBox")!;
                var maskedTotpBox = view.InPane<TextBox>("KeePassEditTotpBox")!;
                var revealedTotpBox = view.InPane<TextBox>("KeePassEditTotpRevealedBox")!;
                Assert.Equal('*', maskedBox.PasswordChar);
                Assert.Equal('*', maskedTotpBox.PasswordChar);
                Assert.True(maskedBox.IsVisible);
                Assert.True(maskedTotpBox.IsVisible);
                Assert.False(revealedBox.IsVisible);
                Assert.False(revealedTotpBox.IsVisible);

                view.InPane<ToggleButton>("KeePassPasswordVisibilityToggle")!.IsChecked = true;
                Dispatcher.UIThread.RunJobs();

                Assert.False(maskedBox.IsVisible);
                Assert.True(revealedBox.IsVisible);
                Assert.False(string.IsNullOrEmpty(revealedBox.Text));
                Assert.False(maskedTotpBox.IsVisible);
                Assert.True(revealedTotpBox.IsVisible);
                // Only the shape of the key is proved here; printing the text would carry the seed
                // out of the test run.
                Assert.True(revealedTotpBox.Text?.StartsWith("otpauth://", StringComparison.Ordinal) == true);

                view.InPane<ToggleButton>("KeePassPasswordVisibilityToggle")!.IsChecked = false;
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

    [Fact]
    public async Task KeePass_tree_renders_with_folder_and_entry_management_wired()
    {
        var fixturePath = Path.Combine(
            Path.GetTempPath(),
            "monica-uitests",
            $"keepass-manage-{Guid.NewGuid():N}.kdbx");
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
        try
        {
            var info = await Task.Run(() =>
                KeePassSmokeVaultWriter.Write(fixturePath, FixturePassword, entries: 3, groups: 2));
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
            viewModel.KeePassImportPassword = FixturePassword;
            await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);

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
                var tree = view.InPane<VaultFolderTree>("KeePassBrowseTree")!;
                Assert.True(tree.Bounds.Width > 0 && tree.Bounds.Height > 0);
                Assert.True(tree.CanManageRows);
                // Each of these is the exact command the panel declares, so a binding that lands on a
                // neighbouring command - valid at compile time, wrong for the user - shows up here.
                Assert.Same(viewModel.CreateKeePassFolderCommand, tree.CreateFolderCommand);
                Assert.Same(viewModel.RenameKeePassFolderCommand, tree.RenameFolderCommand);
                Assert.Same(viewModel.DeleteKeePassFolderCommand, tree.DeleteFolderCommand);
                Assert.Same(viewModel.MoveKeePassFolderCommand, tree.MoveFolderCommand);
                Assert.Same(viewModel.MoveKeePassEntryCommand, tree.MoveEntryToFolderCommand);
                Assert.Same(viewModel.EditKeePassEntryCommand, tree.EditEntryCommand);
                // A .kdbx carries its own recycle bin, so this host wires the split pair rather than the
                // single delete the library page uses.
                Assert.Same(viewModel.DeleteKeePassEntryCommand, tree.MoveToRecycleBinCommand);
                Assert.Same(viewModel.DeleteKeePassEntryPermanentlyCommand, tree.DeleteEntryPermanentlyCommand);
                Assert.False(tree.SelectedEntryInRecycleBin);
                // The root is selected at open and the root is not something a user may delete.
                Assert.False(tree.CanManageSelected);

                // Only the root is selected, and a folder has no detail of its own, so the column beside
                // the tree is down: nothing is painted there, and nothing here claims otherwise.
                Assert.False(viewModel.ShowsKeePassDetailColumn);
                Assert.Null(view.TryInPane<StackPanel>("KeePassEntryEditorPane"));
                // Raising Button.ClickEvent was measured not to run a Command-bound button - only the
                // pointer pipeline calls OnClick - so the hop proved here is the one the template owns:
                // the binding resolved to the live command, and clicking it is what opens the form.
                var newEntryButton = view.FindControl<Button>("NewKeePassEntryButton")!;
                Assert.Same(viewModel.NewKeePassEntryCommand, newEntryButton.Command);
                Assert.True(newEntryButton.IsEnabled);
                Assert.True(newEntryButton.Command!.CanExecute(null));
                newEntryButton.Command.Execute(null);
                Dispatcher.UIThread.RunJobs();

                // The draft used to land in the view model while the column hosting it stayed collapsed:
                // a person clicked 新建条目 and faced an empty panel with the cursor nowhere. So this is
                // measured on screen, not on the property - the form is realized and has room to type in.
                Assert.True(viewModel.HasKeePassEditor);
                Assert.True(viewModel.ShowsKeePassDetailColumn);
                var editorPane = view.InPane<StackPanel>("KeePassEntryEditorPane");
                Assert.True(editorPane.IsVisible);
                Assert.True(editorPane.Bounds.Width > 0 && editorPane.Bounds.Height > 0);
                var titleBox = view.InPane<TextBox>("KeePassEditTitleBox");
                Assert.Equal("", titleBox.Text);
                Assert.True(titleBox.Bounds.Width > 0);
                titleBox.Text = "Filed from the UI test";
                await viewModel.ApplyKeePassEntryEditCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();

                Assert.False(viewModel.HasKeePassEditor);
                Assert.Contains(
                    "Filed from the UI test",
                    viewModel.KeePassTreeRowsPublic.Select(row => row.Label));

                // An entry of an opened .kdbx moves by drag onto a folder, so the folder-picker item
                // the library page shows has to stay off this menu rather than sit on it inert.
                var filedRow = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.IsEntryRow && row.Label == "Filed from the UI test");
                await viewModel.SelectKeePassRowCommand.ExecuteAsync(filedRow);
                Dispatcher.UIThread.RunJobs();
                Assert.True(tree.IsEntrySelection);
                Assert.True(tree.ShowsEntryCommands);
                Assert.False(tree.ShowsEntryFolderPicker);

                // Both depths of delete are on the menu, and the plain one the library page uses is off
                // it; an entry that already sits in the bin has only the permanent way out left.
                Assert.True(tree.ShowsEntryRecycleItem);
                Assert.True(tree.ShowsEntryPermanentItem);
                Assert.False(tree.ShowsEntryPlainDeleteItem);
                Assert.False(viewModel.KeePassSelectedEntryInRecycleBin);
                tree.SelectedEntryInRecycleBin = true;
                Assert.False(tree.ShowsEntryRecycleItem);
                Assert.True(tree.ShowsEntryPermanentItem);
                tree.SelectedEntryInRecycleBin = false;
                Assert.True(tree.ShowsEntryRecycleItem);

                // That entry fills the column beside the tree. A folder has no detail of its own, so
                // selecting one has to step the old projection down rather than leave the panel
                // answering a row nobody has selected.
                Assert.NotNull(viewModel.KeePassEntryDetailsPublic);
                var folderRow = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.IsEntryRow is false && row.Label == "Folder 1");
                await viewModel.SelectKeePassRowCommand.ExecuteAsync(folderRow);
                Dispatcher.UIThread.RunJobs();
                Assert.Null(viewModel.KeePassEntryDetailsPublic);
                Assert.False(viewModel.ShowsKeePassDetailColumn);
                await viewModel.SelectKeePassRowCommand.ExecuteAsync(filedRow);
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(viewModel.KeePassEntryDetailsPublic);
                Assert.True(viewModel.ShowsKeePassDetailColumn);

                // The inline naming box is the tree's own property; the view model has to receive what
                // the user types into it and put the next name back for the box to show it.
                tree.SetValue(VaultFolderTree.FolderNameProperty, "Renamed in the UI test");
                Assert.Equal("Renamed in the UI test", viewModel.KeePassFolderName);
                viewModel.KeePassFolderName = "Created in the UI test";
                Assert.Equal("Created in the UI test", tree.GetValue(VaultFolderTree.FolderNameProperty));

                tree.CreateFolderCommand!.Execute(null);
                Dispatcher.UIThread.RunJobs();

                Assert.Contains(
                    "Created in the UI test",
                    viewModel.KeePassTreeRowsPublic.Select(row => row.Label));
                Assert.True(viewModel.KeePassVaultIsDirty);

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

    /// <summary>
    /// The detail pane is what a user reads after adding or filing an entry, and it is a two-column
    /// table: a field name on the left and its value on the right. Both landing in the left column
    /// paints the value over the name, which reads as garbage on a screen and as nothing at all in a
    /// binding assertion - so this measures where the two texts actually ended up.
    /// </summary>
    [Fact]
    public async Task KeePass_detail_rows_put_the_value_beside_their_label()
    {
        var fixturePath = Path.Combine(
            Path.GetTempPath(),
            "monica-uitests",
            $"keepass-detail-{Guid.NewGuid():N}.kdbx");
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
        try
        {
            var info = await Task.Run(() =>
                KeePassSmokeVaultWriter.Write(fixturePath, FixturePassword, entries: 3, groups: 2));
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
            viewModel.KeePassImportPassword = FixturePassword;
            await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);

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
                // Entries live inside folders, and the tree opens with them collapsed.
                var closedFolder = viewModel.KeePassTreeRowsPublic.First(row =>
                    row is { IsEntryRow: false, IsExpanded: false, Group.HasEntries: true });
                await viewModel.ToggleKeePassFolderCommand.ExecuteAsync(closedFolder);
                var entryRow = viewModel.KeePassTreeRowsPublic.First(row => row.IsEntryRow);
                await viewModel.SelectKeePassRowCommand.ExecuteAsync(entryRow);
                Dispatcher.UIThread.RunJobs();

                var detailPanes = view.GetVisualDescendants()
                    .OfType<ContentControl>()
                    .Where(control => control.Content is PasswordDetailViewModel)
                    .ToList();
                var detailPane = Assert.Single(detailPanes);
                var fieldRows = detailPane.GetVisualDescendants()
                    .OfType<Grid>()
                    .Where(row => row.ColumnDefinitions.Count == 2)
                    .ToList();
                Assert.NotEmpty(fieldRows);
                foreach (var row in fieldRows)
                {
                    var texts = row.GetVisualChildren().OfType<TextBlock>().ToList();
                    Assert.Equal(2, texts.Count);
                    var label = texts[0];
                    var value = texts[1];
                    Assert.True(
                        value.Bounds.X >= label.Bounds.Right,
                        $"a detail value starts at x={value.Bounds.X} while its label runs to " +
                        $"x={label.Bounds.Right}, so the two are painted on top of each other");
                }
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

        public Task<PickedSaveTarget?> PickSaveFileTargetAsync(
            string title,
            string suggestedFileName,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => Task.FromResult<PickedSaveTarget?>(null);
    }
}
