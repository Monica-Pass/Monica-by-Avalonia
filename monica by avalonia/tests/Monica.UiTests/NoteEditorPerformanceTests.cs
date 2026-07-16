using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.ViewModels;
using Monica.Data.Repositories;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class NoteEditorPerformanceTests
{
    public NoteEditorPerformanceTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Note_editor_content_projection_builds_once_per_text_change()
    {
        const int lineCount = 5000;
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var content = string.Join(
            '\n',
            Enumerable.Range(1, lineCount)
                .Select(index => $"# Section {index} [reference](https://example.com/{index}) recovery word"));

        viewModel.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(MainWindowViewModel.NotePreviewMarkdown):
                    _ = viewModel.NotePreviewMarkdown.Length;
                    break;
                case nameof(MainWindowViewModel.NotePlainPreview):
                    _ = viewModel.NotePlainPreview.Length;
                    break;
                case nameof(MainWindowViewModel.NoteLineNumbersText):
                    _ = viewModel.NoteLineNumbersText.Length;
                    break;
                case nameof(MainWindowViewModel.NoteLineCount):
                    _ = viewModel.NoteLineCount;
                    break;
                case nameof(MainWindowViewModel.NoteWordCount):
                    _ = viewModel.NoteWordCount;
                    break;
                case nameof(MainWindowViewModel.NoteCharacterCount):
                    _ = viewModel.NoteCharacterCount;
                    break;
                case nameof(MainWindowViewModel.NoteOutlineItems):
                    _ = viewModel.NoteOutlineItems.Count;
                    break;
                case nameof(MainWindowViewModel.NoteReferenceItems):
                    _ = viewModel.NoteReferenceItems.Count;
                    break;
                case nameof(MainWindowViewModel.NoteOutlineCount):
                    _ = viewModel.NoteOutlineCount;
                    break;
                case nameof(MainWindowViewModel.NoteReferenceCount):
                    _ = viewModel.NoteReferenceCount;
                    break;
                case nameof(MainWindowViewModel.HasNoteOutlineItems):
                    _ = viewModel.HasNoteOutlineItems;
                    break;
                case nameof(MainWindowViewModel.HasNoteReferenceItems):
                    _ = viewModel.HasNoteReferenceItems;
                    break;
                case nameof(MainWindowViewModel.NoteEditorStatusText):
                    _ = viewModel.NoteEditorStatusText;
                    break;
            }
        };

        var stopwatch = Stopwatch.StartNew();
        viewModel.NoteContent = content;
        stopwatch.Stop();

        Assert.True(
            viewModel.NoteContentAnalysisBuildCount == 1 &&
            viewModel.NotePreviewProjectionBuildCount == 1,
            $"Expected one content-analysis build and one preview build, but observed " +
            $"analysis={viewModel.NoteContentAnalysisBuildCount} and " +
            $"preview={viewModel.NotePreviewProjectionBuildCount}.");
        Assert.True(
            stopwatch.ElapsedMilliseconds < 250,
            $"Large note projection took {stopwatch.ElapsedMilliseconds} ms.");
        var outlineItems = viewModel.NoteOutlineItems;
        var referenceItems = viewModel.NoteReferenceItems;
        Assert.Equal(lineCount, viewModel.NoteLineCount);
        Assert.Equal(lineCount, outlineItems.Count);
        Assert.Equal(lineCount, referenceItems.Count);
        Assert.Same(outlineItems, viewModel.NoteOutlineItems);
        Assert.Same(referenceItems, viewModel.NoteReferenceItems);

        viewModel.NoteIsMarkdown = false;

        Assert.Equal(1, viewModel.NoteContentAnalysisBuildCount);
        Assert.Equal(2, viewModel.NotePreviewProjectionBuildCount);
        Assert.Same(outlineItems, viewModel.NoteOutlineItems);
        Assert.Same(referenceItems, viewModel.NoteReferenceItems);
    }

    [Fact]
    public void Note_image_preview_refresh_debounces_rapid_content_changes()
    {
        var repository = DispatchProxy.Create<IMonicaRepository, AttachmentReadRepositoryProxy>();
        var probe = (AttachmentReadRepositoryProxy)(object)repository;
        probe.BlockReads = true;
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, overrides =>
            overrides.AddSingleton(repository));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        for (var index = 0; index < 10; index++)
        {
            viewModel.NoteContent = $"Edit {index}\n\n![](monica-image://preview.png)";
        }

        Assert.Equal(0, probe.ReadCount);
        Assert.True(
            probe.FirstReadStarted.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken),
            "The final image preview read did not start.");
        Thread.Sleep(350);
        Assert.Equal(1, probe.ReadCount);
        viewModel.NoteContent = "";
        Assert.True(
            probe.ReadCancellationObserved.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken),
            "The final image preview read did not observe cancellation.");
    }

    [Fact]
    public async Task Note_image_preview_refresh_is_cancelled_when_vault_locks()
    {
        var repository = DispatchProxy.Create<IMonicaRepository, AttachmentReadRepositoryProxy>();
        var probe = (AttachmentReadRepositoryProxy)(object)repository;
        probe.BlockReads = true;
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, overrides =>
            overrides.AddSingleton(repository));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        viewModel.IsUnlocked = true;
        viewModel.NoteContent = "Private\n\n![](monica-image://private.png)";
        Assert.True(
            probe.FirstReadStarted.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken),
            "The blocking image preview read did not start.");

        await viewModel.LockCommand.ExecuteAsync(null);

        Assert.True(
            probe.ReadCancellationObserved.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken),
            "The active image preview read did not observe cancellation.");
        Assert.Empty(viewModel.NoteImagePreviewItems);
    }

    public class AttachmentReadRepositoryProxy : DispatchProxy
    {
        private int _readCount;

        public bool BlockReads { get; set; }
        public int ReadCount => Volatile.Read(ref _readCount);
        public ManualResetEventSlim FirstReadStarted { get; } = new();
        public ManualResetEventSlim ReadCancellationObserved { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name != nameof(IMonicaRepository.TryReadAttachmentContentAsync))
            {
                throw new NotSupportedException($"Unexpected repository call: {targetMethod.Name}");
            }

            Interlocked.Increment(ref _readCount);
            FirstReadStarted.Set();
            if (!BlockReads)
            {
                return Task.FromResult<byte[]?>(null);
            }

            var cancellationToken = args is { Length: > 1 } && args[1] is CancellationToken token
                ? token
                : CancellationToken.None;
            return WaitForCancellationAsync(cancellationToken);
        }

        private async Task<byte[]?> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return null;
            }
            catch (OperationCanceledException)
            {
                ReadCancellationObserved.Set();
                throw;
            }
        }
    }
}
