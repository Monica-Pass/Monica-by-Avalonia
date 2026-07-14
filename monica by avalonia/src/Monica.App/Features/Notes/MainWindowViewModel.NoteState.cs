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
    private static readonly PlatformFilePickerFileType[] MarkdownFileTypes =
    [
        new("Markdown", ["*.md", "*.markdown"])
    ];
    private static readonly PlatformFilePickerFileType[] NoteImageFileTypes =
    [
        new("Images", ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp"])
    ];

    private void LoadNoteIntoEditor(SecureItem? item)
    {
        if (item is null)
        {
            ResetNoteEditor();
            return;
        }

        var decoded = NoteContentCodec.DecodeFromItem(item);
        NoteTitle = item.Title;
        NoteContent = decoded.Content;
        NoteTagsText = string.Join(", ", decoded.Tags);
        NoteIsMarkdown = decoded.IsMarkdown;
        NoteIsFavorite = item.IsFavorite;
        NotePreviewMode = decoded.IsMarkdown;
        StatusMessage = _localization.Format("EditingNoteFormat", item.Title);
    }

    private void OpenNoteTab(SecureItem item)
    {
        var tab = OpenNoteTabs.FirstOrDefault(openTab => openTab.Source?.Id == item.Id);
        if (tab is null)
        {
            tab = new NoteEditorTab(item.Id, item, item.Title);
            OpenNoteTabs.Add(tab);
            NotifyNoteTabsChanged();
            RefreshNoteTabState();
        }

        SelectedNoteTab = tab;
    }

    private void LoadNoteTab(NoteEditorTab? tab)
    {
        _isLoadingNoteEditor = true;
        try
        {
            if (tab is null)
            {
                SelectedNote = null;
                ResetNoteEditor();
                return;
            }

            EnsureNoteTabDraftInitialized(tab);
            SelectedNote = tab.Source;
            LoadNoteTabDraftIntoEditor(tab);
        }
        finally
        {
            _isLoadingNoteEditor = false;
        }
    }

    private void EnsureNoteTabDraftInitialized(NoteEditorTab tab)
    {
        if (tab.DraftInitialized)
        {
            return;
        }

        if (tab.Source is null)
        {
            tab.DraftTitle = tab.Title;
            tab.DraftContent = "";
            tab.DraftTagsText = "";
            tab.DraftIsMarkdown = true;
            tab.DraftIsFavorite = false;
            tab.DraftPreviewMode = false;
            tab.DraftSplitPreviewMode = false;
            tab.DraftInitialized = true;
            return;
        }

        var decoded = NoteContentCodec.DecodeFromItem(tab.Source);
        tab.DraftTitle = tab.Source.Title;
        tab.DraftContent = decoded.Content;
        tab.DraftTagsText = string.Join(", ", decoded.Tags);
        tab.DraftIsMarkdown = decoded.IsMarkdown;
        tab.DraftIsFavorite = tab.Source.IsFavorite;
        tab.DraftPreviewMode = decoded.IsMarkdown;
        tab.DraftSplitPreviewMode = false;
        tab.Title = string.IsNullOrWhiteSpace(tab.Source.Title) ? _localization.Get("Untitled") : tab.Source.Title.Trim();
        tab.IsDirty = false;
        tab.DraftInitialized = true;
    }

    private void LoadNoteTabDraftIntoEditor(NoteEditorTab tab)
    {
        NoteTitle = tab.DraftTitle;
        NoteContent = tab.DraftContent;
        NoteTagsText = tab.DraftTagsText;
        NoteIsMarkdown = tab.DraftIsMarkdown;
        NoteIsFavorite = tab.DraftIsFavorite;
        NotePreviewMode = tab.DraftPreviewMode;
        NoteSplitPreviewMode = tab.DraftSplitPreviewMode;
        StatusMessage = tab.Source is null
            ? _localization.Get("EditingNewSecureNote")
            : _localization.Format("EditingNoteFormat", tab.Title);
    }

    private void CaptureSelectedNoteTabViewState()
    {
        if (_isLoadingNoteEditor || SelectedNoteTab is null)
        {
            return;
        }

        CaptureNoteEditorState(SelectedNoteTab, markDirty: false);
    }

    private void CaptureNoteEditorState(NoteEditorTab tab, bool markDirty)
    {
        tab.DraftTitle = NoteTitle;
        tab.DraftContent = NoteContent;
        tab.DraftTagsText = NoteTagsText;
        tab.DraftIsMarkdown = NoteIsMarkdown;
        tab.DraftIsFavorite = NoteIsFavorite;
        tab.DraftPreviewMode = NotePreviewMode;
        tab.DraftSplitPreviewMode = NoteSplitPreviewMode;
        tab.DraftInitialized = true;
        tab.DraftSelectionStart = Math.Clamp(tab.DraftSelectionStart, 0, NoteContent.Length);
        tab.DraftSelectionEnd = Math.Clamp(tab.DraftSelectionEnd, 0, NoteContent.Length);
        tab.Title = string.IsNullOrWhiteSpace(NoteTitle) ? _localization.Get("Untitled") : NoteTitle.Trim();
        if (markDirty)
        {
            tab.IsDirty = true;
        }
    }

    private void AppendNoteContentSnippet(string snippet)
    {
        var prefix = string.IsNullOrWhiteSpace(NoteContent)
            ? ""
            : NoteContent.EndsWith('\n') ? "\n" : "\n\n";
        NoteContent += prefix + snippet;
    }

    private void MarkSelectedNoteTabDirty()
    {
        if (_isLoadingNoteEditor || SelectedNoteTab is null)
        {
            return;
        }

        CaptureNoteEditorState(SelectedNoteTab, markDirty: true);
    }

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

    private static string BuildLineNumbersText(string content)
    {
        return string.Join(Environment.NewLine, Enumerable.Range(1, CountNoteLines(content)));
    }

    private static int CountNoteLines(string content) =>
        string.IsNullOrEmpty(content)
            ? 1
            : content.Count(character => character == '\n') + 1;

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

    private static void AppendPreviewMarkdownLine(StringBuilder builder, string line, bool appendLineBreak)
    {
        builder.Append(line);
        if (appendLineBreak)
        {
            builder.Append('\n');
        }
    }

    private static IReadOnlyList<NoteOutlineItem> BuildNoteOutlineItems(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var items = new List<NoteOutlineItem>();
        var inCodeFence = false;
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
            {
                continue;
            }

            var match = HeadingRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }
            var level = match.Groups[1].Value.Length;
            var title = match.Groups[2].Value.Trim();
            if (title.Length == 0)
            {
                continue;
            }

            items.Add(new NoteOutlineItem(
                level,
                title,
                index + 1,
                new Thickness(Math.Min(level - 1, 5) * 12, 0, 0, 0)));
        }

        return items;
    }

    private IReadOnlyList<NoteReferenceItem> BuildNoteReferenceItems(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var items = new List<NoteReferenceItem>();
        var inCodeFence = false;
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
            {
                continue;
            }

            var markdownLinkRanges = new List<(int Start, int End)>();
            foreach (Match match in MarkdownLinkRegex().Matches(line))
            {
                var isImage = match.Groups[1].Value == "!";
                var label = match.Groups[2].Value.Trim();
                var target = match.Groups[3].Value.Trim();
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                markdownLinkRanges.Add((match.Index, match.Index + match.Length));
                items.Add(new NoteReferenceItem(
                    string.IsNullOrWhiteSpace(label) ? (isImage ? _localization.Get("NoteImage") : target) : label,
                    target,
                    index + 1,
                    isImage));
            }

            foreach (Match match in BareUrlRegex().Matches(line))
            {
                var start = match.Index;
                if (markdownLinkRanges.Any(range => start >= range.Start && start < range.End))
                {
                    continue;
                }

                var target = match.Value.TrimEnd('.', ',', ';', ':');
                items.Add(new NoteReferenceItem(target, target, index + 1, IsImageUrl(target)));
            }
        }

        return items
            .DistinctBy(item => (item.Target, item.LineNumber))
            .ToArray();
    }

    private static bool IsImageUrl(string target) =>
        target.StartsWith("monica-image://", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    private static bool TryCreateExternalReferenceUri(string? target, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(target) ||
            !Uri.TryCreate(target.Trim(), UriKind.Absolute, out var candidate) ||
            candidate.Scheme is not ("http" or "https"))
        {
            return false;
        }

        uri = candidate;
        return true;
    }
    [GeneratedRegex("^\\s{0,3}(#{1,6})\\s+(.+?)\\s*#*\\s*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex("(!?)\\[([^\\]]*)\\]\\(([^\\)\\s]+)\\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex("https?://[^\\s<>()]+")]
    private static partial Regex BareUrlRegex();

    private static string InferImageContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private void ResetNoteEditor()
    {
        NoteTitle = "";
        NoteContent = "";
        NoteTagsText = "";
        NoteIsMarkdown = true;
        NotePreviewMode = false;
        NoteSplitPreviewMode = false;
        NoteIsFavorite = false;
    }
}
