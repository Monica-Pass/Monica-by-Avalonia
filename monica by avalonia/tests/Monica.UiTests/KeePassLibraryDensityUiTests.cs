using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.Sync;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.UiTests;

/// <summary>
/// The import page is where a person reaches a .kdbx, and at the shipped frame size the library
/// had been left a handful of rows tall. These readings are in rows a person can count, taken from
/// the real frame, because "the rail is up" is not the same claim as "the rail is usable".
/// </summary>
[Collection(AvaloniaUiTestCollection.Name)]
public sealed class KeePassLibraryDensityUiTests
{
    private const string FixturePassword = KeePassSmokeVaultWriter.DefaultPassword;

    /// <summary>
    /// Eight rows is the shortest list that reads as a library rather than as a preview: below it
    /// the person scrolling to find an entry is the design, not an accident of the frame.
    /// </summary>
    private const int MinimumRowsInView = 8;

    [Fact]
    public async Task Opened_vault_leaves_the_library_in_rows_a_person_can_count()
    {
        var fixturePath = Path.Combine(
            Path.GetTempPath(),
            "monica-uitests",
            $"keepass-density-{Guid.NewGuid():N}.kdbx");
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
        try
        {
            var info = await Task.Run(() =>
                KeePassSmokeVaultWriter.Write(fixturePath, FixturePassword, entries: 12, groups: 3));
            var content = await File.ReadAllBytesAsync(fixturePath, TestContext.Current.CancellationToken);
            var picker = new DensityFileService(
                new PickedBinaryFile(info.FileName, content, fixturePath));

            var window = new Monica.App.MainWindow { Width = 1280, Height = 800 };
            using var services = Monica.App.App.ConfigureServices(window, collection =>
            {
                collection.AddSingleton<IFileSystemPickerService>(picker);
            });
            var viewModel = services.GetRequiredService<MainWindowViewModel>();
            window.Show();
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.L.SetLanguage("zh-CN");
            Drain();

            viewModel.SelectSectionCommand.Execute("Sync");
            viewModel.SelectedSyncPage = "Import";
            viewModel.KeePassImportTabSelected = true;
            Drain();

            await viewModel.SelectKeePassFileCommand.ExecuteAsync(null);
            viewModel.KeePassImportPassword = FixturePassword;
            await viewModel.PreviewKeePassImportCommand.ExecuteAsync(null);
            Assert.True(viewModel.HasKeePassImportPreview);
            Drain();

            var view = window.GetVisualDescendants().OfType<SyncImportView>().First();
            var tree = view.InPane<VaultFolderTree>("KeePassBrowseTree")!;
            var treeScroller = tree.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            var rowHeight = tree.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .FirstOrDefault(item => item.Bounds.Height > 0)?.Bounds.Height ?? 0;
            var rowsInView = treeScroller is null || rowHeight <= 0
                ? 0
                : (int)Math.Floor(treeScroller.Viewport.Height / rowHeight);

            Assert.True(
                rowsInView >= MinimumRowsInView,
                $"the library shows {rowsInView} rows of {rowHeight:0} at 1280x800, below the " +
                $"{MinimumRowsInView} this page has to leave standing.");

            // The rail's bottom edge has to be inside the window: a pane tall enough to run past the
            // frame is scrolled past its own last row, which is the same failure in the other direction.
            var pane = view.GetVisualDescendants().OfType<KeePassBrowsePane>().First();
            var paneBottom = pane.TranslatePoint(new Point(0, pane.Bounds.Height), window)?.Y
                ?? double.PositiveInfinity;
            Assert.True(
                paneBottom <= window.Bounds.Height + 0.5,
                $"the browse pane ends at {paneBottom:0}px in a {window.Bounds.Height:0}px frame.");

            // The room the library stands in was given back by the strip that retired, so assert the
            // strip is actually down on this page - and still up where its cards name a control a
            // person can reach from the same screen.
            var health = window.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .First(control => control.Name == "SyncHealthStatusRegion");
            Assert.False(health.IsVisible);
            viewModel.SelectedSyncPage = "Configuration";
            Drain();
            Assert.True(health.IsVisible);

            window.Close();
            Drain();
        }
        finally
        {
            if (File.Exists(fixturePath))
            {
                File.Delete(fixturePath);
            }
        }
    }

    private static void Drain()
    {
        for (var i = 0; i < 60; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class DensityFileService(PickedBinaryFile file) : IFileSystemPickerService
    {
        public PlatformIntegrationCapability Capability { get; } = PlatformIntegrationService.Available(
            PlatformFeatureKeys.FilePicker,
            "Density test file picker");

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
