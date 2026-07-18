using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    partial void OnNoteContentChanged(string value)
    {
        InvalidateNoteEditorProjections();
        OnPropertyChanged(nameof(NotePreviewMarkdown));
        OnPropertyChanged(nameof(NotePlainPreview));
        OnPropertyChanged(nameof(NoteLineNumbersText));
        OnPropertyChanged(nameof(NoteLineCount));
        OnPropertyChanged(nameof(NoteWordCount));
        OnPropertyChanged(nameof(NoteCharacterCount));
        OnPropertyChanged(nameof(NoteOutlineItems));
        OnPropertyChanged(nameof(NoteReferenceItems));
        OnPropertyChanged(nameof(NoteOutlineCount));
        OnPropertyChanged(nameof(NoteReferenceCount));
        OnPropertyChanged(nameof(HasNoteOutlineItems));
        OnPropertyChanged(nameof(HasNoteReferenceItems));
        OnPropertyChanged(nameof(NoteEditorStatusText));
        MarkSelectedNoteTabDirty();
        QueueNoteImagePreviewRefresh(value);
    }

    partial void OnNoteTagsTextChanged(string value) => MarkSelectedNoteTabDirty();

    partial void OnNoteIsFavoriteChanged(bool value) => MarkSelectedNoteTabDirty();

    partial void OnNoteIsMarkdownChanged(bool value)
    {
        InvalidateNotePreviewProjection();
        OnPropertyChanged(nameof(NotePreviewMarkdown));
        OnPropertyChanged(nameof(NotePlainPreview));
        OnPropertyChanged(nameof(NoteFormatText));
        MarkSelectedNoteTabDirty();
    }

    partial void OnNotePreviewModeChanged(bool value)
    {
        if (value && NoteSplitPreviewMode)
        {
            NoteSplitPreviewMode = false;
        }

        RaiseNoteEditorLayoutState();
        OnPropertyChanged(nameof(NoteViewModeIndex));
        CaptureSelectedNoteTabViewState();
    }

    partial void OnNoteSplitPreviewModeChanged(bool value)
    {
        if (value && NotePreviewMode)
        {
            NotePreviewMode = false;
        }

        RaiseNoteEditorLayoutState();
        OnPropertyChanged(nameof(NoteViewModeIndex));
        CaptureSelectedNoteTabViewState();
    }

    partial void OnNoteTitleChanged(string value) => MarkSelectedNoteTabDirty();

    partial void OnNoteSearchTextChanged(string value) => RaiseNoteTreeState();

    partial void OnNoteNarrowShowsTreeChanged(bool value) => RaiseNoteWorkspaceLayoutState();

    partial void OnSelectedNoteChanged(SecureItem? value)
    {
        if (_isLoadingNoteEditor)
        {
            return;
        }

        if (value is not null)
        {
            OpenNoteTab(value);
            return;
        }

        SelectedNoteTab = null;
        ResetNoteEditor();
    }

    partial void OnSelectedNoteTabChanged(NoteEditorTab? oldValue, NoteEditorTab? newValue)
    {
        if (!_isLoadingNoteEditor && oldValue is not null)
        {
            CaptureNoteEditorState(oldValue, markDirty: false);
        }

        LoadNoteTab(newValue);
        if (IsNoteWorkspaceNarrow)
        {
            NoteNarrowShowsTree = newValue is null;
        }

        RefreshNoteTabState();
    }
}
