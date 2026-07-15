using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static string BuildLineNumbersText(int lineCount) =>
        string.Join(Environment.NewLine, Enumerable.Range(1, lineCount));

    private static int CountNoteLines(string content)
    {
        return string.IsNullOrEmpty(content)
            ? 1
            : content.Count(character => character == '\n') + 1;
    }

    private static int CountNoteWords(string content)
    {
        var count = 0;
        var inAsciiWord = false;
        foreach (var character in content)
        {
            if (IsCjkCharacter(character))
            {
                count++;
                inAsciiWord = false;
            }
            else if (char.IsLetterOrDigit(character))
            {
                if (!inAsciiWord)
                {
                    count++;
                    inAsciiWord = true;
                }
            }
            else
            {
                inAsciiWord = false;
            }
        }

        return count;
    }

    private static bool IsCjkCharacter(char character) =>
        character is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff';

    private string BuildNotePreviewMarkdown(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "";
        }

        var builder = new StringBuilder(content.Length);
        var inCodeFence = false;
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                AppendPreviewMarkdownLine(builder, line, index < lines.Length - 1);
                continue;
            }

            var previewLine = inCodeFence
                ? line
                : MarkdownLinkRegex().Replace(line, match =>
                {
                    var isImage = match.Groups[1].Value == "!";
                    var label = match.Groups[2].Value.Trim();
                    var target = match.Groups[3].Value.Trim();
                    if (!isImage || !target.StartsWith("monica-image://", StringComparison.OrdinalIgnoreCase))
                    {
                        return match.Value;
                    }

                    return string.IsNullOrWhiteSpace(label)
                        ? $"[{_localization.Get("NoteImageAttachment")}]"
                        : $"[{_localization.Format("NoteImageAttachmentFormat", label)}]";
                });
            AppendPreviewMarkdownLine(builder, previewLine, index < lines.Length - 1);
        }

        return builder.ToString();
    }

    private string BuildNotePlainPreview(string content, bool isMarkdown)
        => NoteContentCodec.ToPlainPreview(content, isMarkdown);

    private static void AppendPreviewMarkdownLine(StringBuilder builder, string line, bool appendLineBreak)
    {
        builder.Append(line);
        if (appendLineBreak)
        {
            builder.Append('\n');
        }
    }

}
