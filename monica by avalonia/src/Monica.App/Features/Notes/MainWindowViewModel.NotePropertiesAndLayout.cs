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
    private IReadOnlyList<SecureItem> _filteredNoteItems = [];
    private IReadOnlyList<SecureItem> _favoriteNoteItems = [];
    private IReadOnlyList<NoteTreeGroup> _noteTreeGroups = [];
    private int _favoriteNoteCount;
    private bool _noteTreeProjectionDirty = true;

    internal int FilteredNoteProjectionBuildCount { get; private set; }
    internal int NoteTreeGroupProjectionBuildCount { get; private set; }
    internal int NotePayloadDecodeCount { get; private set; }

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

    [ObservableProperty]
    private bool _noteNarrowShowsTree = true;

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
    public string NoteFormatText => NoteIsMarkdown ? "Markdown" : _localization.PlainText;
    public IReadOnlyList<SecureItem> FavoriteNoteItems
    {
        get
        {
            EnsureNoteTreeProjection();
            return _favoriteNoteItems;
        }
    }

    public IReadOnlyList<SecureItem> FilteredNoteItems
    {
        get
        {
            EnsureNoteTreeProjection();
            return _filteredNoteItems;
        }
    }

    public IReadOnlyList<NoteTreeGroup> NoteTreeGroups
    {
        get
        {
            EnsureNoteTreeProjection();
            return _noteTreeGroups;
        }
    }

    public int FavoriteNoteCount
    {
        get
        {
            EnsureNoteTreeProjection();
            return _favoriteNoteCount;
        }
    }
    public bool HasFavoriteNoteItems => FavoriteNoteItems.Count > 0;
    public bool HasFilteredNoteItems => FilteredNoteItems.Count > 0;
    public bool HasNoteTreeGroups => NoteTreeGroups.Count > 0;
    public bool HasNoteSearchText => !string.IsNullOrWhiteSpace(NoteSearchText);
    public bool ShowAddNoteInEmptyTree => NoteItems.Count == 0;
    public bool ShowClearNoteSearchInEmptyTree => NoteItems.Count > 0 && HasNoteSearchText && !HasNoteTreeGroups;
    public string NoteTreeEmptyText => ShowClearNoteSearchInEmptyTree
        ? _localization.Get("NoteNoMatchingItems")
        : _localization.Get("NoteEmptyDescription");
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
    public bool IsNoteWorkspaceNarrow =>
        NoteWorkspaceViewportWidth > 0 &&
        NoteWorkspaceViewportWidth < 760;
    public bool IsNoteTreePaneVisible =>
        !NoteSplitPreviewMode &&
        (!IsNoteWorkspaceNarrow || NoteNarrowShowsTree);
    public bool IsNoteEditorWorkspaceVisible =>
        !IsNoteWorkspaceNarrow || !NoteNarrowShowsTree;
    public bool ShowBackToNoteList =>
        IsNoteWorkspaceNarrow &&
        !NoteNarrowShowsTree;
    public GridLength NoteTreeColumnWidth => IsNoteTreePaneVisible
        ? IsNoteWorkspaceNarrow
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(280)
        : new GridLength(0);
    public GridLength NoteWorkspaceEditorColumnWidth => IsNoteEditorWorkspaceVisible
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0);
    public bool IsNoteInspectorPaneVisible =>
        NoteWorkspaceViewportWidth <= 0 || NoteWorkspaceViewportWidth >= 780;
    public GridLength NoteInspectorColumnWidth => IsNoteInspectorPaneVisible
        ? new GridLength(260)
        : new GridLength(0);
    public string NoteEditorStatusText =>
        NoteSelectedCharacterCount > 0
            ? _localization.Format(
                "NoteEditorSelectionStatusFormat",
                NoteCaretLine,
                NoteCaretColumn,
                NoteSelectedCharacterCount,
                NoteLineCount,
                NoteWordCount,
                NoteCharacterCount)
            : _localization.Format(
                "NoteEditorStatusFormat",
                NoteCaretLine,
                NoteCaretColumn,
                NoteLineCount,
                NoteWordCount,
                NoteCharacterCount);
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
                if (IsNoteWorkspaceNarrow)
                {
                    NoteNarrowShowsTree = SelectedNoteTab is null;
                }

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

}
