using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.ImportExport;
using Monica.App.Features.Sync;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.UiTests;

/// <summary>
/// The version list asked where a person sees it: a pane that is really on screen, an entry that was
/// really edited through the form, and the revert really run from the row the template produced. The
/// platform suite proves a .kdbx keeps and gives back its versions; this proves the pane shows them,
/// keeps them tied to the entry on screen, and never writes a password into a row.
/// </summary>
[Collection(AvaloniaUiTestCollection.Name)]
public sealed class KeePassHistoryWorkflowUiTests
{
    private const string FixturePassword = KeePassSmokeVaultWriter.DefaultPassword;
    private const string SourceTitle = "Entry 000001";
    private const string EditedTitle = "Renamed in the UI test";

    [Fact]
    public async Task KeePass_version_list_appears_for_the_selected_entry_and_reverts_it()
    {
        var fixturePath = Path.Combine(
            Path.GetTempPath(),
            "monica-uitests",
            $"keepass-history-{Guid.NewGuid():N}.kdbx");
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
        try
        {
            var info = await Task.Run(() =>
                KeePassSmokeVaultWriter.Write(fixturePath, FixturePassword, entries: 2, groups: 1));
            var content = await File.ReadAllBytesAsync(fixturePath, TestContext.Current.CancellationToken);
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

            // A folder has no versions of its own, so the section is not realized for one at all.
            var folder = Assert.Single(
                viewModel.KeePassTreeRowsPublic,
                row => row.Kind == KeePassTreeRowKind.Folder && row.Group!.Path == "Folder 1");
            await viewModel.ToggleKeePassFolderCommand.ExecuteAsync(folder);
            await viewModel.SelectKeePassRowCommand.ExecuteAsync(folder);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(view.TryInPane<StackPanel>("KeePassHistorySection"));

            try
            {
                var entryRow = Assert.Single(
                    viewModel.KeePassTreeRowsPublic,
                    row => row.IsEntryRow && row.Entry!.Title == SourceTitle);
                await viewModel.SelectKeePassRowCommand.ExecuteAsync(entryRow);
                Dispatcher.UIThread.RunJobs();

                var section = view.InPane<StackPanel>("KeePassHistorySection")!;
                Assert.True(section.IsVisible);
                var list = view.InPane<ItemsControl>("KeePassHistoryList")!;
                Assert.False(list.IsVisible);
                Assert.Empty(viewModel.KeePassHistoryVersions);

                // One edit through the form is what puts a version in the file's own history list.
                await viewModel.EditKeePassEntryCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                viewModel.KeePassEditorPublic!.Title = EditedTitle;
                await viewModel.ApplyKeePassEntryEditCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(EditedTitle, viewModel.KeePassEntryDetailsPublic?.Title);

                // Stale versions of the entry just edited would be worse than none, so the detail
                // panel dropping its projection has to drop the list with it.
                Assert.Empty(viewModel.KeePassHistoryVersions);
                Assert.False(list.IsVisible);

                await viewModel.ShowKeePassHistoryCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();

                var version = Assert.Single(viewModel.KeePassHistoryVersions);
                Assert.Equal(0, version.Index);
                Assert.Equal(SourceTitle, version.PrimaryText);
                Assert.True(list.IsVisible);
                Assert.True(section.Bounds.Height > 0);

                var restoreButtons = list.GetVisualDescendants()
                    .OfType<Button>()
                    .Where(button => button.Name == "RestoreKeePassHistoryButton")
                    .ToArray();
                var restore = Assert.Single(restoreButtons);

                // Raising Button.ClickEvent was measured not to run a Command-bound button, so what is
                // asserted here is the hop the template has to get right on its own.
                Assert.Same(viewModel.RestoreKeePassHistoryVersionCommand, restore.Command);
                Assert.Same(version, restore.CommandParameter);
                Assert.True(restore.Command!.CanExecute(restore.CommandParameter));

                var rendered = string.Join(
                    "|",
                    list.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
                Assert.Contains(SourceTitle, rendered);
                Assert.DoesNotContain(EditedTitle, rendered);

                // The row names a version; it must not carry what that version held. The master key is
                // one needle, but the entry has its own secrets, and the fixture's vocabulary for
                // "Entry 000001" is known here because this test wrote that vault.
                foreach (var secret in new[]
                {
                    FixturePassword,
                    "secret-1",
                    "ticket-000001",
                    "JBSWY3DPEHPK3PXP",
                    new string('n', 96),
                })
                {
                    Assert.False(
                        rendered.Contains(secret, StringComparison.Ordinal),
                        "the version list rendered a secret the entry holds");
                }

                await viewModel.RestoreKeePassHistoryVersionCommand.ExecuteAsync(version);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(SourceTitle, viewModel.KeePassEntryDetailsPublic?.Title);
                Assert.Contains(
                    SourceTitle,
                    viewModel.KeePassTreeRowsPublic
                        .Where(row => row.IsEntryRow)
                        .Select(row => row.Entry!.Title));

                // Reverting is itself an edit: the shape it replaced is now the newest version.
                Assert.Equal(2, viewModel.KeePassHistoryVersions.Count);
                Assert.Equal(EditedTitle, viewModel.KeePassHistoryVersions[0].PrimaryText);
                Assert.True(viewModel.KeePassVaultIsDirty);

                // None of it reached the file - the bytes are still the ones it was opened from.
                Assert.True(
                    (await File.ReadAllBytesAsync(fixturePath, TestContext.Current.CancellationToken))
                    .AsSpan().SequenceEqual(content));
            }
            finally
            {
                host.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            try
            {
                File.Delete(fixturePath);
            }
            catch (IOException)
            {
                // Best effort: a fixture the OS still holds is not worth failing a green run over.
            }
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
