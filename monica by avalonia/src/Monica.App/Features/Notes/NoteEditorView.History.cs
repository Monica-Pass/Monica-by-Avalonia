using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public partial class NoteEditorView
{
    private const int MaxNoteEditorHistoryEntries = 100;
    private readonly Dictionary<NoteEditorTab, NoteEditorHistoryState> _noteEditorHistories = [];
    private readonly NoteEditorHistoryState _fallbackNoteEditorHistory = new();
    private bool _isRestoringNoteEditorHistory;

    private sealed record NoteEditorSnapshot(string Text, int SelectionStart, int SelectionEnd);

    private sealed class NoteEditorHistoryState
    {
        public List<NoteEditorSnapshot> Entries { get; } = [];
        public int Index { get; set; } = -1;
    }

    private void NoteContentEditor_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_isRestoringNoteEditorHistory)
        {
            CaptureNoteEditorHistorySnapshot();
        }

        UpdateNoteEditorStatus();
        UpdateNoteFindStatus();
    }

    private void NoteContentEditor_OnKeyUp(object? sender, KeyEventArgs e)
    {
        UpdateNoteEditorStatus();
        UpdateNoteFindStatus();
    }

    private void NoteContentEditor_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        UpdateNoteEditorStatus();
        UpdateNoteFindStatus();
    }

    private void EnsureSelectedNoteEditorHistory()
    {
        var state = GetCurrentNoteEditorHistory();
        if (state.Entries.Count == 0)
        {
            CaptureNoteEditorHistorySnapshot(force: true);
        }
        else
        {
            CaptureNoteEditorHistorySnapshot();
        }
    }

    private void CaptureNoteEditorHistorySnapshot(bool force = false)
    {
        var history = GetCurrentNoteEditorHistory();
        var text = NoteContentEditor.Text ?? "";
        var snapshot = new NoteEditorSnapshot(
            text,
            Math.Clamp(NoteContentEditor.SelectionStart, 0, text.Length),
            Math.Clamp(NoteContentEditor.SelectionEnd, 0, text.Length));

        if (history.Index >= 0 && history.Index < history.Entries.Count)
        {
            var current = history.Entries[history.Index];
            if (!force && string.Equals(current.Text, snapshot.Text, StringComparison.Ordinal))
            {
                history.Entries[history.Index] = snapshot;
                return;
            }
        }

        if (history.Index < history.Entries.Count - 1)
        {
            history.Entries.RemoveRange(history.Index + 1, history.Entries.Count - history.Index - 1);
        }

        history.Entries.Add(snapshot);
        if (history.Entries.Count > MaxNoteEditorHistoryEntries)
        {
            history.Entries.RemoveAt(0);
        }

        history.Index = history.Entries.Count - 1;
    }

    private void UndoNoteEditor()
    {
        var history = GetCurrentNoteEditorHistory();
        if (history.Index <= 0)
        {
            return;
        }

        history.Index--;
        RestoreNoteEditorSnapshot(history.Entries[history.Index]);
    }

    private void RedoNoteEditor()
    {
        var history = GetCurrentNoteEditorHistory();
        if (history.Index < 0 || history.Index >= history.Entries.Count - 1)
        {
            return;
        }

        history.Index++;
        RestoreNoteEditorSnapshot(history.Entries[history.Index]);
    }

    private NoteEditorHistoryState GetCurrentNoteEditorHistory()
    {
        return DataContext is MainWindowViewModel { SelectedNoteTab: { } tab }
            ? GetNoteEditorHistory(tab)
            : _fallbackNoteEditorHistory;
    }

    private NoteEditorHistoryState GetNoteEditorHistory(NoteEditorTab tab)
    {
        if (!_noteEditorHistories.TryGetValue(tab, out var history))
        {
            history = new NoteEditorHistoryState();
            _noteEditorHistories[tab] = history;
        }

        return history;
    }

    private void RestoreNoteEditorSnapshot(NoteEditorSnapshot snapshot)
    {
        _isRestoringNoteEditorHistory = true;
        try
        {
            NoteContentEditor.Text = snapshot.Text;
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.NoteContent = snapshot.Text;
            }

            var length = snapshot.Text.Length;
            NoteContentEditor.SelectionStart = Math.Clamp(snapshot.SelectionStart, 0, length);
            NoteContentEditor.SelectionEnd = Math.Clamp(snapshot.SelectionEnd, 0, length);
            NoteContentEditor.Focus();
        }
        finally
        {
            _isRestoringNoteEditorHistory = false;
        }

        UpdateNoteEditorStatus();
    }
}
