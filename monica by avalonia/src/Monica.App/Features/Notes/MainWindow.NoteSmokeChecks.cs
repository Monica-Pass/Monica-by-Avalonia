using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    public async Task<bool> RunSmokeUiNoteEditorChecksAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(RunSmokeUiNoteEditorChecksAsync);
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI note editor failed. reason=no-view-model");
            return false;
        }

        viewModel.SelectSectionCommand.Execute("Notes");
        if (viewModel.SelectedNoteTab is null)
        {
            viewModel.AddNoteCommand.Execute(null);
        }

        viewModel.NoteTitle = "Smoke Markdown editor";
        viewModel.NoteIsMarkdown = true;
        viewModel.NotePreviewMode = false;
        viewModel.NoteSplitPreviewMode = false;
        var content = string.Join("\n",
            "# Smoke Markdown editor",
            "",
            "This line contains NEEDLE and a second needle for replace-all.",
            "",
            "## Links and images",
            "",
            "![inline](monica-image://smoke-image)",
            "[reference](https://example.invalid/smoke)",
            "",
            "- [ ] Task item",
            "1. Ordered item",
            "",
            "```",
            "needle inside code is intentionally replaceable in plain editor search",
            "```");

        viewModel.NoteContent = content;
        NoteContentEditor.Text = content;
        NoteContentEditor.SelectionStart = 0;
        NoteContentEditor.SelectionEnd = 0;
        CaptureNoteEditorHistorySnapshot(force: true);
        UpdateNoteEditorStatus();

        ShowNoteFindPanel(replaceMode: true);
        NoteFindTextBox.Text = "needle";
        NoteReplaceTextBox.Text = "thread";
        ReplaceAllNoteMatches();

        viewModel.NoteSplitPreviewMode = true;
        await viewModel.SaveNoteCommand.ExecuteAsync(null);

        var updatedContent = viewModel.NoteContent;
        var success =
            viewModel.SelectedNote is not null &&
            viewModel.NoteItems.Any(item => item.Id == viewModel.SelectedNote.Id) &&
            updatedContent.Contains("thread", StringComparison.OrdinalIgnoreCase) &&
            !updatedContent.Contains("needle", StringComparison.OrdinalIgnoreCase) &&
            viewModel.HasNoteOutlineItems &&
            viewModel.HasNoteReferenceItems &&
            viewModel.NoteReferenceItems.Any(item => item.IsImage) &&
            viewModel.NoteLineCount >= 12 &&
            viewModel.NoteSplitPreviewMode;

        AppDiagnostics.Info(
            $"Smoke UI note editor checks completed. success={success}, " +
            $"lineCount={viewModel.NoteLineCount}, outline={viewModel.NoteOutlineCount}, " +
            $"references={viewModel.NoteReferenceCount}, selectedNote={viewModel.SelectedNote?.Title}");
        return success;
    }
}
