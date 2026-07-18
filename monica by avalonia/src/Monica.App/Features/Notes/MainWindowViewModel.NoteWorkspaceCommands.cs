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
        OnPropertyChanged(nameof(ShowAddNoteInTreeHeader));
        OnPropertyChanged(nameof(NoteTreeColumnWidth));
        OnPropertyChanged(nameof(NoteWorkspaceEditorColumnWidth));
        OnPropertyChanged(nameof(NoteEditorContentMargin));
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
        _noteTreeProjectionDirty = true;
        OnPropertyChanged(nameof(FavoriteNoteItems));
        OnPropertyChanged(nameof(FilteredNoteItems));
        OnPropertyChanged(nameof(NoteTreeGroups));
        OnPropertyChanged(nameof(NoteTreeEntries));
        OnPropertyChanged(nameof(FavoriteNoteCount));
        OnPropertyChanged(nameof(HasFavoriteNoteItems));
        OnPropertyChanged(nameof(HasFilteredNoteItems));
        OnPropertyChanged(nameof(HasNoteTreeGroups));
        OnPropertyChanged(nameof(HasNoteTreeEntries));
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

}
