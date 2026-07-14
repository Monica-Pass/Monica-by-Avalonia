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
    public void UpdateNoteEditorStatus(int caretIndex, int selectionStart, int selectionEnd)
    {
        var text = NoteContent ?? "";
        caretIndex = Math.Clamp(caretIndex, 0, text.Length);
        selectionStart = Math.Clamp(selectionStart, 0, text.Length);
        selectionEnd = Math.Clamp(selectionEnd, 0, text.Length);
        var line = 1;
        var column = 1;
        for (var index = 0; index < caretIndex; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        NoteCaretLine = line;
        NoteCaretColumn = column;
        NoteSelectedCharacterCount = Math.Abs(selectionEnd - selectionStart);
        if (!_isLoadingNoteEditor && SelectedNoteTab is not null)
        {
            SelectedNoteTab.DraftSelectionStart = selectionStart;
            SelectedNoteTab.DraftSelectionEnd = selectionEnd;
        }
    }

    private async Task RefreshNoteImagePreviewsAsync(string content)
    {
        var version = Interlocked.Increment(ref _noteImagePreviewVersion);
        var imagePaths = NoteContentCodec.ExtractInlineImageIds(content)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (imagePaths.Length == 0)
        {
            if (version == _noteImagePreviewVersion)
            {
                ReplaceNoteImagePreviews([]);
            }

            return;
        }

        var previews = new List<NoteImagePreviewItem>();
        foreach (var imagePath in imagePaths)
        {
            try
            {
                var attachment = CreateNoteImageAttachment(imagePath);
                var contentBytes = await _repository.TryReadAttachmentContentAsync(attachment);
                if (contentBytes is null || contentBytes.Length == 0)
                {
                    continue;
                }

                using var stream = new MemoryStream(contentBytes);
                previews.Add(new NoteImagePreviewItem(
                    imagePath,
                    BuildNoteImagePreviewName(imagePath, previews.Count + 1),
                    FormatByteSize(contentBytes.LongLength),
                    new Bitmap(stream)));
            }
            catch (Exception ex)
            {
                AppDiagnostics.Error($"Note image preview failed for {imagePath}", ex);
            }
        }

        if (version == _noteImagePreviewVersion)
        {
            ReplaceNoteImagePreviews(previews);
        }
        else
        {
            foreach (var preview in previews)
            {
                preview.Image.Dispose();
            }
        }
    }

    private Attachment CreateNoteImageAttachment(string imagePath)
    {
        var ownerId = SelectedNoteTab?.Source?.Id ?? SelectedNote?.Id ?? 0;
        return new Attachment
        {
            OwnerType = "SECURE_ITEM",
            OwnerId = ownerId,
            FileName = BuildNoteImagePreviewName(imagePath, 0),
            ContentType = InferImageContentType(imagePath),
            StoragePath = imagePath,
            SizeBytes = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private void ReplaceNoteImagePreviews(IReadOnlyList<NoteImagePreviewItem> previews)
    {
        foreach (var preview in NoteImagePreviewItems)
        {
            preview.Image.Dispose();
        }

        ReplaceItems(NoteImagePreviewItems, previews);
        OnPropertyChanged(nameof(NoteImagePreviewCount));
        OnPropertyChanged(nameof(HasNoteImagePreviewItems));
    }

    private string BuildNoteImagePreviewName(string imagePath, int fallbackIndex)
    {
        var normalized = imagePath.Trim();
        if (normalized.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            return fallbackIndex > 0
                ? $"MDBX {_localization.Format("NoteImageNumberFormat", fallbackIndex)}"
                : $"MDBX {_localization.Get("NoteImage")}";
        }

        var fileName = Path.GetFileName(normalized.Replace('\\', '/'));
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        return fallbackIndex > 0
            ? _localization.Format("NoteImageNumberFormat", fallbackIndex)
            : _localization.Get("NoteImage");
    }

}
