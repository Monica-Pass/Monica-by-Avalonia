using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.ImportExport;
using Monica.App.Features.Sync;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.UiTests;

/// <summary>
/// The two ways out of a .kdbx's own recycle bin, measured on the screen that shows them. A row in that
/// folder has exactly one reversible move left and the folder itself is the only place a whole set may be
/// destroyed, so what is proved here is which row each action is offered on, that the offer runs the
/// command the pane declares rather than a neighbour of it, and that undoing a mis-click puts the entry
/// back where it came from without ever naming the secret it carries.
/// </summary>
[Collection(AvaloniaUiTestCollection.Name)]
public sealed class KeePassRecycleBinWorkflowUiTests
{
    private const string FixturePassword = KeePassSmokeVaultWriter.DefaultPassword;

    /// <summary>
    /// Only the fixture's own literal, and only to prove it never reaches a rendered string; the value is
    /// compared against, never printed.
    /// </summary>
    private const string ProtectedValueMarker = "secret-";

    private const string FirstEntryTitle = "Entry 000001";

    private const string SecondEntryTitle = "Entry 000002";

    public KeePassRecycleBinWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    /// <summary>
    /// Recycles one entry through the tree's own command and asks the screen for the menu it would show:
    /// the reversible item has to leave the row it no longer applies to and the way back out has to appear
    /// on the row it does. Then it takes the entry out and proves the destination is the folder it came
    /// from, that the change is staged rather than written, and that the notice about it carries the
    /// entry's name and folder but none of its contents.
    /// </summary>
    [Fact]
    public async Task KeePass_bin_row_offers_restore_and_takes_the_entry_back_to_its_folder()
    {
        var fixturePath = Path.Combine(
            Path.GetTempPath(),
            "monica-uitests",
            $"keepass-restore-{Guid.NewGuid():N}.kdbx");
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
        try
        {
            var info = await Task.Run(() =>
                KeePassSmokeVaultWriter.Write(fixturePath, FixturePassword, entries: 3, groups: 2));
            var content = await File.ReadAllBytesAsync(
                fixturePath,
                TestContext.Current.CancellationToken);
            using var opened = await OpenAsync(info, fixturePath, content);
            var view = opened.View;
            var viewModel = opened.ViewModel;
            var dialog = opened.Dialog;
            try
            {
                var tree = view.InPane<VaultFolderTree>("KeePassBrowseTree")!;
                var folderRow = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.Kind == KeePassTreeRowKind.Folder && row.Group!.Name == "Folder 1");
                await viewModel.ToggleKeePassFolderCommand.ExecuteAsync(folderRow);
                var entryRow = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.IsEntryRow && row.Entry!.Title == SecondEntryTitle);
                await viewModel.SelectKeePassRowCommand.ExecuteAsync(entryRow);
                Dispatcher.UIThread.RunJobs();

                // Before the row has been deleted, the bin's own items have no business on it: a restore
                // offer there would undo a move nobody made.
                Assert.Same(viewModel.DeleteKeePassEntryCommand, tree.MoveToRecycleBinCommand);
                Assert.True(tree.ShowsEntryRecycleItem);
                Assert.False(tree.ShowsEntryRestoreItem);
                Assert.False(tree.SelectedEntryInRecycleBin);
                Assert.False(tree.SelectedFolderIsRecycleBin);

                await viewModel.DeleteKeePassEntryCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();

                // Deleting into the bin still asks, and the asking is the pane's dialog rather than a
                // second click on the menu item.
                Assert.Equal(1, dialog.ContentDialogs);
                Assert.Equal(0, dialog.TypedDialogs);
                var binRow = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.Kind == KeePassTreeRowKind.Folder && row.Group!.IsRecycleBin);
                var binnedRow = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.IsEntryRow && row.Entry!.Title == SecondEntryTitle);
                Assert.Equal(binRow.Group!.Uuid, binnedRow.Entry!.GroupUuid);

                await viewModel.SelectKeePassRowCommand.ExecuteAsync(binnedRow);
                Dispatcher.UIThread.RunJobs();

                // The pane declares both bin commands; asserting the pair here is what catches a binding
                // that lands restore on the empty command or the other way round.
                Assert.Same(viewModel.RestoreKeePassEntryCommand, tree.RestoreEntryCommand);
                Assert.Same(viewModel.EmptyKeePassRecycleBinCommand, tree.EmptyRecycleBinCommand);
                Assert.True(tree.SelectedEntryInRecycleBin);
                Assert.True(tree.ShowsEntryRestoreItem);
                Assert.False(tree.ShowsEntryRecycleItem);
                Assert.False(tree.ShowsEmptyRecycleBinItem);

                // The folder is the other row's host: emptying belongs to it and not to the entry inside.
                await viewModel.SelectKeePassRowCommand.ExecuteAsync(binRow);
                Dispatcher.UIThread.RunJobs();
                Assert.True(tree.SelectedFolderIsRecycleBin);
                Assert.True(tree.ShowsEmptyRecycleBinItem);
                Assert.False(tree.ShowsEntryRestoreItem);

                await viewModel.SelectKeePassRowCommand.ExecuteAsync(binnedRow);
                Dispatcher.UIThread.RunJobs();
                await viewModel.RestoreKeePassEntryCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();

                Assert.False(tree.ShowsEntryRestoreItem);
                Assert.True(viewModel.KeePassVaultIsDirty);
                Assert.False(viewModel.IsStatusMessageFailure);
                var restoredRow = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.IsEntryRow && row.Entry!.Title == SecondEntryTitle);
                Assert.Equal(folderRow.Group!.Uuid, restoredRow.Entry!.GroupUuid);

                var notice = viewModel.StatusMessage;
                Assert.Contains(SecondEntryTitle, notice, StringComparison.Ordinal);
                Assert.Contains("Folder 1", notice, StringComparison.Ordinal);
                Assert.DoesNotContain(ProtectedValueMarker, notice, StringComparison.Ordinal);

                // Back out of the bin the row is an ordinary entry again: the reversible item returns to
                // its menu, which is the difference between a restored row and a stranded one.
                await viewModel.SelectKeePassRowCommand.ExecuteAsync(restoredRow);
                Dispatcher.UIThread.RunJobs();
                Assert.False(tree.SelectedEntryInRecycleBin);
                Assert.True(tree.ShowsEntryRecycleItem);

                // Nothing in this run reached the file: the bytes on disk are the ones it was opened from,
                // so the restored row is still only a stage.
                Assert.True(
                    (await File.ReadAllBytesAsync(
                        fixturePath,
                        TestContext.Current.CancellationToken)).AsSpan().SequenceEqual(content));
            }
            finally
            {
                opened.Host.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            TryDelete(fixturePath);
        }
    }

    /// <summary>
    /// Empties the bin from the folder row. The typed confirmation is the product's existing gate before
    /// a whole set of secrets is destroyed, so both answers are walked: a refusal must take nothing, and
    /// an agreement must take everything the folder holds - reported as a count, because the thing being
    /// destroyed is described by how much of it there was, not by name.
    /// </summary>
    [Fact]
    public async Task Emptying_the_bin_from_its_folder_asks_first_and_then_takes_every_row()
    {
        var fixturePath = Path.Combine(
            Path.GetTempPath(),
            "monica-uitests",
            $"keepass-empty-{Guid.NewGuid():N}.kdbx");
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
        try
        {
            var info = await Task.Run(() =>
                KeePassSmokeVaultWriter.Write(fixturePath, FixturePassword, entries: 3, groups: 2));
            var content = await File.ReadAllBytesAsync(
                fixturePath,
                TestContext.Current.CancellationToken);
            using var opened = await OpenAsync(info, fixturePath, content);
            var view = opened.View;
            var viewModel = opened.ViewModel;
            var dialog = opened.Dialog;
            try
            {
                var tree = view.InPane<VaultFolderTree>("KeePassBrowseTree")!;
                var folderRow = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.Kind == KeePassTreeRowKind.Folder && row.Group!.Name == "Folder 1");
                var otherFolderRow = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.Kind == KeePassTreeRowKind.Folder && row.Group!.Name == "Folder 2");
                await viewModel.ToggleKeePassFolderCommand.ExecuteAsync(folderRow);

                foreach (var title in new[] { FirstEntryTitle, SecondEntryTitle })
                {
                    var entryRow = Assert.Single(
                        viewModel.KeePassTreeRowsPublic,
                        row => row.IsEntryRow && row.Entry!.Title == title);
                    await viewModel.SelectKeePassRowCommand.ExecuteAsync(entryRow);
                    await viewModel.DeleteKeePassEntryCommand.ExecuteAsync(null);
                }

                Dispatcher.UIThread.RunJobs();
                var binRow = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.Kind == KeePassTreeRowKind.Folder && row.Group!.IsRecycleBin);
                Assert.Equal(2, CountRowsUnder(viewModel, binRow.Group!.Uuid));

                await viewModel.SelectKeePassRowCommand.ExecuteAsync(binRow);
                Dispatcher.UIThread.RunJobs();
                Assert.True(tree.ShowsEmptyRecycleBinItem);

                dialog.Approves = false;
                await viewModel.EmptyKeePassRecycleBinCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();

                // The gate is not decoration: a refused confirmation has to leave the set where it was.
                Assert.Equal(1, dialog.TypedDialogs);
                Assert.NotEmpty(dialog.LastRequiredPhrase);
                Assert.False(viewModel.IsStatusMessageFailure);
                Assert.Equal(1, CountFolderRowsNamed(viewModel, "Recycle Bin"));
                Assert.Equal(2, CountRowsUnder(viewModel, binRow.Group!.Uuid));

                dialog.Approves = true;
                await viewModel.EmptyKeePassRecycleBinCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(2, dialog.TypedDialogs);
                Assert.Contains("2", dialog.LastTypedMessage, StringComparison.Ordinal);

                // What is on screen once the set is destroyed: the bin folder and both rows that were
                // inside it are gone. Counted rather than matched against a collection, so a failure
                // reports how many rows were left instead of printing the fields of deleted entries.
                Assert.Equal(0, CountFolderRowsNamed(viewModel, "Recycle Bin"));
                Assert.Equal(0, CountRowsUnder(viewModel, binRow.Group!.Uuid));

                // The third entry never went near the bin, so emptying must not have touched it. Its
                // folder is closed on screen, so it is opened the way a user would before the row is read.
                await viewModel.ToggleKeePassFolderCommand.ExecuteAsync(otherFolderRow);
                Dispatcher.UIThread.RunJobs();
                var survivor = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.IsEntryRow && row.Entry!.Title == "Entry 000003");
                Assert.Equal(otherFolderRow.Group!.Uuid, survivor.Entry!.GroupUuid);
                Assert.True(viewModel.KeePassVaultIsDirty);
                Assert.False(viewModel.IsStatusMessageFailure);
                Assert.DoesNotContain(ProtectedValueMarker, viewModel.StatusMessage, StringComparison.Ordinal);

                Assert.True(
                    (await File.ReadAllBytesAsync(
                        fixturePath,
                        TestContext.Current.CancellationToken)).AsSpan().SequenceEqual(content));
            }
            finally
            {
                opened.Host.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            TryDelete(fixturePath);
        }
    }

    /// <summary>
    /// How many entries the bin holds according to the rows on screen, counted through the folder the rows
    /// say they sit in rather than by indentation, which the tree also uses for other things.
    /// </summary>
    private static int CountRowsUnder(MainWindowViewModel viewModel, string groupUuid) =>
        viewModel.KeePassTreeRowsPublic.Count(row =>
            row.IsEntryRow && string.Equals(row.Entry!.GroupUuid, groupUuid, StringComparison.OrdinalIgnoreCase));

    private static int CountFolderRowsNamed(MainWindowViewModel viewModel, string name) =>
        viewModel.KeePassTreeRowsPublic.Count(row =>
            row.Kind == KeePassTreeRowKind.Folder && row.Group!.Name == name);

    /// <summary>
    /// Opens the fixture through the picker seam, puts the KeePass pane on screen, and hands back the
    /// dialog fake the bin commands ask through. The key derivation costs Argon2 and this runs on the UI
    /// thread, so the fixture is written off it.
    /// </summary>
    private static async Task<OpenedVault> OpenAsync(
        KeePassSmokeVaultInfo info,
        string fixturePath,
        byte[] content)
    {
        var picked = new PickedBinaryFile(info.FileName, content, fixturePath);
        var dialog = new BinDialogRecorder();
        var picker = new SingleKeePassFileService(picked);
        var window = new Monica.App.MainWindow();
        var services = Monica.App.App.ConfigureServices(window, collection =>
        {
            collection.AddSingleton<IFileSystemPickerService>(picker);
            collection.AddSingleton<IConfirmationDialogService>(dialog);
        });
        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        await viewModel.SelectKeePassFileCommand.ExecuteAsync(null);
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

        return new OpenedVault(view, viewModel, host, services, dialog);
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

    private sealed record OpenedVault(
        SyncImportView View,
        MainWindowViewModel ViewModel,
        Window Host,
        ServiceProvider Services,
        BinDialogRecorder Dialog) : IDisposable
    {
        public void Dispose() => Services.Dispose();
    }

    /// <summary>
    /// Answers the two confirmation shapes separately, because the pane uses one for a single row and the
    /// other for destroying a set, and a command wired to the wrong gate would skip the typing step a
    /// person is asked to do.
    /// </summary>
    private sealed class BinDialogRecorder : IConfirmationDialogService
    {
        public bool Approves { get; set; } = true;

        public int ContentDialogs { get; private set; }

        public int TypedDialogs { get; private set; }

        public string LastRequiredPhrase { get; private set; } = "";

        public string LastTypedMessage { get; private set; } = "";

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText,
            string? closeButtonText = null,
            CancellationToken cancellationToken = default)
        {
            ContentDialogs++;
            return Task.FromResult(Approves);
        }

        public Task<bool> ConfirmTypedAsync(
            string title,
            string message,
            string requiredPhrase,
            string instruction,
            string primaryButtonText,
            string? closeButtonText = null,
            CancellationToken cancellationToken = default)
        {
            TypedDialogs++;
            LastRequiredPhrase = requiredPhrase;
            LastTypedMessage = message;
            return Task.FromResult(Approves);
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
