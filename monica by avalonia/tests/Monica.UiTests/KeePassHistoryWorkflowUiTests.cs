using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.ImportExport;
using Monica.App.Features.Sync;
using Monica.App.Services;
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
            var (host, view, viewModel, content) = await OpenVaultOnKeePassTabAsync(fixturePath);

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
            TryDelete(fixturePath);
        }
    }

    /// <summary>
    /// The three numbers the file carries about how much of an entry's past it keeps, shown and changed
    /// from the pane. The platform suite proves the writer honours them; this proves they reach the
    /// screen in the unit a person reads them in, that typing into a box is what reaches the database and
    /// that the byte count behind the megabyte box survives a round trip untouched, and that a box the
    /// file cannot hold is refused rather than guessed at.
    /// </summary>
    [Fact]
    public async Task KeePass_history_policy_shows_the_file_s_numbers_and_takes_back_what_is_typed()
    {
        var fixturePath = Path.Combine(
            Path.GetTempPath(),
            "monica-uitests",
            $"keepass-policy-{Guid.NewGuid():N}.kdbx");
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
        try
        {
            var (host, view, viewModel, content) = await OpenVaultOnKeePassTabAsync(fixturePath);
            try
            {
                // No entry is selected at all: these numbers describe the library, so the rail that
                // carries them has to be up on its own.
                Assert.True(viewModel.ShowsKeePassRail);
                var section = view.InPane<StackPanel>("KeePassPolicySection")!;
                Assert.True(section.IsVisible);

                var maxItemsBox = view.InPane<TextBox>("KeePassPolicyMaxItemsBox")!;
                var daysBox = view.InPane<TextBox>("KeePassPolicyDaysBox")!;
                var sizeBox = view.InPane<TextBox>("KeePassPolicySizeBox")!;

                // What the screen shows is what the file holds, read back off the bytes by the platform
                // rather than recited from a constant that could drift with the fixture. The size is the
                // one number the file does not hold in the unit it shows: the box speaks megabytes, and
                // the byte count it was converted from must not be what a person reads or has to type.
                using var probe = await new KeePassVaultService().OpenAsync(
                    content,
                    Path.GetFileName(fixturePath),
                    FixturePassword,
                    fixturePath,
                    TestContext.Current.CancellationToken);
                var fromFile = await probe.ReadHistoryPolicyAsync(TestContext.Current.CancellationToken);
                Assert.Equal(fromFile.MaxItems.ToString(), maxItemsBox.Text);
                Assert.Equal(fromFile.MaintenanceDays.ToString(), daysBox.Text);
                Assert.Equal(
                    KeePassHistorySizeUnits.ToDisplayMegabytes(fromFile.MaxSizeBytes),
                    sizeBox.Text);
                Assert.NotEqual(fromFile.MaxSizeBytes.ToString(), sizeBox.Text);
                Assert.True(
                    fromFile.MaxSizeBytes > KeePassHistorySizeUnits.BytesPerMegabyte,
                    "the fixture no longer carries a size cap worth more than a megabyte, so the "
                        + "assertion above has nothing to be about");

                var rendered = string.Join(
                    "|",
                    section.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
                foreach (var secret in new[] { FixturePassword, "secret-1", "ticket-000001" })
                {
                    Assert.False(
                        rendered.Contains(secret, StringComparison.Ordinal),
                        "the policy rail rendered a secret the library holds");
                }

                var applyButton = view.InPane<Button>("ApplyKeePassPolicyButton")!;
                Assert.Same(viewModel.ApplyKeePassHistoryPolicyCommand, applyButton.Command);
                Assert.True(applyButton.Command!.CanExecute(null));

                // Typed into the boxes, not set on the view model: the hop from the screen to the value is
                // the one a person uses. The size box is deliberately left alone, because that is the
                // case the megabyte spelling makes ambiguous - a truncated view that must not come back
                // as a rounded number.
                maxItemsBox.Text = "3";
                daysBox.Text = "30";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("3", viewModel.KeePassPolicyMaxItemsText);
                Assert.Equal("30", viewModel.KeePassPolicyMaintenanceDaysText);
                Assert.Equal("6", viewModel.KeePassPolicyMaxSizeMbText);

                await viewModel.ApplyKeePassHistoryPolicyCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();

                // The boxes keep what was applied - the screen does not quietly round the untouched one -
                // and the change is staged, not saved.
                Assert.Equal("3", maxItemsBox.Text);
                Assert.Equal("30", daysBox.Text);
                Assert.Equal("6", sizeBox.Text);
                Assert.True(viewModel.KeePassVaultIsDirty);
                Assert.True(
                    (await File.ReadAllBytesAsync(fixturePath, TestContext.Current.CancellationToken))
                    .AsSpan().SequenceEqual(content));

                // Two floors, refused one at a time so each one names the box it belongs to: a box the
                // file has no spelling for reloads from the database rather than being half-applied, the
                // age is stored unsigned so the -1 the other two boxes accept is not a day count, and the
                // size stops at what the other clients of this file can carry - one over it reloads the
                // box instead of writing a number they would have to make sense of.
                maxItemsBox.Text = "not-a-number";
                Dispatcher.UIThread.RunJobs();
                await viewModel.ApplyKeePassHistoryPolicyCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("3", maxItemsBox.Text);

                daysBox.Text = "-1";
                Dispatcher.UIThread.RunJobs();
                await viewModel.ApplyKeePassHistoryPolicyCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("30", daysBox.Text);
                Assert.Equal("6", sizeBox.Text);

                sizeBox.Text = (KeePassHistorySizeUnits.MaximumMegabytes + 1).ToString();
                Dispatcher.UIThread.RunJobs();
                await viewModel.ApplyKeePassHistoryPolicyCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("6", sizeBox.Text);
                Assert.Equal("3", maxItemsBox.Text);

                // What reaches the file is checked off the disk, through the save the person presses, so
                // the claim is about bytes and not about a box echoing what was typed into it.
                await viewModel.SaveKeePassVaultCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                Assert.False(viewModel.KeePassVaultIsDirty);
                var saved = await ReopenPolicyAsync(fixturePath);
                Assert.Equal(3, saved.MaxItems);
                Assert.Equal(30u, saved.MaintenanceDays);
                Assert.Equal(fromFile.MaxSizeBytes, saved.MaxSizeBytes);

                // Now the size box is edited, and the megabyte it was given becomes that many bytes.
                sizeBox.Text = "7";
                Dispatcher.UIThread.RunJobs();
                await viewModel.ApplyKeePassHistoryPolicyCommand.ExecuteAsync(null);
                await viewModel.SaveKeePassVaultCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("7", sizeBox.Text);
                var resized = await ReopenPolicyAsync(fixturePath);
                Assert.Equal(7 * KeePassHistorySizeUnits.BytesPerMegabyte, resized.MaxSizeBytes);
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
    /// Opens a smoke fixture through the real command path and lands on the KeePass tab of the import
    /// page, where a browsed library is read. The bytes it was opened from come back along with the
    /// view so a test can say what the file still holds.
    /// </summary>
    private static async Task<(Window Host, SyncImportView View, MainWindowViewModel ViewModel, byte[] Content)>
        OpenVaultOnKeePassTabAsync(string fixturePath)
    {
        var info = await Task.Run(() =>
            KeePassSmokeVaultWriter.Write(fixturePath, FixturePassword, entries: 2, groups: 1));
        var content = await File.ReadAllBytesAsync(fixturePath, TestContext.Current.CancellationToken);
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

        var view = new SyncImportView { DataContext = viewModel };
        var host = new Window { Width = 1280, Height = 800, Content = view };
        viewModel.SelectedSyncPage = "Import";
        host.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = view.FindControl<TabControl>("ImportSourceTabs")!;
        tabs.SelectedItem = view.FindControl<TabItem>("KeePassImportTab")!;
        Dispatcher.UIThread.RunJobs();
        GC.KeepAlive(services);
        return (host, view, viewModel, content);
    }

    /// <summary>
    /// Reads the history policy back off the file on disk, through the platform rather than through the
    /// screen, so what the save wrote can be compared with what the box showed.
    /// </summary>
    private static async Task<KeePassHistoryPolicy> ReopenPolicyAsync(string fixturePath)
    {
        var content = await File.ReadAllBytesAsync(fixturePath, TestContext.Current.CancellationToken);
        using var session = await new KeePassVaultService().OpenAsync(
            content,
            Path.GetFileName(fixturePath),
            FixturePassword,
            fixturePath,
            TestContext.Current.CancellationToken);
        return await session.ReadHistoryPolicyAsync(TestContext.Current.CancellationToken);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: a fixture the OS still holds is not worth failing a green run over.
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
