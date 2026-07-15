using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.ViewModels;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class NoteEditorPerformanceTests
{
    public NoteEditorPerformanceTests()
    {
        TestAppBuilder.EnsureInitialized();
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
}
