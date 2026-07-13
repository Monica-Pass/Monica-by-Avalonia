using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string NoteCountText => _localization.Format("NoteCountFormat", NoteItems.Count);
    public string NotePreviewMarkdown => NoteIsMarkdown ? BuildNotePreviewMarkdown(NoteContent) : "";
    public string NotePlainPreview => NoteContentCodec.ToPlainPreview(NoteContent, NoteIsMarkdown);

    private bool _isLoadingNoteEditor;
    private int _noteImagePreviewVersion;

    public ObservableCollection<SecureItem> NoteItems { get; } = new ObservableRangeCollection<SecureItem>();
    public ObservableCollection<NoteEditorTab> OpenNoteTabs { get; } = [];
    public ObservableCollection<NoteImagePreviewItem> NoteImagePreviewItems { get; } = [];

    [ObservableProperty]
    private SecureItem? _selectedNote;

    [ObservableProperty]
    private NoteEditorTab? _selectedNoteTab;

    [ObservableProperty]
    private string _noteTitle = "";

    [ObservableProperty]
    private string _noteContent = "";

    [ObservableProperty]
    private string _noteTagsText = "";

    [ObservableProperty]
    private string _noteSearchText = "";

    [ObservableProperty]
    private bool _noteIsMarkdown = true;

    [ObservableProperty]
    private bool _notePreviewMode;

    [ObservableProperty]
    private bool _noteSplitPreviewMode;

    [ObservableProperty]
    private bool _noteIsFavorite;

    public string NoteLineNumbersText => BuildLineNumbersText(NoteContent);
    public int NoteLineCount => CountNoteLines(NoteContent);
    public int NoteWordCount => CountNoteWords(NoteContent);
    public int NoteCharacterCount => NoteContent.Length;
    public IReadOnlyList<NoteOutlineItem> NoteOutlineItems => BuildNoteOutlineItems(NoteContent);
    public IReadOnlyList<NoteReferenceItem> NoteReferenceItems => BuildNoteReferenceItems(NoteContent);
    public int NoteOutlineCount => NoteOutlineItems.Count;
    public int NoteReferenceCount => NoteReferenceItems.Count;
    public bool HasNoteOutlineItems => NoteOutlineCount > 0;
    public bool HasNoteReferenceItems => NoteReferenceCount > 0;
    public int NoteImagePreviewCount => NoteImagePreviewItems.Count;
    public bool HasNoteImagePreviewItems => NoteImagePreviewCount > 0;
    public string NoteFormatText => NoteIsMarkdown ? "Markdown" : "Plain text";
    public IReadOnlyList<SecureItem> FavoriteNoteItems => BuildFilteredNoteItems(favoritesOnly: true);
    public IReadOnlyList<SecureItem> FilteredNoteItems => BuildFilteredNoteItems(favoritesOnly: false);
    public IReadOnlyList<NoteTreeGroup> NoteTreeGroups => BuildNoteTreeGroups(FilteredNoteItems);
    public int FavoriteNoteCount => NoteItems.Count(item => item.IsFavorite);
    public bool HasFavoriteNoteItems => FavoriteNoteItems.Count > 0;
    public bool HasFilteredNoteItems => FilteredNoteItems.Count > 0;
    public bool HasNoteTreeGroups => NoteTreeGroups.Count > 0;
    public string NoteTreeStatusText => string.IsNullOrWhiteSpace(NoteSearchText)
        ? NoteCountText
        : $"{FilteredNoteItems.Count}/{NoteItems.Count}";
    public bool IsNoteEditorPaneVisible => !NotePreviewMode || NoteSplitPreviewMode;
    public bool IsNotePreviewPaneVisible => NotePreviewMode || NoteSplitPreviewMode;
    public GridLength NoteEditorColumnWidth => IsNoteEditorPaneVisible
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0);
    public GridLength NotePreviewSeparatorColumnWidth => NoteSplitPreviewMode
        ? new GridLength(18)
        : new GridLength(0);
    public GridLength NotePreviewColumnWidth => IsNotePreviewPaneVisible
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0);
    public Thickness NotePreviewContentPadding => NoteSplitPreviewMode
        ? new Thickness(0)
        : new Thickness(32, 0, 0, 0);
    public bool IsNoteTreePaneVisible => !NoteSplitPreviewMode;
    public GridLength NoteTreeColumnWidth => IsNoteTreePaneVisible
        ? new GridLength(280)
        : new GridLength(0);
    public bool IsNoteInspectorPaneVisible =>
        NoteWorkspaceViewportWidth <= 0 || NoteWorkspaceViewportWidth >= 780;
    public GridLength NoteInspectorColumnWidth => IsNoteInspectorPaneVisible
        ? new GridLength(260)
        : new GridLength(0);
    public string NoteEditorStatusText =>
        NoteSelectedCharacterCount > 0
            ? $"行 {NoteCaretLine}, 列 {NoteCaretColumn} · 已选 {NoteSelectedCharacterCount} · {NoteLineCount} 行 · {NoteWordCount} 词 · {NoteCharacterCount} 字符"
            : $"行 {NoteCaretLine}, 列 {NoteCaretColumn} · {NoteLineCount} 行 · {NoteWordCount} 词 · {NoteCharacterCount} 字符";
    public bool HasOpenNoteTabs => OpenNoteTabs.Count > 0;
    public double NoteTabWidth => CalculateNoteTabWidth(OpenNoteTabs.Count, NoteTabRailViewportWidth);
    public double NoteTabStripWidth
    {
        get
        {
            const double fallbackWidth = 720;
            const double minWidth = 260;
            const double maxWidth = 680;
            var viewportWidth = NoteWorkspaceViewportWidth;
            if (viewportWidth <= 0 || double.IsNaN(viewportWidth))
            {
                return Math.Min(fallbackWidth, maxWidth);
            }

            var treeWidth = IsNoteTreePaneVisible ? 280 : 0;
            return Math.Clamp(viewportWidth - treeWidth, minWidth, maxWidth);
        }
    }

    private double _noteTabRailViewportWidth;
    private double _noteWorkspaceViewportWidth;

    public double NoteTabRailViewportWidth
    {
        get => _noteTabRailViewportWidth;
        set
        {
            if (SetProperty(ref _noteTabRailViewportWidth, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(NoteTabWidth));
            }
        }
    }

    public double NoteWorkspaceViewportWidth
    {
        get => _noteWorkspaceViewportWidth;
        set
        {
            if (SetProperty(ref _noteWorkspaceViewportWidth, Math.Max(0, value)))
            {
                RaiseNoteWorkspaceLayoutState();
            }
        }
    }

    private static double CalculateNoteTabWidth(int tabCount, double viewportWidth)
    {
        const double maxWidth = 148;
        const double minWidth = 76;
        const double tabGap = 4;

        if (tabCount <= 0)
        {
            return maxWidth;
        }

        if (viewportWidth <= 0 || double.IsNaN(viewportWidth))
        {
            return tabCount switch
            {
                <= 1 => maxWidth,
                <= 4 => 136,
                <= 7 => 112,
                <= 10 => 92,
                _ => minWidth
            };
        }

        var widthThatFits = (viewportWidth - ((tabCount - 1) * tabGap) - 8) / tabCount;
        return Math.Clamp(widthThatFits, minWidth, maxWidth);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteEditorStatusText))]
    private int _noteCaretLine = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteEditorStatusText))]
    private int _noteCaretColumn = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteEditorStatusText))]
    private int _noteSelectedCharacterCount;

    partial void OnNoteContentChanged(string value)
    {
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
        _ = RefreshNoteImagePreviewsAsync(value);
    }

    partial void OnNoteTagsTextChanged(string value) => MarkSelectedNoteTabDirty();

    partial void OnNoteIsMarkdownChanged(bool value)
    {
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
        CaptureSelectedNoteTabViewState();
    }

    partial void OnNoteSplitPreviewModeChanged(bool value)
    {
        if (value && NotePreviewMode)
        {
            NotePreviewMode = false;
        }

        RaiseNoteEditorLayoutState();
        CaptureSelectedNoteTabViewState();
    }

    partial void OnNoteTitleChanged(string value) => MarkSelectedNoteTabDirty();

    partial void OnNoteSearchTextChanged(string value) => RaiseNoteTreeState();

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
        RefreshNoteTabState();
    }
}
