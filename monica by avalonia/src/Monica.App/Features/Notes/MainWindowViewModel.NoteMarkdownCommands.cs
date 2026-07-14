using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void InsertMarkdown(string? action)
    {
        var snippet = action switch
        {
            "h1" => "# Heading",
            "h2" => "## Heading",
            "bold" => "**bold text**",
            "italic" => "_italic text_",
            "quote" => "> Quote",
            "code" => "```\ncode\n```",
            "ul" => "- List item",
            "ol" => "1. List item",
            "todo" => "- [ ] Task",
            "table" => "| Column | Column |\n| --- | --- |\n| Value | Value |",
            "link" => "[link text](https://)",
            "hr" => "---",
            _ => ""
        };

        if (string.IsNullOrWhiteSpace(snippet))
        {
            return;
        }

        AppendNoteContentSnippet(snippet);
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task InsertNoteImageAsync()
    {
        var markdown = await PickNoteImageMarkdownAsync();
        if (!string.IsNullOrWhiteSpace(markdown))
        {
            AppendNoteContentSnippet(markdown);
        }
    }

    public async Task<string?> PickNoteImageMarkdownAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenBinaryFileAsync(_localization.Get("InsertImage"), NoteImageFileTypes);
            if (file is null)
            {
                return null;
            }

            var draft = await _passwordAttachmentFileService.StoreAttachmentAsync(
                file.FileName,
                file.Content,
                InferImageContentType(file.FileName));
            StatusMessage = _localization.Format("InsertedNoteImageFormat", draft.FileName);
            return NoteContentCodec.BuildInlineImageMarkdown(draft.StoragePath);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("InsertNoteImageFailedFormat", ex.Message);
            return null;
        }
    }

}
