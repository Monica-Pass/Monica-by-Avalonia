using System.Security.Cryptography;
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

    [RelayCommand(CanExecute = nameof(CanInsertNoteImage))]
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
        if (!CanInsertNoteImage)
        {
            return null;
        }

        byte[]? pickedContent = null;
        byte[]? storedContent = null;
        var stagedStoragePath = "";
        IsInsertingNoteImage = true;
        try
        {
            var file = await _fileSystemPickerService.OpenBinaryFileAsync(_localization.Get("InsertImage"), NoteImageFileTypes);
            if (file is null)
            {
                return null;
            }

            pickedContent = file.Content;
            if (_vaultSessionService.IsExplicitlyLocked)
            {
                StatusMessage = _localization.Get("VaultLocked");
                return null;
            }

            var draft = await _passwordAttachmentFileService.StoreAttachmentAsync(
                file.FileName,
                file.Content,
                InferImageContentType(file.FileName));
            storedContent = draft.Content;
            stagedStoragePath = draft.StoragePath;
            if (_vaultSessionService.IsExplicitlyLocked)
            {
                await DeleteStagedNoteImageAsync(stagedStoragePath);
                stagedStoragePath = "";
                StatusMessage = _localization.Get("VaultLocked");
                return null;
            }

            var markdown = NoteContentCodec.BuildInlineImageMarkdown(draft.StoragePath);
            stagedStoragePath = "";
            StatusMessage = _localization.Format("InsertedNoteImageFormat", draft.FileName);
            return markdown;
        }
        catch (AttachmentTooLargeException ex)
        {
            StatusMessage = _localization.Format(
                "AttachmentTooLargeFormat",
                FormatByteSize(ex.ActualBytes),
                FormatByteSize(ex.MaximumBytes));
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            await DeleteStagedNoteImageAsync(stagedStoragePath);
            stagedStoragePath = "";
            AppDiagnostics.Error("Secure note image insertion failed", ex);
            StatusMessage = _localization.Get("InsertNoteImageFailed");
            return null;
        }
        finally
        {
            ZeroNoteImageBuffer(pickedContent);
            if (!ReferenceEquals(pickedContent, storedContent))
            {
                ZeroNoteImageBuffer(storedContent);
            }

            IsInsertingNoteImage = false;
        }
    }

    private async Task DeleteStagedNoteImageAsync(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return;
        }

        try
        {
            await _passwordAttachmentFileService.DeleteStoredAttachmentAsync(storagePath);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Staged secure note image cleanup failed", ex);
        }
    }

    private static void ZeroNoteImageBuffer(byte[]? content)
    {
        if (content is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

}
