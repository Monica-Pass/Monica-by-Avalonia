using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public partial class NoteEditorView
{
    private void NoteFindTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateNoteFindStatus();
        if (NoteFindPanel.IsVisible && !string.IsNullOrEmpty(NoteFindTextBox.Text))
        {
            SelectFirstNoteFindMatch(focusEditor: false);
        }
    }

    private void NoteFindTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideNoteFindPanel();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            SelectNoteFindMatch(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift), focusEditor: true);
            e.Handled = true;
        }
    }

    private void NoteReplaceTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideNoteFindPanel();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ReplaceAllNoteMatches();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            ReplaceCurrentNoteMatch();
            e.Handled = true;
        }
    }

    private void FindPreviousNoteMatchButton_OnClick(object? sender, RoutedEventArgs e) =>
        SelectNoteFindMatch(forward: false, focusEditor: true);

    private void FindNextNoteMatchButton_OnClick(object? sender, RoutedEventArgs e) =>
        SelectNoteFindMatch(forward: true, focusEditor: true);

    private void ReplaceCurrentNoteMatchButton_OnClick(object? sender, RoutedEventArgs e) =>
        ReplaceCurrentNoteMatch();

    private void ReplaceAllNoteMatchesButton_OnClick(object? sender, RoutedEventArgs e) =>
        ReplaceAllNoteMatches();

    private void NoteFindOptions_OnChanged(object? sender, RoutedEventArgs e)
    {
        UpdateNoteFindStatus();
        SelectFirstNoteFindMatch(focusEditor: false);
    }

    private void CloseNoteFindPanelButton_OnClick(object? sender, RoutedEventArgs e) =>
        HideNoteFindPanel();

    private void ShowNoteFindPanel(bool replaceMode)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NotePreviewMode = false;
        }

        NoteFindPanel.IsVisible = true;
        NoteReplaceTextBox.IsVisible = replaceMode || NoteReplaceTextBox.IsVisible;
        var selectedText = GetSelectedNoteText();
        if (!string.IsNullOrWhiteSpace(selectedText) && !selectedText.Contains('\n'))
        {
            NoteFindTextBox.Text = selectedText;
        }

        UpdateNoteFindStatus();
        var focusTarget = replaceMode ? NoteReplaceTextBox : NoteFindTextBox;
        focusTarget.Focus();
        focusTarget.SelectAll();
        Dispatcher.UIThread.Post(() =>
        {
            focusTarget.Focus();
            focusTarget.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void HideNoteFindPanel()
    {
        NoteFindPanel.IsVisible = false;
        NoteContentEditor.Focus();
    }

}
