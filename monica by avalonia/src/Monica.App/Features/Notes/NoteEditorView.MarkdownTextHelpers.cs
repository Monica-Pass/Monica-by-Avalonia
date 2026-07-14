using System.Text.RegularExpressions;
using Avalonia.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public partial class NoteEditorView
{
    private void ReplaceEditorText(int start, int end, string replacement)
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);
        var updated = text[..start] + replacement + text[end..];
        editor.Text = updated;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NoteContent = updated;
        }
    }

    private static (int Start, int End) GetSelectionRange(TextBox editor, int textLength)
    {
        var start = Math.Clamp(Math.Min(editor.SelectionStart, editor.SelectionEnd), 0, textLength);
        var end = Math.Clamp(Math.Max(editor.SelectionStart, editor.SelectionEnd), start, textLength);
        return (start, end);
    }

    private static int FindLineStart(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        var lineBreak = text.LastIndexOf('\n', Math.Max(0, index - 1));
        return lineBreak < 0 ? 0 : lineBreak + 1;
    }

    private static int FindLineEnd(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        var lineBreak = text.IndexOf('\n', index);
        return lineBreak < 0 ? text.Length : lineBreak;
    }

    private static int GetLineStartIndex(string text, int lineNumber)
    {
        if (lineNumber <= 1 || string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var currentLine = 1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            currentLine++;
            if (currentLine == lineNumber)
            {
                return Math.Min(index + 1, text.Length);
            }
        }

        return text.Length;
    }

    private static string StripHeadingPrefix(string line)
    {
        var trimmed = line.TrimStart();
        var offset = line.Length - trimmed.Length;
        var markerLength = 0;
        while (markerLength < trimmed.Length && trimmed[markerLength] == '#')
        {
            markerLength++;
        }

        if (markerLength == 0 || markerLength >= trimmed.Length || trimmed[markerLength] != ' ')
        {
            return line;
        }

        return line[..offset] + trimmed[(markerLength + 1)..];
    }

    private static string GetBlockPrefix(string text, int start)
    {
        if (string.IsNullOrEmpty(text) || start == 0)
        {
            return "";
        }

        return text[Math.Max(0, start - 1)] == '\n' ? "" : "\n\n";
    }

    private static string GetBlockSuffix(string text, int end)
    {
        if (string.IsNullOrEmpty(text) || end >= text.Length)
        {
            return "";
        }

        return text[end] == '\n' ? "" : "\n\n";
    }
}
