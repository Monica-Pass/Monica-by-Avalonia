using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task ImportMarkdownNoteAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync(_localization.Get("ImportMarkdown"), MarkdownFileTypes);
            if (file is null)
            {
                return;
            }

            var title = Path.GetFileNameWithoutExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = _localization.Get("Untitled");
            }

            var tab = new NoteEditorTab(-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), null, title)
            {
                IsDirty = true,
                DraftInitialized = true,
                DraftTitle = title,
                DraftContent = file.Content,
                DraftTagsText = "",
                DraftIsMarkdown = true,
                DraftIsFavorite = false,
                DraftPreviewMode = false,
                DraftSplitPreviewMode = false
            };

            OpenNoteTabs.Add(tab);
            NotifyNoteTabsChanged();
            SelectedNoteTab = tab;
            StatusMessage = _localization.Format("ImportedMarkdownDraftFormat", file.FileName);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ImportMarkdownFailedFormat", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task ExportCurrentNoteMarkdownAsync()
    {
        if (!await AuthorizeSensitiveExportAsync(grantFileExport: false))
        {
            return;
        }

        if (SelectedNoteTab is not null)
        {
            CaptureNoteEditorState(SelectedNoteTab, markDirty: SelectedNoteTab.IsDirty);
        }

        var title = string.IsNullOrWhiteSpace(NoteTitle)
            ? _localization.Get("Untitled")
            : NoteTitle.Trim();
        var suggestedFileName = $"{BuildSafeFileName(title)}.md";
        var content = NoteIsMarkdown
            ? NoteContent
            : NoteContentCodec.ToPlainPreview(NoteContent, NoteIsMarkdown);
        await SaveExportTextAsync(_localization.Get("ExportMarkdown"), suggestedFileName, content, MarkdownFileTypes);
    }

    private static bool CanSaveNoteTab(NoteEditorTab tab) =>
        !string.IsNullOrWhiteSpace(tab.DraftTitle) ||
        !string.IsNullOrWhiteSpace(tab.DraftContent);

    private static string BuildSafeFileName(string title)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(title.Length);
        foreach (var character in title.Trim())
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        var fileName = builder.ToString().Trim(' ', '.');
        return string.IsNullOrWhiteSpace(fileName) ? "untitled" : fileName;
    }

}
