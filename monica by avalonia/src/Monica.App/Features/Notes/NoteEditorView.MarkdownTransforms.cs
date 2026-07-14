using System.Text.RegularExpressions;
using Avalonia.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public partial class NoteEditorView
{
    private static readonly Regex OrderedListLineRegex = new(@"^(\s*)(\d+)([.)])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex TaskListLineRegex = new(@"^(\s*)([-*+])\s+\[(?: |x|X)\]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex UnorderedListLineRegex = new(@"^(\s*)([-*+])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex QuoteLineRegex = new(@"^(\s*(?:>\s*)+)(.*)$", RegexOptions.Compiled);

    private void WrapSelection(string prefix, string suffix, string placeholder)
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (start, end) = GetSelectionRange(editor, text.Length);
        var selected = text[start..end];
        var inner = string.IsNullOrEmpty(selected) ? placeholder : selected;
        ReplaceEditorText(start, end, prefix + inner + suffix);
        editor.SelectionStart = start + prefix.Length;
        editor.SelectionEnd = start + prefix.Length + inner.Length;
        editor.Focus();
    }

    private void WrapBlock(string prefix, string suffix, string placeholder)
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (start, end) = GetSelectionRange(editor, text.Length);
        var selected = text[start..end];
        var inner = string.IsNullOrEmpty(selected) ? placeholder : selected.Trim('\r', '\n');
        var replacement = prefix + inner + suffix;
        ReplaceEditorText(start, end, replacement);
        editor.SelectionStart = start + prefix.Length;
        editor.SelectionEnd = start + prefix.Length + inner.Length;
        editor.Focus();
    }

    private void WrapLink()
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (start, end) = GetSelectionRange(editor, text.Length);
        var selected = text[start..end];
        var label = string.IsNullOrWhiteSpace(selected) ? "link text" : selected;
        var replacement = $"[{label}](https://)";
        ReplaceEditorText(start, end, replacement);
        var urlStart = start + label.Length + 3;
        editor.SelectionStart = urlStart;
        editor.SelectionEnd = urlStart + "https://".Length;
        editor.Focus();
    }

    private void InsertMarkdownBlock(string markdown)
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (start, end) = GetSelectionRange(editor, text.Length);
        var prefix = GetBlockPrefix(text, start);
        var suffix = GetBlockSuffix(text, end);
        var replacement = prefix + markdown + suffix;
        ReplaceEditorText(start, end, replacement);
        var caret = start + replacement.Length - suffix.Length;
        editor.SelectionStart = caret;
        editor.SelectionEnd = caret;
        editor.Focus();
    }

    private bool TryHandleMarkdownEnterContinuation()
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (start, end) = GetSelectionRange(editor, text.Length);
        if (start != end)
        {
            return false;
        }

        var lineStart = FindLineStart(text, start);
        var lineEnd = FindLineEnd(text, start);
        var currentLine = text[lineStart..lineEnd];
        if (!TryGetContinuationMarker(currentLine, out var markerLength, out var nextMarker, out var exitText))
        {
            return false;
        }

        if (start < lineStart + markerLength)
        {
            return false;
        }

        var currentBody = currentLine[markerLength..].Trim();
        if (currentBody.Length == 0)
        {
            ReplaceEditorText(lineStart, lineEnd, exitText);
            var caret = lineStart + exitText.Length;
            editor.SelectionStart = caret;
            editor.SelectionEnd = caret;
            editor.Focus();
            return true;
        }

        var replacement = "\n" + nextMarker;
        ReplaceEditorText(start, start, replacement);
        var nextCaret = start + replacement.Length;
        editor.SelectionStart = nextCaret;
        editor.SelectionEnd = nextCaret;
        editor.Focus();
        return true;
    }

    private static bool TryGetContinuationMarker(
        string line,
        out int markerLength,
        out string nextMarker,
        out string exitText)
    {
        markerLength = 0;
        nextMarker = "";
        exitText = "";

        var taskMatch = TaskListLineRegex.Match(line);
        if (taskMatch.Success)
        {
            var indent = taskMatch.Groups[1].Value;
            var bullet = taskMatch.Groups[2].Value;
            markerLength = indent.Length + bullet.Length + " [ ] ".Length;
            nextMarker = $"{indent}{bullet} [ ] ";
            exitText = indent;
            return true;
        }

        var orderedMatch = OrderedListLineRegex.Match(line);
        if (orderedMatch.Success)
        {
            var indent = orderedMatch.Groups[1].Value;
            var numberText = orderedMatch.Groups[2].Value;
            var delimiter = orderedMatch.Groups[3].Value;
            markerLength = indent.Length + numberText.Length + delimiter.Length + 1;
            var nextNumber = int.TryParse(numberText, out var number) ? number + 1 : 1;
            nextMarker = $"{indent}{nextNumber}{delimiter} ";
            exitText = indent;
            return true;
        }

        var unorderedMatch = UnorderedListLineRegex.Match(line);
        if (unorderedMatch.Success)
        {
            var indent = unorderedMatch.Groups[1].Value;
            var bullet = unorderedMatch.Groups[2].Value;
            markerLength = indent.Length + bullet.Length + 1;
            nextMarker = $"{indent}{bullet} ";
            exitText = indent;
            return true;
        }

        var quoteMatch = QuoteLineRegex.Match(line);
        if (quoteMatch.Success)
        {
            var prefix = quoteMatch.Groups[1].Value;
            markerLength = prefix.Length;
            nextMarker = prefix;
            exitText = "";
            return true;
        }

        return false;
    }

    private void PrefixSelectedLines(string prefix, bool stripMarkdownPrefixes = false) =>
        PrefixSelectedLines((_, _) => prefix, stripMarkdownPrefixes);

    private void IndentSelectedLines(bool outdent)
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (selectionStart, selectionEnd) = GetSelectionRange(editor, text.Length);
        var lineStart = FindLineStart(text, selectionStart);
        var adjustedEnd = selectionEnd > selectionStart ? Math.Max(selectionStart, selectionEnd - 1) : selectionEnd;
        var lineEnd = FindLineEnd(text, adjustedEnd);
        var selectedBlock = text[lineStart..lineEnd];
        var lines = selectedBlock.Split('\n');
        var transformed = lines
            .Select(line => outdent ? OutdentLine(line) : "    " + line)
            .ToArray();
        var replacement = string.Join("\n", transformed);
        ReplaceEditorText(lineStart, lineEnd, replacement);
        editor.SelectionStart = lineStart;
        editor.SelectionEnd = lineStart + replacement.Length;
        editor.Focus();
    }

    private void PrefixSelectedLines(Func<int, string, string> prefixFactory, bool stripMarkdownPrefixes = false)
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (selectionStart, selectionEnd) = GetSelectionRange(editor, text.Length);
        var lineStart = FindLineStart(text, selectionStart);
        var lineEnd = FindLineEnd(text, selectionEnd);
        var selectedBlock = text[lineStart..lineEnd];
        var lines = selectedBlock.Split('\n');
        var transformed = lines
            .Select((line, index) =>
            {
                var normalized = line.TrimEnd('\r');
                var body = stripMarkdownPrefixes ? StripHeadingPrefix(normalized) : normalized;
                return prefixFactory(index, body) + body;
            })
            .ToArray();
        var replacement = string.Join("\n", transformed);
        ReplaceEditorText(lineStart, lineEnd, replacement);
        editor.SelectionStart = lineStart;
        editor.SelectionEnd = lineStart + replacement.Length;
        editor.Focus();
    }

    private static string OutdentLine(string line)
    {
        if (line.StartsWith('\t'))
        {
            return line[1..];
        }

        var removeCount = 0;
        while (removeCount < 4 && removeCount < line.Length && line[removeCount] == ' ')
        {
            removeCount++;
        }

        return removeCount == 0 ? line : line[removeCount..];
    }

}
