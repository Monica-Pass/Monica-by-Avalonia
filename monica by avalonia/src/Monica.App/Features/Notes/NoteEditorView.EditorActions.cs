using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Monica.App.Features.Notes;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public partial class NoteEditorView
{
    private void ApplyMarkdownAction(string action)
    {
        switch (action)
        {
            case "h1":
                PrefixSelectedLines("# ", stripMarkdownPrefixes: true);
                break;
            case "h2":
                PrefixSelectedLines("## ", stripMarkdownPrefixes: true);
                break;
            case "h3":
                PrefixSelectedLines("### ", stripMarkdownPrefixes: true);
                break;
            case "bold":
                WrapSelection("**", "**", "bold text");
                break;
            case "italic":
                WrapSelection("_", "_", "italic text");
                break;
            case "strike":
                WrapSelection("~~", "~~", "strikethrough");
                break;
            case "inlinecode":
                WrapSelection("`", "`", "code");
                break;
            case "quote":
                PrefixSelectedLines("> ");
                break;
            case "code":
                WrapBlock("```\n", "\n```", "code");
                break;
            case "ul":
                PrefixSelectedLines("- ");
                break;
            case "ol":
                PrefixSelectedLines((index, _) => $"{index + 1}. ");
                break;
            case "todo":
                PrefixSelectedLines("- [ ] ");
                break;
            case "table":
                InsertMarkdownBlock("| Column | Column |\n| --- | --- |\n| Value | Value |");
                break;
            case "link":
                WrapLink();
                break;
            case "hr":
                InsertMarkdownBlock("---");
                break;
        }

        UpdateNoteEditorStatus();
    }

    private void UpdateNoteEditorStatus()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.UpdateNoteEditorStatus(
                NoteContentEditor.SelectionEnd,
                NoteContentEditor.SelectionStart,
                NoteContentEditor.SelectionEnd);
        }
    }

    private void JumpToNoteLine(int lineNumber)
    {
        if (lineNumber <= 0)
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NotePreviewMode = false;
        }

        var text = NoteContentEditor.Text ?? "";
        var index = GetLineStartIndex(text, lineNumber);
        NoteContentEditor.SelectionStart = index;
        NoteContentEditor.SelectionEnd = index;
        NoteContentEditor.Focus();
        UpdateNoteEditorStatus();
    }
}
