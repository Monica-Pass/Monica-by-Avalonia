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

}
