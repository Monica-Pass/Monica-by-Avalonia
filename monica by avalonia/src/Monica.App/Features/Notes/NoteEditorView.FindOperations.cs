using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public partial class NoteEditorView
{
    private string GetSelectedNoteText()
    {
        var text = NoteContentEditor.Text ?? "";
        var (start, end) = GetSelectionRange(NoteContentEditor, text.Length);
        return start == end ? "" : text[start..end];
    }

    private void SelectFirstNoteFindMatch(bool focusEditor)
    {
        var matches = GetNoteFindMatches();
        if (matches.Count == 0)
        {
            UpdateNoteFindStatus(matches);
            return;
        }

        SelectNoteFindMatch(matches[0], focusEditor);
    }

    private void SelectNoteFindMatch(bool forward, bool focusEditor)
    {
        var matches = GetNoteFindMatches();
        if (matches.Count == 0)
        {
            UpdateNoteFindStatus(matches);
            return;
        }

        var queryLength = GetNoteFindQuery().Length;
        var selectedIndex = GetSelectedNoteFindMatchIndex(matches, queryLength);
        int targetIndex;
        if (selectedIndex >= 0)
        {
            targetIndex = forward
                ? (selectedIndex + 1) % matches.Count
                : (selectedIndex - 1 + matches.Count) % matches.Count;
        }
        else
        {
            var caret = Math.Clamp(NoteContentEditor.SelectionEnd, 0, (NoteContentEditor.Text ?? "").Length);
            targetIndex = forward
                ? matches.FindIndex(index => index >= caret)
                : matches.FindLastIndex(index => index < caret);

            if (targetIndex < 0)
            {
                targetIndex = forward ? 0 : matches.Count - 1;
            }
        }

        SelectNoteFindMatch(matches[targetIndex], focusEditor);
    }

    private void SelectNoteFindMatch(int index, bool focusEditor)
    {
        var query = GetNoteFindQuery();
        if (query.Length == 0)
        {
            UpdateNoteFindStatus();
            return;
        }

        var textLength = (NoteContentEditor.Text ?? "").Length;
        var start = Math.Clamp(index, 0, textLength);
        var end = Math.Clamp(start + query.Length, start, textLength);
        NoteContentEditor.SelectionStart = start;
        NoteContentEditor.SelectionEnd = end;
        if (focusEditor)
        {
            NoteContentEditor.Focus();
        }

        UpdateNoteFindStatus();
        UpdateNoteEditorStatus();
    }

    private void ReplaceCurrentNoteMatch()
    {
        var query = GetNoteFindQuery();
        if (query.Length == 0)
        {
            return;
        }

        var text = NoteContentEditor.Text ?? "";
        var (start, end) = GetSelectionRange(NoteContentEditor, text.Length);
        if (end - start != query.Length ||
            !string.Equals(text[start..end], query, GetNoteFindComparison()))
        {
            SelectNoteFindMatch(forward: true, focusEditor: true);
            return;
        }

        var replacement = NoteReplaceTextBox.Text ?? "";
        ReplaceEditorText(start, end, replacement);
        var nextCaret = start + replacement.Length;
        NoteContentEditor.SelectionStart = nextCaret;
        NoteContentEditor.SelectionEnd = nextCaret;
        CaptureNoteEditorHistorySnapshot();
        SelectNoteFindMatch(forward: true, focusEditor: true);
    }

    private void ReplaceAllNoteMatches()
    {
        var query = GetNoteFindQuery();
        if (query.Length == 0)
        {
            return;
        }

        var text = NoteContentEditor.Text ?? "";
        var replacement = NoteReplaceTextBox.Text ?? "";
        var comparison = GetNoteFindComparison();
        var builder = new StringBuilder(text.Length);
        var index = 0;
        var count = 0;
        while (index < text.Length)
        {
            var matchIndex = text.IndexOf(query, index, comparison);
            if (matchIndex < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            builder.Append(text, index, matchIndex - index);
            builder.Append(replacement);
            index = matchIndex + query.Length;
            count++;
        }

        if (count == 0)
        {
            UpdateNoteFindStatus();
            return;
        }

        ReplaceEditorText(0, text.Length, builder.ToString());
        NoteContentEditor.SelectionStart = 0;
        NoteContentEditor.SelectionEnd = 0;
        CaptureNoteEditorHistorySnapshot();
        UpdateNoteFindStatus();
        NoteFindStatusText.Text = FormatLocalizedText("NoteReplacedMatchesFormat", count);
        NoteContentEditor.Focus();
    }

    private List<int> GetNoteFindMatches()
    {
        var text = NoteContentEditor.Text ?? "";
        var query = GetNoteFindQuery();
        var matches = new List<int>();
        if (query.Length == 0 || text.Length == 0)
        {
            return matches;
        }

        var comparison = GetNoteFindComparison();
        var index = 0;
        while (index <= text.Length - query.Length)
        {
            var matchIndex = text.IndexOf(query, index, comparison);
            if (matchIndex < 0)
            {
                break;
            }

            matches.Add(matchIndex);
            index = matchIndex + query.Length;
        }

        return matches;
    }

    private string GetNoteFindQuery() =>
        NoteFindTextBox.Text ?? "";

    private StringComparison GetNoteFindComparison() =>
        NoteFindMatchCaseCheckBox.IsChecked == true
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

    private void UpdateNoteFindStatus() =>
        UpdateNoteFindStatus(GetNoteFindMatches());

    private void UpdateNoteFindStatus(IReadOnlyList<int> matches)
    {
        if (!NoteFindPanel.IsVisible)
        {
            return;
        }

        var queryLength = GetNoteFindQuery().Length;
        if (queryLength == 0)
        {
            NoteFindStatusText.Text = "0/0";
            return;
        }

        if (matches.Count == 0)
        {
            NoteFindStatusText.Text = GetLocalizedText("NoteNoMatches");
            return;
        }

        var selectedIndex = GetSelectedNoteFindMatchIndex(matches, queryLength);
        NoteFindStatusText.Text = selectedIndex >= 0
            ? $"{selectedIndex + 1}/{matches.Count}"
            : $"0/{matches.Count}";
    }

    private int GetSelectedNoteFindMatchIndex(IReadOnlyList<int> matches, int queryLength)
    {
        if (queryLength == 0)
        {
            return -1;
        }

        var text = NoteContentEditor.Text ?? "";
        var (start, end) = GetSelectionRange(NoteContentEditor, text.Length);
        if (end - start != queryLength)
        {
            return -1;
        }

        for (var index = 0; index < matches.Count; index++)
        {
            if (matches[index] == start)
            {
                return index;
            }
        }

        return -1;
    }
}
