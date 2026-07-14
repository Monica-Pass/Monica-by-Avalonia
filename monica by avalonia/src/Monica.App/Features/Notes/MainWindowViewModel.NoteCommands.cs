using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void AddNote()
    {
        var tab = new NoteEditorTab(-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), null, _localization.Get("NewSecureNote"))
        {
            IsDirty = true,
            DraftInitialized = true,
            DraftContent = "",
            DraftTagsText = "",
            DraftIsMarkdown = true,
            DraftIsFavorite = false,
            DraftPreviewMode = false,
            DraftSplitPreviewMode = false
        };
        OpenNoteTabs.Add(tab);
        NotifyNoteTabsChanged();
        SelectedNoteTab = tab;
        NoteNarrowShowsTree = false;
        StatusMessage = _localization.Get("EditingNewSecureNote");
    }

    [RelayCommand]
    private void OpenNote(SecureItem? item)
    {
        if (item is not null)
        {
            OpenNoteTab(item);
            NoteNarrowShowsTree = false;
        }
    }

    [RelayCommand]
    private void SelectNoteTab(NoteEditorTab? tab)
    {
        if (tab is not null)
        {
            SelectedNoteTab = tab;
        }
    }

    [RelayCommand]
    private void CloseNoteTab(NoteEditorTab? tab)
    {
        tab ??= SelectedNoteTab;
        if (tab is null)
        {
            return;
        }

        var index = OpenNoteTabs.IndexOf(tab);
        OpenNoteTabs.Remove(tab);
        NotifyNoteTabsChanged();
        if (ReferenceEquals(SelectedNoteTab, tab))
        {
            SelectedNoteTab = OpenNoteTabs.Count == 0
                ? null
                : OpenNoteTabs[Math.Clamp(index, 0, OpenNoteTabs.Count - 1)];
        }

        RefreshNoteTabState();
    }

    private bool CanSelectPreviousNoteTab() =>
        SelectedNoteTab is not null && OpenNoteTabs.IndexOf(SelectedNoteTab) > 0;

    [RelayCommand(CanExecute = nameof(CanSelectPreviousNoteTab))]
    private void SelectPreviousNoteTab()
    {
        var index = SelectedNoteTab is null ? -1 : OpenNoteTabs.IndexOf(SelectedNoteTab);
        if (index > 0)
        {
            SelectedNoteTab = OpenNoteTabs[index - 1];
        }
    }

    private bool CanSelectNextNoteTab()
    {
        var index = SelectedNoteTab is null ? -1 : OpenNoteTabs.IndexOf(SelectedNoteTab);
        return index >= 0 && index < OpenNoteTabs.Count - 1;
    }

    [RelayCommand(CanExecute = nameof(CanSelectNextNoteTab))]
    private void SelectNextNoteTab()
    {
        var index = SelectedNoteTab is null ? -1 : OpenNoteTabs.IndexOf(SelectedNoteTab);
        if (index >= 0 && index < OpenNoteTabs.Count - 1)
        {
            SelectedNoteTab = OpenNoteTabs[index + 1];
        }
    }

    private void NotifyNoteTabsChanged()
    {
        OnPropertyChanged(nameof(HasOpenNoteTabs));
        OnPropertyChanged(nameof(NoteTabWidth));
    }

    private void RaiseNoteEditorLayoutState()
    {
        OnPropertyChanged(nameof(IsNoteEditorPaneVisible));
        OnPropertyChanged(nameof(IsNotePreviewPaneVisible));
        OnPropertyChanged(nameof(NoteEditorColumnWidth));
        OnPropertyChanged(nameof(NotePreviewSeparatorColumnWidth));
        OnPropertyChanged(nameof(NotePreviewColumnWidth));
        OnPropertyChanged(nameof(NotePreviewContentPadding));
        RaiseNoteWorkspaceLayoutState();
    }

    private void RaiseNoteWorkspaceLayoutState()
    {
        OnPropertyChanged(nameof(IsNoteWorkspaceNarrow));
        OnPropertyChanged(nameof(IsNoteTreePaneVisible));
        OnPropertyChanged(nameof(IsNoteEditorWorkspaceVisible));
        OnPropertyChanged(nameof(ShowBackToNoteList));
        OnPropertyChanged(nameof(NoteTreeColumnWidth));
        OnPropertyChanged(nameof(NoteWorkspaceEditorColumnWidth));
        OnPropertyChanged(nameof(NoteTabStripWidth));
        OnPropertyChanged(nameof(IsNoteInspectorPaneVisible));
        OnPropertyChanged(nameof(NoteInspectorColumnWidth));
    }

    private void RaiseOtherWorkspaceLayoutState()
    {
        OnPropertyChanged(nameof(IsOtherWorkspaceCompact));
        OnPropertyChanged(nameof(TotpAccountColumnWidth));
        OnPropertyChanged(nameof(TotpCodeColumnWidth));
        OnPropertyChanged(nameof(TotpInspectorColumnWidth));
        OnPropertyChanged(nameof(TotpCodeConsolePadding));
        OnPropertyChanged(nameof(TotpCodeFontSize));
        OnPropertyChanged(nameof(GeneratorOptionsColumnWidth));
        OnPropertyChanged(nameof(GeneratorResultPanelPadding));
        OnPropertyChanged(nameof(GeneratorOptionsPanelPadding));
        OnPropertyChanged(nameof(GeneratorOptionsSpacing));
        OnPropertyChanged(nameof(GeneratorCheckboxSpacing));
        OnPropertyChanged(nameof(GeneratorPasswordBoxMinHeight));
        OnPropertyChanged(nameof(GeneratorHistoryPanelMaxHeight));
        OnPropertyChanged(nameof(ShowGeneratorStrengthSummaryCard));
    }

    private void RaiseNoteTreeState()
    {
        OnPropertyChanged(nameof(FavoriteNoteItems));
        OnPropertyChanged(nameof(FilteredNoteItems));
        OnPropertyChanged(nameof(NoteTreeGroups));
        OnPropertyChanged(nameof(FavoriteNoteCount));
        OnPropertyChanged(nameof(HasFavoriteNoteItems));
        OnPropertyChanged(nameof(HasFilteredNoteItems));
        OnPropertyChanged(nameof(HasNoteTreeGroups));
        OnPropertyChanged(nameof(HasNoteSearchText));
        OnPropertyChanged(nameof(ShowAddNoteInEmptyTree));
        OnPropertyChanged(nameof(ShowClearNoteSearchInEmptyTree));
        OnPropertyChanged(nameof(NoteTreeEmptyText));
        OnPropertyChanged(nameof(NoteTreeStatusText));
    }

    [RelayCommand]
    private void ClearNoteSearch() => NoteSearchText = "";

    [RelayCommand]
    private void ShowNoteTree()
    {
        if (IsNoteWorkspaceNarrow)
        {
            NoteNarrowShowsTree = true;
        }
    }

    private void RefreshNoteTabState()
    {
        foreach (var tab in OpenNoteTabs)
        {
            tab.IsSelected = ReferenceEquals(tab, SelectedNoteTab);
        }

        SelectPreviousNoteTabCommand.NotifyCanExecuteChanged();
        SelectNextNoteTabCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<SecureItem> BuildFilteredNoteItems(bool favoritesOnly)
    {
        var query = NoteSearchText ?? "";
        return NoteItems
            .Where(item => (!favoritesOnly || item.IsFavorite) && MatchesNoteSearch(item, query))
            .OrderByDescending(item => item.IsFavorite)
            .ThenByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<NoteTreeGroup> BuildNoteTreeGroups(IReadOnlyList<SecureItem> notes)
    {
        var taggedGroups = new SortedDictionary<string, List<SecureItem>>(StringComparer.OrdinalIgnoreCase);
        var untagged = new List<SecureItem>();

        foreach (var note in notes)
        {
            var tags = GetNoteTreeTags(note);
            if (tags.Count == 0)
            {
                untagged.Add(note);
                continue;
            }

            foreach (var tag in tags)
            {
                if (!taggedGroups.TryGetValue(tag, out var bucket))
                {
                    bucket = [];
                    taggedGroups[tag] = bucket;
                }

                bucket.Add(note);
            }
        }

        var groups = taggedGroups
            .Select(pair => new NoteTreeGroup(pair.Key, pair.Value.Count, pair.Value, IsUntagged: false))
            .ToList();

        if (untagged.Count > 0)
        {
            groups.Add(new NoteTreeGroup(_localization.Get("NoteUntagged"), untagged.Count, untagged, IsUntagged: true));
        }

        return groups;
    }

    private static IReadOnlyList<string> GetNoteTreeTags(SecureItem item)
    {
        try
        {
            return NoteContentCodec.DecodeFromItem(item).Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool MatchesNoteSearch(SecureItem item, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var terms = query
            .Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return true;
        }

        var decodedContent = "";
        var decodedTags = "";
        try
        {
            var decoded = NoteContentCodec.DecodeFromItem(item);
            decodedContent = decoded.Content;
            decodedTags = string.Join(" ", decoded.Tags);
        }
        catch
        {
            // Keep the file tree usable even when a legacy note payload cannot be decoded.
        }

        return terms.All(term =>
            ContainsOrdinalIgnoreCase(item.Title, term) ||
            ContainsOrdinalIgnoreCase(item.Notes, term) ||
            ContainsOrdinalIgnoreCase(decodedContent, term) ||
            ContainsOrdinalIgnoreCase(decodedTags, term));
    }

    private static bool ContainsOrdinalIgnoreCase(string? source, string value) =>
        !string.IsNullOrEmpty(source) &&
        source.Contains(value, StringComparison.OrdinalIgnoreCase);

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

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (SelectedNoteTab is not null)
        {
            CaptureNoteEditorState(SelectedNoteTab, markDirty: SelectedNoteTab.IsDirty);
            if (!CanSaveNoteTab(SelectedNoteTab))
            {
                StatusMessage = _localization.Get("NoteRequiresContent");
                return;
            }

            var savedNote = await SaveNoteTabAsync(SelectedNoteTab);
            SelectedNote = savedNote;
            RaiseCounts();
            StatusMessage = _localization.Format("SavedNoteFormat", savedNote.Title);
            return;
        }

        if (string.IsNullOrWhiteSpace(NoteTitle) && string.IsNullOrWhiteSpace(NoteContent))
        {
            StatusMessage = _localization.Get("NoteRequiresContent");
            return;
        }

        var sourceNote = SelectedNote;
        var payload = NoteContentCodec.BuildSavePayload(
            NoteTitle,
            NoteContent,
            NoteTagsText,
            NoteIsMarkdown,
            sourceNote is null ? [] : NoteContentCodec.DecodeImagePaths(sourceNote.ImagePaths));

        var item = sourceNote ?? new SecureItem
        {
            ItemType = VaultItemType.Note,
            CreatedAt = DateTimeOffset.UtcNow
        };

        item.Title = payload.Title;
        item.Notes = payload.NotesCache;
        item.ItemData = payload.ItemData;
        item.ImagePaths = payload.ImagePaths;
        item.IsFavorite = NoteIsFavorite;
        item.ItemType = VaultItemType.Note;
        item.SyncStatus = item.BitwardenVaultId is null ? SyncStatus.None : SyncStatus.Pending;

        await _repository.SaveSecureItemAsync(item);
        await LogOperationAsync(new OperationLog
        {
            ItemType = "NOTE",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = sourceNote is null ? "CREATE" : "UPDATE",
            DeviceName = Environment.MachineName
        });

        if (NoteItems.All(note => note.Id != item.Id))
        {
            NoteItems.Insert(0, item);
        }

        if (SelectedNoteTab is not null)
        {
            SelectedNoteTab.Source = item;
            SelectedNoteTab.Title = item.Title;
            CaptureNoteEditorState(SelectedNoteTab, markDirty: false);
            SelectedNoteTab.IsDirty = false;
        }

        SelectedNote = item;
        RaiseCounts();
        StatusMessage = _localization.Format("SavedNoteFormat", item.Title);
    }

    [RelayCommand]
    private async Task SaveAllNoteTabsAsync()
    {
        if (SelectedNoteTab is not null)
        {
            CaptureNoteEditorState(SelectedNoteTab, markDirty: SelectedNoteTab.IsDirty);
        }

        var dirtyTabs = OpenNoteTabs.Where(tab => tab.IsDirty).ToArray();
        if (dirtyTabs.Length == 0)
        {
            StatusMessage = _localization.Get("NoNotesToSave");
            return;
        }

        var savedCount = 0;
        var skippedCount = 0;
        foreach (var tab in dirtyTabs)
        {
            if (!CanSaveNoteTab(tab))
            {
                skippedCount++;
                continue;
            }

            await SaveNoteTabAsync(tab);
            savedCount++;
        }

        if (SelectedNoteTab?.Source is not null)
        {
            SelectedNote = SelectedNoteTab.Source;
        }

        if (savedCount > 0)
        {
            RaiseCounts();
        }

        StatusMessage = skippedCount == 0
            ? _localization.Format("SavedNotesFormat", savedCount)
            : _localization.Format("SavedNotesWithSkippedFormat", savedCount, skippedCount);
    }

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

    private async Task<SecureItem> SaveNoteTabAsync(NoteEditorTab tab)
    {
        var sourceNote = tab.Source;
        var payload = NoteContentCodec.BuildSavePayload(
            tab.DraftTitle,
            tab.DraftContent,
            tab.DraftTagsText,
            tab.DraftIsMarkdown,
            sourceNote is null ? [] : NoteContentCodec.DecodeImagePaths(sourceNote.ImagePaths));

        var item = sourceNote ?? new SecureItem
        {
            ItemType = VaultItemType.Note,
            CreatedAt = DateTimeOffset.UtcNow
        };

        item.Title = payload.Title;
        item.Notes = payload.NotesCache;
        item.ItemData = payload.ItemData;
        item.ImagePaths = payload.ImagePaths;
        item.IsFavorite = tab.DraftIsFavorite;
        item.ItemType = VaultItemType.Note;
        item.SyncStatus = item.BitwardenVaultId is null ? SyncStatus.None : SyncStatus.Pending;

        await _repository.SaveSecureItemAsync(item);
        await LogOperationAsync(new OperationLog
        {
            ItemType = "NOTE",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = sourceNote is null ? "CREATE" : "UPDATE",
            DeviceName = Environment.MachineName
        });

        if (NoteItems.All(note => note.Id != item.Id))
        {
            NoteItems.Insert(0, item);
        }

        tab.Source = item;
        tab.Title = item.Title;
        tab.DraftTitle = item.Title;
        tab.DraftIsFavorite = item.IsFavorite;
        tab.IsDirty = false;
        return item;
    }

    [RelayCommand]
    private async Task ToggleNoteFavoriteAsync()
    {
        NoteIsFavorite = !NoteIsFavorite;
        if (SelectedNote is null)
        {
            MarkSelectedNoteTabDirty();
            return;
        }

        SelectedNote.IsFavorite = NoteIsFavorite;
        await _repository.SaveSecureItemAsync(SelectedNote);
        if (SelectedNoteTab is not null)
        {
            CaptureNoteEditorState(SelectedNoteTab, markDirty: false);
        }

        RaiseNoteTreeState();
        StatusMessage = _localization.Format("SavedNoteFormat", SelectedNote.Title);
    }

    [RelayCommand]
    private async Task DeleteNoteAsync(SecureItem? item)
    {
        item ??= SelectedNote;
        if (item is null)
        {
            return;
        }

        if (!await ConfirmMoveItemToRecycleBinAsync(item.Title))
        {
            return;
        }

        await _repository.SoftDeleteSecureItemAsync(item.Id);
        NoteItems.Remove(item);
        var deletedTabs = OpenNoteTabs
            .Where(tab => tab.Source?.Id == item.Id)
            .ToArray();
        foreach (var tab in deletedTabs)
        {
            CloseNoteTab(tab);
        }

        if (deletedTabs.Length == 0 && SelectedNote?.Id == item.Id)
        {
            SelectedNote = null;
        }

        RaiseCounts();
        StatusMessage = _localization.Format("MovedToRecycleBinFormat", item.Title);
    }
}
