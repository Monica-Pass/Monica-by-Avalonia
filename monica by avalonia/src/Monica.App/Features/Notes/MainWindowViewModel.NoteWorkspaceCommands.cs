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
        OnPropertyChanged(nameof(TotpCodeConsolePadding));
        OnPropertyChanged(nameof(TotpCodeFontSize));
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

}
