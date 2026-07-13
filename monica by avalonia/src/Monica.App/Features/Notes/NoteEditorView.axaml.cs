using Avalonia.Controls;
using Avalonia.Input;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public sealed class NoteEditorCloseRequestedEventArgs(NoteEditorTab tab) : EventArgs
{
    public NoteEditorTab Tab { get; } = tab;
}

public partial class NoteEditorView : UserControl
{
    public NoteEditorView()
    {
        InitializeComponent();
    }

    public event EventHandler<NoteEditorCloseRequestedEventArgs>? CloseRequested;

    public bool TryHandleWorkspaceShortcut(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && NoteFindPanel.IsVisible)
        {
            HideNoteFindPanel();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.F3)
        {
            SelectNoteFindMatch(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift), focusEditor: true);
            e.Handled = true;
            return true;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return false;
        }

        if (e.Key == Key.F)
        {
            ShowNoteFindPanel(replaceMode: false);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.H)
        {
            ShowNoteFindPanel(replaceMode: true);
            e.Handled = true;
            return true;
        }

        return false;
    }

    public void RestoreSelectedTabSelection()
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedNoteTab is null)
        {
            return;
        }

        var textLength = (NoteContentEditor.Text ?? "").Length;
        NoteContentEditor.SelectionStart = Math.Clamp(viewModel.SelectedNoteTab.DraftSelectionStart, 0, textLength);
        NoteContentEditor.SelectionEnd = Math.Clamp(viewModel.SelectedNoteTab.DraftSelectionEnd, 0, textLength);
        if (viewModel.IsNoteEditorPaneVisible)
        {
            NoteContentEditor.Focus();
        }

        UpdateNoteEditorStatus();
    }

    public void EnsureSelectedHistory() => EnsureSelectedNoteEditorHistory();

    public void JumpToLine(int lineNumber) => JumpToNoteLine(lineNumber);

    public void RemoveHistory(NoteEditorTab tab) => _noteEditorHistories.Remove(tab);

    public void SetMode(string mode)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.NotePreviewMode = mode == "preview";
        viewModel.NoteSplitPreviewMode = mode == "split";
    }

    public async Task<bool> RunSmokeChecksAsync(MainWindowViewModel viewModel)
    {
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
            "# Smoke Markdown editor", "",
            "This line contains NEEDLE and a second needle for replace-all.", "",
            "## Links and images", "",
            "![inline](monica-image://smoke-image)",
            "[reference](https://example.invalid/smoke)", "",
            "- [ ] Task item", "1. Ordered item", "",
            "```", "needle inside code is intentionally replaceable in plain editor search", "```");

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
        return viewModel.SelectedNote is not null &&
               viewModel.NoteItems.Any(item => item.Id == viewModel.SelectedNote.Id) &&
               updatedContent.Contains("thread", StringComparison.OrdinalIgnoreCase) &&
               !updatedContent.Contains("needle", StringComparison.OrdinalIgnoreCase) &&
               viewModel.HasNoteOutlineItems &&
               viewModel.HasNoteReferenceItems &&
               viewModel.NoteReferenceItems.Any(item => item.IsImage) &&
               viewModel.NoteLineCount >= 12 &&
               viewModel.NoteSplitPreviewMode;
    }

    public async Task RunKeyboardSmokeChecksAsync(Action<string, bool, string> check)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            check("note-editor-view-model", false, "missing view model");
            return;
        }

        viewModel.NotePreviewMode = false;
        viewModel.NoteSplitPreviewMode = false;
        viewModel.NoteContent = "alpha\nbeta";
        NoteContentEditor.Text = viewModel.NoteContent;
        NoteContentEditor.SelectionStart = 0;
        NoteContentEditor.SelectionEnd = NoteContentEditor.Text?.Length ?? 0;
        NoteContentEditor.Focus();
        check("note-editor-focus", NoteContentEditor.IsFocused, $"section={viewModel.SelectedSection}");

        IndentSelectedLines(outdent: false);
        check("note-tab-indents-lines", (NoteContentEditor.Text ?? "").StartsWith("    alpha", StringComparison.Ordinal), $"content='{NoteContentEditor.Text}'");
        IndentSelectedLines(outdent: true);
        check("note-shift-tab-outdents-lines", string.Equals(NoteContentEditor.Text, "alpha\nbeta", StringComparison.Ordinal), $"content='{NoteContentEditor.Text}'");

        ShowNoteFindPanel(replaceMode: false);
        await Task.Delay(50);
        check("note-ctrl-f-focus-find", NoteFindPanel.IsVisible && NoteFindTextBox.IsFocused, $"findVisible={NoteFindPanel.IsVisible}");
        HideNoteFindPanel();
        check("note-escape-closes-find", !NoteFindPanel.IsVisible && NoteContentEditor.IsFocused, $"findVisible={NoteFindPanel.IsVisible}");
        ShowNoteFindPanel(replaceMode: true);
        await Task.Delay(50);
        check("note-ctrl-h-focus-replace", NoteFindPanel.IsVisible && NoteReplaceTextBox.IsVisible && NoteReplaceTextBox.IsFocused, $"replaceVisible={NoteReplaceTextBox.IsVisible}");
        HideNoteFindPanel();
    }

    private void RequestClose(NoteEditorTab tab) =>
        CloseRequested?.Invoke(this, new NoteEditorCloseRequestedEventArgs(tab));
}
