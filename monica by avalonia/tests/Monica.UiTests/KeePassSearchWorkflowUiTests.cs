using Avalonia.Controls;
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
/// The proof a search box has to make on a screen rather than in a session: the field is where a person
/// looks, typing into it replaces the hierarchy with a flat list that still says where each hit came
/// from, the line under it reports how many matched without repeating the query, and none of the
/// secrets the database holds reach a rendered pixel. The database is opened through the real file
/// picker seam and the tree is the real control, so a binding that points at a neighbouring command
/// shows up red here instead of as a box that does nothing.
/// </summary>
[Collection(AvaloniaUiTestCollection.Name)]
public sealed class KeePassSearchWorkflowUiTests
{
    private const string FixturePassword = KeePassSmokeVaultWriter.DefaultPassword;

    /// <summary>
    /// Only the fixture's own literal, and only to prove it never appears on screen; the value is
    /// compared against, never printed.
    /// </summary>
    private const string ProtectedValueMarker = "secret-";

    private const string ProtectedCustomFieldMarker = "ticket-";

    public KeePassSearchWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public async Task KeePass_search_flattens_the_tree_reports_the_count_and_keeps_the_secrets_in_the_file()
    {
        var fixturePath = Path.Combine(
            Path.GetTempPath(),
            "monica-uitests",
            $"keepass-search-{Guid.NewGuid():N}.kdbx");
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
        try
        {
            // Three folders, two entries each, so a hit that arrives through a folder-less field still
            // has a distinct path to report. Deriving the key costs Argon2 and this runs on the UI
            // thread, so the fixture is written off it.
            var info = await Task.Run(() =>
                KeePassSmokeVaultWriter.Write(fixturePath, FixturePassword, entries: 6, groups: 3));
            var content = await File.ReadAllBytesAsync(
                fixturePath,
                TestContext.Current.CancellationToken);
            using var opened = await OpenAsync(info, fixturePath, content);
            var view = opened.View;
            var viewModel = opened.ViewModel;
            var host = opened.Host;
            try
            {
                var searchField = view.InPane<SearchField>("KeePassSearchField")!;
                Assert.True(
                    searchField.Bounds.Width > 0 && searchField.Bounds.Height > 0,
                    "the search field is not on screen");
                var summary = view.InPane<TextBlock>("KeePassSearchSummary")!;
                Assert.False(summary.IsVisible);
                var tree = view.InPane<VaultFolderTree>("KeePassBrowseTree")!;

                // At rest the tree is a hierarchy with its entries folded away, which is what makes the
                // query below a real test: nothing it can match is on screen until it is typed.
                Assert.Contains(viewModel.KeePassTreeRowsPublic, row => !row.IsEntryRow);
                Assert.DoesNotContain(viewModel.KeePassTreeRowsPublic, row => row.IsEntryRow);

                // Driven through the control's own text box, so the two-way binding is part of the
                // evidence and not a property set behind the view's back.
                var box = searchField.InnerSearchBox!;
                box.Text = "example.com";
                Dispatcher.UIThread.RunJobs();

                Assert.True(viewModel.HasKeePassSearchText);
                Assert.True(summary.IsVisible);
                var rows = viewModel.KeePassTreeRowsPublic;
                Assert.Equal(6, rows.Count);
                Assert.All(rows, row =>
                {
                    Assert.True(row.IsEntryRow);
                    Assert.True(row.ShowsGroupPath);
                    // The indentation is gone, so the subtitle is the only place the folder can appear.
                    Assert.False(string.IsNullOrEmpty(row.Entry!.GroupPath));
                    Assert.Equal(row.Entry!.GroupPath, row.EntryDetail);
                });
                Assert.Equal(
                    ["Entry 000001", "Entry 000002", "Entry 000003", "Entry 000004", "Entry 000005", "Entry 000006"],
                    rows.Select(row => row.Label).ToArray());
                Assert.Equal(
                    ["Folder 1", "Folder 1", "Folder 2", "Folder 2", "Folder 3", "Folder 3"],
                    rows.Select(row => row.EntryDetail).ToArray());

                // The line says how many matched and never repeats what was typed - a search that
                // echoes could be read as a way to ask the database about a value.
                Assert.Contains("6", summary.Text);
                Assert.DoesNotContain("example.com", summary.Text);
                Assert.Equal(viewModel.KeePassSearchSummaryText, summary.Text);
                Assert.Equal("6 entries match.", viewModel.KeePassSearchSummaryText);

                var clearButton = searchField.InnerClearButton!;
                Assert.True(clearButton.IsVisible);
                Assert.Same(viewModel.ClearKeePassSearchCommand, clearButton.Command);

                // What the tree actually painted. The whole control is walked because the row texts are
                // generated by the shared template, not by the test.
                var painted = tree.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(text => text.Text ?? "")
                    .ToList();
                Assert.Contains("Entry 000006", painted);
                Assert.DoesNotContain(painted, text => text.Contains(ProtectedValueMarker, StringComparison.Ordinal));
                Assert.DoesNotContain(painted, text => text.Contains(ProtectedCustomFieldMarker, StringComparison.Ordinal));
                Assert.DoesNotContain(painted, text => text.Contains("otpauth", StringComparison.Ordinal));
                Assert.DoesNotContain(painted, text => text.Contains("otp://", StringComparison.Ordinal));

                // A folder created while the query is typed must not flash the hierarchy back onto the
                // screen: the rebuild runs and the query is re-issued over it.
                await viewModel.SelectKeePassRowCommand.ExecuteAsync(rows[0]);
                viewModel.KeePassFolderName = "Folder made mid-search";
                await viewModel.CreateKeePassFolderCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("example.com", viewModel.KeePassSearchText);
                Assert.Equal(6, viewModel.KeePassTreeRowsPublic.Count);
                Assert.All(viewModel.KeePassTreeRowsPublic, row => Assert.True(row.IsEntryRow));
                Assert.DoesNotContain(
                    "Folder made mid-search",
                    viewModel.KeePassTreeRowsPublic.Select(row => row.Label));
                // Nothing reached the file: the create is staged in the session, like every other edit.
                Assert.True(viewModel.KeePassVaultIsDirty);

                clearButton.Command!.Execute(null);
                Dispatcher.UIThread.RunJobs();

                Assert.False(viewModel.HasKeePassSearchText);
                Assert.Equal("", searchField.Text);
                Assert.False(summary.IsVisible);
                var restored = viewModel.KeePassTreeRowsPublic;
                Assert.Contains(restored, row => !row.IsEntryRow && row.Label == "Folder made mid-search");
                Assert.All(
                    restored.Where(row => row.IsEntryRow),
                    row => Assert.False(row.ShowsGroupPath));
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
    /// A list that stops at a page boundary has to say so. This is the difference between "these are
    /// your entries" and a person concluding an entry is gone, so the count that travels with the cap
    /// is checked where it is read - on the line under the box.
    /// </summary>
    [Fact]
    public async Task KeePass_search_says_what_it_left_out()
    {
        var fixturePath = Path.Combine(
            Path.GetTempPath(),
            "monica-uitests",
            $"keepass-search-truncated-{Guid.NewGuid():N}.kdbx");
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
        try
        {
            var info = await Task.Run(() =>
                KeePassSmokeVaultWriter.Write(fixturePath, FixturePassword, entries: 250, groups: 3));
            var content = await File.ReadAllBytesAsync(
                fixturePath,
                TestContext.Current.CancellationToken);
            using var opened = await OpenAsync(info, fixturePath, content);
            var view = opened.View;
            var viewModel = opened.ViewModel;
            var host = opened.Host;
            try
            {
                var searchField = view.InPane<SearchField>("KeePassSearchField")!;
                var summary = view.InPane<TextBlock>("KeePassSearchSummary")!;
                searchField.InnerSearchBox!.Text = "Entry 000";
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(KeePassVaultSession.SearchResultCap, viewModel.KeePassTreeRowsPublic.Count);
                Assert.True(summary.IsVisible);
                // Both figures have to be on the line: one number alone reads as a total, not a page.
                Assert.Contains("200", summary.Text);
                Assert.Contains("250", summary.Text);
                Assert.DoesNotContain("Entry 000", summary.Text);
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
    /// Opens the fixture through the picker seam and puts the KeePass pane on screen, which is the
    /// state both facts need and the state a binding only exists in once a template is applied.
    /// </summary>
    private static async Task<ScannedVault> OpenAsync(
        KeePassSmokeVaultInfo info,
        string fixturePath,
        byte[] content)
    {
        var picker = new SingleKeePassFileService(new PickedBinaryFile(info.FileName, content, fixturePath));
        var window = new Monica.App.MainWindow();
        var services = Monica.App.App.ConfigureServices(window, collection =>
        {
            collection.AddSingleton<IFileSystemPickerService>(picker);
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

        return new ScannedVault(view, viewModel, host, services);
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

    private sealed record ScannedVault(
        SyncImportView View,
        MainWindowViewModel ViewModel,
        Window Host,
        ServiceProvider Services) : IDisposable
    {
        public void Dispose() => Services.Dispose();
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
