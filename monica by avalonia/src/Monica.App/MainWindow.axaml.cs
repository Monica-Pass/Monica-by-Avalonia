using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Monica.App.ViewModels;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace Monica.App;

public partial class MainWindow : Window
{
    private const int MaxNoteEditorHistoryEntries = 100;
    private static readonly Regex OrderedListLineRegex = new(@"^(\s*)(\d+)([.)])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex TaskListLineRegex = new(@"^(\s*)([-*+])\s+\[(?: |x|X)\]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex UnorderedListLineRegex = new(@"^(\s*)([-*+])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex QuoteLineRegex = new(@"^(\s*(?:>\s*)+)(.*)$", RegexOptions.Compiled);
    private readonly Dictionary<NoteEditorTab, NoteEditorHistoryState> _noteEditorHistories = [];
    private readonly NoteEditorHistoryState _fallbackNoteEditorHistory = new();
    private bool _isRestoringNoteEditorHistory;
    private bool _isClosingAfterUnsavedNoteDecision;
    private bool _isHandlingUnsavedWindowClose;
    private MainWindowViewModel? _observedViewModel;

    private sealed record NoteEditorSnapshot(string Text, int SelectionStart, int SelectionEnd);

    private sealed class NoteEditorHistoryState
    {
        public List<NoteEditorSnapshot> Entries { get; } = [];
        public int Index { get; set; } = -1;
    }

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += MainWindow_OnPropertyChanged;
        UpdateMaximizeRestoreButton();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitializeCommand.ExecuteAsync(null);
        }
    }

    private void NavigationView_OnSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var tag = (e.SelectedItem as Control)?.Tag?.ToString()
            ?? (e.SelectedItemContainer as Control)?.Tag?.ToString();
        viewModel.SelectSectionCommand.Execute(tag);
    }

    private void MainWindow_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            UpdateMaximizeRestoreButton();
        }
    }

    private void TitleBarDragArea_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void TitleBarDragArea_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximizeRestore();
        e.Handled = true;
    }

    private void MinimizeWindowButton_OnClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeRestoreWindowButton_OnClick(object? sender, RoutedEventArgs e) =>
        ToggleMaximizeRestore();

    private void CloseWindowButton_OnClick(object? sender, RoutedEventArgs e) =>
        Close();

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void UpdateMaximizeRestoreButton()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeRestoreWindowIcon.Text = isMaximized ? "\uE923" : "\uE922";
        ToolTip.SetTip(MaximizeRestoreWindowButton, isMaximized ? "Restore" : "Maximize");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        PropertyChanged -= MainWindow_OnPropertyChanged;

        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            _observedViewModel = null;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isClosingAfterUnsavedNoteDecision ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dirtyCount = viewModel.OpenNoteTabs.Count(tab => tab.IsDirty);
        if (dirtyCount == 0)
        {
            return;
        }

        e.Cancel = true;
        if (_isHandlingUnsavedWindowClose)
        {
            return;
        }

        _isHandlingUnsavedWindowClose = true;
        try
        {
            var result = await ShowUnsavedNoteTabsDialogAsync(dirtyCount);
            if (result == FAContentDialogResult.Primary)
            {
                await viewModel.SaveAllNoteTabsCommand.ExecuteAsync(null);
                if (viewModel.OpenNoteTabs.Any(tab => tab.IsDirty))
                {
                    return;
                }

                _isClosingAfterUnsavedNoteDecision = true;
                Close();
            }
            else if (result == FAContentDialogResult.Secondary)
            {
                _isClosingAfterUnsavedNoteDecision = true;
                Close();
            }
        }
        finally
        {
            _isHandlingUnsavedWindowClose = false;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        }

        _observedViewModel = DataContext as MainWindowViewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        }
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedNoteTab))
        {
            Dispatcher.UIThread.Post(() =>
            {
                RestoreSelectedNoteTabSelection();
                EnsureSelectedNoteEditorHistory();
                ScrollSelectedNoteTabIntoView();
            });
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.NoteTabWidth))
        {
            Dispatcher.UIThread.Post(ScrollSelectedNoteTabIntoView);
        }
    }

    private void PreviousNoteTabButton_OnClick(object? sender, RoutedEventArgs e) =>
        ScrollNoteTabsBy(-GetNoteTabPageScrollAmount());

    private void NextNoteTabButton_OnClick(object? sender, RoutedEventArgs e) =>
        ScrollNoteTabsBy(GetNoteTabPageScrollAmount());

    private void NoteTabsScrollViewer_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NoteTabRailViewportWidth = e.NewSize.Width;
        }

        Dispatcher.UIThread.Post(ScrollSelectedNoteTabIntoView);
    }

    private double GetNoteTabPageScrollAmount()
    {
        var viewportWidth = NoteTabsScrollViewer.Viewport.Width;
        return viewportWidth > 0 && !double.IsNaN(viewportWidth)
            ? Math.Max(96, viewportWidth * 0.8)
            : 144;
    }

    private void ScrollNoteTabsBy(double delta) =>
        SetNoteTabsOffset(NoteTabsScrollViewer.Offset.X + delta);

    private void ScrollSelectedNoteTabIntoView()
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedNoteTab is null)
        {
            return;
        }

        var index = viewModel.OpenNoteTabs.IndexOf(viewModel.SelectedNoteTab);
        if (index < 0)
        {
            return;
        }

        var viewportWidth = NoteTabsScrollViewer.Viewport.Width;
        if (viewportWidth <= 0 || double.IsNaN(viewportWidth))
        {
            return;
        }

        var tabStride = viewModel.NoteTabWidth + 4;
        var tabStart = index * tabStride;
        var tabEnd = tabStart + tabStride;
        var currentOffset = NoteTabsScrollViewer.Offset.X;
        var targetOffset = currentOffset;

        if (tabStart < currentOffset)
        {
            targetOffset = tabStart;
        }
        else if (tabEnd > currentOffset + viewportWidth)
        {
            targetOffset = tabEnd - viewportWidth;
        }

        SetNoteTabsOffset(targetOffset);
    }

    private void SetNoteTabsOffset(double targetOffset)
    {
        var viewportWidth = NoteTabsScrollViewer.Viewport.Width;
        var extentWidth = NoteTabsScrollViewer.Extent.Width;
        if (viewportWidth <= 0 || extentWidth <= 0 || double.IsNaN(viewportWidth) || double.IsNaN(extentWidth))
        {
            return;
        }

        var maxOffset = Math.Max(0, extentWidth - viewportWidth);
        var x = Math.Clamp(targetOffset, 0, maxOffset);
        NoteTabsScrollViewer.Offset = new Vector(x, 0);
    }

    private void RestoreSelectedNoteTabSelection()
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedNoteTab is null)
        {
            return;
        }

        var textLength = (NoteContentEditor.Text ?? "").Length;
        var start = Math.Clamp(viewModel.SelectedNoteTab.DraftSelectionStart, 0, textLength);
        var end = Math.Clamp(viewModel.SelectedNoteTab.DraftSelectionEnd, 0, textLength);
        NoteContentEditor.SelectionStart = start;
        NoteContentEditor.SelectionEnd = end;
        if (viewModel.IsNoteEditorPaneVisible)
        {
            NoteContentEditor.Focus();
        }

        UpdateNoteEditorStatus();
    }

    private void MarkdownToolbarButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action })
        {
            return;
        }

        ApplyMarkdownAction(action);
    }

    private async void InsertNoteImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var markdown = await viewModel.PickNoteImageMarkdownAsync();
        if (!string.IsNullOrWhiteSpace(markdown))
        {
            InsertMarkdownBlock(markdown);
        }
    }

    private void UndoNoteEditorButton_OnClick(object? sender, RoutedEventArgs e) =>
        UndoNoteEditor();

    private void RedoNoteEditorButton_OnClick(object? sender, RoutedEventArgs e) =>
        RedoNoteEditor();

    private async void CloseNoteTabButton_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control { DataContext: NoteEditorTab tab } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await CloseNoteTabWithPromptAsync(viewModel, tab);
    }

    private async Task CloseNoteTabWithPromptAsync(MainWindowViewModel viewModel, NoteEditorTab tab)
    {
        if (!tab.IsDirty)
        {
            CloseNoteTab(viewModel, tab);
            return;
        }

        var result = await ShowUnsavedNoteTabDialogAsync(tab);
        if (result == FAContentDialogResult.Primary)
        {
            viewModel.SelectedNoteTab = tab;
            await viewModel.SaveNoteCommand.ExecuteAsync(null);
            if (!tab.IsDirty)
            {
                CloseNoteTab(viewModel, tab);
            }

            return;
        }

        if (result == FAContentDialogResult.Secondary)
        {
            CloseNoteTab(viewModel, tab);
        }
    }

    private void CloseNoteTab(MainWindowViewModel viewModel, NoteEditorTab tab)
    {
        viewModel.CloseNoteTabCommand.Execute(tab);
        _noteEditorHistories.Remove(tab);
    }

    private async Task<FAContentDialogResult> ShowUnsavedNoteTabDialogAsync(NoteEditorTab tab)
    {
        var title = string.IsNullOrWhiteSpace(tab.Title) ? "未命名笔记" : tab.Title.Trim();
        var dialog = new FAContentDialog
        {
            Title = "保存对此笔记的更改？",
            Content = new TextBlock
            {
                Text = $"“{title}”有未保存的更改。关闭前要保存吗？",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 420
            },
            PrimaryButtonText = "保存",
            SecondaryButtonText = "放弃",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Primary
        };

        return await dialog.ShowAsync(this);
    }

    private async Task<FAContentDialogResult> ShowUnsavedNoteTabsDialogAsync(int dirtyCount)
    {
        var dialog = new FAContentDialog
        {
            Title = "保存未保存的笔记？",
            Content = new TextBlock
            {
                Text = $"还有 {dirtyCount} 个笔记标签包含未保存的更改。关闭 Monica 前要保存全部吗？",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 440
            },
            PrimaryButtonText = "保存全部",
            SecondaryButtonText = "放弃",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Primary
        };

        return await dialog.ShowAsync(this);
    }

    private void NoteFindTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateNoteFindStatus();
        if (NoteFindPanel.IsVisible && !string.IsNullOrEmpty(NoteFindTextBox.Text))
        {
            SelectFirstNoteFindMatch(focusEditor: false);
        }
    }

    private void NoteFindTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideNoteFindPanel();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            SelectNoteFindMatch(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift), focusEditor: true);
            e.Handled = true;
        }
    }

    private void NoteReplaceTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideNoteFindPanel();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ReplaceAllNoteMatches();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            ReplaceCurrentNoteMatch();
            e.Handled = true;
        }
    }

    private void FindPreviousNoteMatchButton_OnClick(object? sender, RoutedEventArgs e) =>
        SelectNoteFindMatch(forward: false, focusEditor: true);

    private void FindNextNoteMatchButton_OnClick(object? sender, RoutedEventArgs e) =>
        SelectNoteFindMatch(forward: true, focusEditor: true);

    private void ReplaceCurrentNoteMatchButton_OnClick(object? sender, RoutedEventArgs e) =>
        ReplaceCurrentNoteMatch();

    private void ReplaceAllNoteMatchesButton_OnClick(object? sender, RoutedEventArgs e) =>
        ReplaceAllNoteMatches();

    private void NoteFindOptions_OnChanged(object? sender, RoutedEventArgs e)
    {
        UpdateNoteFindStatus();
        SelectFirstNoteFindMatch(focusEditor: false);
    }

    private void CloseNoteFindPanelButton_OnClick(object? sender, RoutedEventArgs e) =>
        HideNoteFindPanel();

    private async void NoteContentEditor_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && NoteFindPanel.IsVisible)
        {
            HideNoteFindPanel();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F3)
        {
            SelectNoteFindMatch(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift), focusEditor: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab &&
            (e.KeyModifiers == KeyModifiers.None || e.KeyModifiers == KeyModifiers.Shift))
        {
            IndentSelectedLines(outdent: e.KeyModifiers == KeyModifiers.Shift);
            e.Handled = true;
            UpdateNoteEditorStatus();
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && TryHandleMarkdownEnterContinuation())
        {
            e.Handled = true;
            UpdateNoteEditorStatus();
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (e.Key == Key.F)
        {
            ShowNoteFindPanel(replaceMode: false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.H)
        {
            ShowNoteFindPanel(replaceMode: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            RedoNoteEditor();
            e.Handled = true;
            UpdateNoteEditorStatus();
            return;
        }

        if (e.Key == Key.Z)
        {
            UndoNoteEditor();
            e.Handled = true;
            UpdateNoteEditorStatus();
            return;
        }

        if (e.Key == Key.Y)
        {
            RedoNoteEditor();
            e.Handled = true;
            UpdateNoteEditorStatus();
            return;
        }

        if (DataContext is MainWindowViewModel noteViewModel)
        {
            if (e.Key == Key.N && e.KeyModifiers == KeyModifiers.Control)
            {
                noteViewModel.AddNoteCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.W && e.KeyModifiers == KeyModifiers.Control && noteViewModel.SelectedNoteTab is not null)
            {
                e.Handled = true;
                await CloseNoteTabWithPromptAsync(noteViewModel, noteViewModel.SelectedNoteTab);
                UpdateNoteEditorStatus();
                return;
            }

            if (e.Key == Key.PageUp && e.KeyModifiers == KeyModifiers.Control &&
                noteViewModel.SelectPreviousNoteTabCommand.CanExecute(null))
            {
                noteViewModel.SelectPreviousNoteTabCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.PageDown && e.KeyModifiers == KeyModifiers.Control &&
                noteViewModel.SelectNextNoteTabCommand.CanExecute(null))
            {
                noteViewModel.SelectNextNoteTabCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        var handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            switch (e.Key)
            {
                case Key.D1:
                case Key.NumPad1:
                    ApplyMarkdownAction("h1");
                    break;
                case Key.D2:
                case Key.NumPad2:
                    ApplyMarkdownAction("h2");
                    break;
                case Key.D3:
                case Key.NumPad3:
                    ApplyMarkdownAction("h3");
                    break;
                default:
                    handled = false;
                    break;
            }
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            switch (e.Key)
            {
                case Key.D7:
                case Key.NumPad7:
                    ApplyMarkdownAction("ol");
                    break;
                case Key.D8:
                case Key.NumPad8:
                    ApplyMarkdownAction("ul");
                    break;
                case Key.X:
                    ApplyMarkdownAction("strike");
                    break;
                case Key.S:
                    if (DataContext is MainWindowViewModel viewModel)
                    {
                        await viewModel.SaveAllNoteTabsCommand.ExecuteAsync(null);
                    }
                    break;
                default:
                    handled = false;
                    break;
            }
        }
        else
        {
            switch (e.Key)
            {
                case Key.B:
                    ApplyMarkdownAction("bold");
                    break;
                case Key.I:
                    ApplyMarkdownAction("italic");
                    break;
                case Key.E:
                    ApplyMarkdownAction("inlinecode");
                    break;
                case Key.K:
                    ApplyMarkdownAction("link");
                    break;
                case Key.S:
                    if (DataContext is MainWindowViewModel viewModel)
                    {
                        await viewModel.SaveNoteCommand.ExecuteAsync(null);
                    }
                    break;
                default:
                    handled = false;
                    break;
            }
        }

        if (handled)
        {
            e.Handled = true;
            UpdateNoteEditorStatus();
        }
    }

    private void NoteContentEditor_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_isRestoringNoteEditorHistory)
        {
            CaptureNoteEditorHistorySnapshot();
        }

        UpdateNoteEditorStatus();
        UpdateNoteFindStatus();
    }

    private void NoteContentEditor_OnKeyUp(object? sender, KeyEventArgs e)
    {
        UpdateNoteEditorStatus();
        UpdateNoteFindStatus();
    }

    private void NoteContentEditor_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        UpdateNoteEditorStatus();
        UpdateNoteFindStatus();
    }

    private void ShowNoteFindPanel(bool replaceMode)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NotePreviewMode = false;
        }

        NoteFindPanel.IsVisible = true;
        NoteReplaceTextBox.IsVisible = replaceMode || NoteReplaceTextBox.IsVisible;
        var selectedText = GetSelectedNoteText();
        if (!string.IsNullOrWhiteSpace(selectedText) && !selectedText.Contains('\n'))
        {
            NoteFindTextBox.Text = selectedText;
        }

        UpdateNoteFindStatus();
        if (replaceMode)
        {
            NoteReplaceTextBox.Focus();
            NoteReplaceTextBox.SelectAll();
        }
        else
        {
            NoteFindTextBox.Focus();
            NoteFindTextBox.SelectAll();
        }
    }

    private void HideNoteFindPanel()
    {
        NoteFindPanel.IsVisible = false;
        NoteContentEditor.Focus();
    }

    private string GetSelectedNoteText()
    {
        var text = NoteContentEditor.Text ?? "";
        var (start, end) = GetSelectionRange(NoteContentEditor, text.Length);
        return start == end ? "" : text[start..end];
    }

    private void SelectFirstNoteFindMatch(bool focusEditor)
    {
        var matches = GetNoteFindMatches();
        if (matches.Count == 0)
        {
            UpdateNoteFindStatus(matches);
            return;
        }

        SelectNoteFindMatch(matches[0], focusEditor);
    }

    private void SelectNoteFindMatch(bool forward, bool focusEditor)
    {
        var matches = GetNoteFindMatches();
        if (matches.Count == 0)
        {
            UpdateNoteFindStatus(matches);
            return;
        }

        var queryLength = GetNoteFindQuery().Length;
        var selectedIndex = GetSelectedNoteFindMatchIndex(matches, queryLength);
        int targetIndex;
        if (selectedIndex >= 0)
        {
            targetIndex = forward
                ? (selectedIndex + 1) % matches.Count
                : (selectedIndex - 1 + matches.Count) % matches.Count;
        }
        else
        {
            var caret = Math.Clamp(NoteContentEditor.SelectionEnd, 0, (NoteContentEditor.Text ?? "").Length);
            targetIndex = forward
                ? matches.FindIndex(index => index >= caret)
                : matches.FindLastIndex(index => index < caret);

            if (targetIndex < 0)
            {
                targetIndex = forward ? 0 : matches.Count - 1;
            }
        }

        SelectNoteFindMatch(matches[targetIndex], focusEditor);
    }

    private void SelectNoteFindMatch(int index, bool focusEditor)
    {
        var query = GetNoteFindQuery();
        if (query.Length == 0)
        {
            UpdateNoteFindStatus();
            return;
        }

        var textLength = (NoteContentEditor.Text ?? "").Length;
        var start = Math.Clamp(index, 0, textLength);
        var end = Math.Clamp(start + query.Length, start, textLength);
        NoteContentEditor.SelectionStart = start;
        NoteContentEditor.SelectionEnd = end;
        if (focusEditor)
        {
            NoteContentEditor.Focus();
        }

        UpdateNoteFindStatus();
        UpdateNoteEditorStatus();
    }

    private void ReplaceCurrentNoteMatch()
    {
        var query = GetNoteFindQuery();
        if (query.Length == 0)
        {
            return;
        }

        var text = NoteContentEditor.Text ?? "";
        var (start, end) = GetSelectionRange(NoteContentEditor, text.Length);
        if (end - start != query.Length ||
            !string.Equals(text[start..end], query, GetNoteFindComparison()))
        {
            SelectNoteFindMatch(forward: true, focusEditor: true);
            return;
        }

        var replacement = NoteReplaceTextBox.Text ?? "";
        ReplaceEditorText(start, end, replacement);
        var nextCaret = start + replacement.Length;
        NoteContentEditor.SelectionStart = nextCaret;
        NoteContentEditor.SelectionEnd = nextCaret;
        CaptureNoteEditorHistorySnapshot();
        SelectNoteFindMatch(forward: true, focusEditor: true);
    }

    private void ReplaceAllNoteMatches()
    {
        var query = GetNoteFindQuery();
        if (query.Length == 0)
        {
            return;
        }

        var text = NoteContentEditor.Text ?? "";
        var replacement = NoteReplaceTextBox.Text ?? "";
        var comparison = GetNoteFindComparison();
        var builder = new StringBuilder(text.Length);
        var index = 0;
        var count = 0;
        while (index < text.Length)
        {
            var matchIndex = text.IndexOf(query, index, comparison);
            if (matchIndex < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            builder.Append(text, index, matchIndex - index);
            builder.Append(replacement);
            index = matchIndex + query.Length;
            count++;
        }

        if (count == 0)
        {
            UpdateNoteFindStatus();
            return;
        }

        ReplaceEditorText(0, text.Length, builder.ToString());
        NoteContentEditor.SelectionStart = 0;
        NoteContentEditor.SelectionEnd = 0;
        CaptureNoteEditorHistorySnapshot();
        UpdateNoteFindStatus();
        NoteFindStatusText.Text = $"已替换 {count} 处";
        NoteContentEditor.Focus();
    }

    private List<int> GetNoteFindMatches()
    {
        var text = NoteContentEditor.Text ?? "";
        var query = GetNoteFindQuery();
        var matches = new List<int>();
        if (query.Length == 0 || text.Length == 0)
        {
            return matches;
        }

        var comparison = GetNoteFindComparison();
        var index = 0;
        while (index <= text.Length - query.Length)
        {
            var matchIndex = text.IndexOf(query, index, comparison);
            if (matchIndex < 0)
            {
                break;
            }

            matches.Add(matchIndex);
            index = matchIndex + query.Length;
        }

        return matches;
    }

    private string GetNoteFindQuery() =>
        NoteFindTextBox.Text ?? "";

    private StringComparison GetNoteFindComparison() =>
        NoteFindMatchCaseCheckBox.IsChecked == true
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

    private void UpdateNoteFindStatus() =>
        UpdateNoteFindStatus(GetNoteFindMatches());

    private void UpdateNoteFindStatus(IReadOnlyList<int> matches)
    {
        if (!NoteFindPanel.IsVisible)
        {
            return;
        }

        var queryLength = GetNoteFindQuery().Length;
        if (queryLength == 0)
        {
            NoteFindStatusText.Text = "0/0";
            return;
        }

        if (matches.Count == 0)
        {
            NoteFindStatusText.Text = "无匹配";
            return;
        }

        var selectedIndex = GetSelectedNoteFindMatchIndex(matches, queryLength);
        NoteFindStatusText.Text = selectedIndex >= 0
            ? $"{selectedIndex + 1}/{matches.Count}"
            : $"0/{matches.Count}";
    }

    private int GetSelectedNoteFindMatchIndex(IReadOnlyList<int> matches, int queryLength)
    {
        if (queryLength == 0)
        {
            return -1;
        }

        var text = NoteContentEditor.Text ?? "";
        var (start, end) = GetSelectionRange(NoteContentEditor, text.Length);
        if (end - start != queryLength)
        {
            return -1;
        }

        for (var index = 0; index < matches.Count; index++)
        {
            if (matches[index] == start)
            {
                return index;
            }
        }

        return -1;
    }

    private void EnsureSelectedNoteEditorHistory()
    {
        var state = GetCurrentNoteEditorHistory();
        if (state.Entries.Count == 0)
        {
            CaptureNoteEditorHistorySnapshot(force: true);
        }
        else
        {
            CaptureNoteEditorHistorySnapshot();
        }
    }

    private void CaptureNoteEditorHistorySnapshot(bool force = false)
    {
        var history = GetCurrentNoteEditorHistory();
        var text = NoteContentEditor.Text ?? "";
        var snapshot = new NoteEditorSnapshot(
            text,
            Math.Clamp(NoteContentEditor.SelectionStart, 0, text.Length),
            Math.Clamp(NoteContentEditor.SelectionEnd, 0, text.Length));

        if (history.Index >= 0 && history.Index < history.Entries.Count)
        {
            var current = history.Entries[history.Index];
            if (!force && string.Equals(current.Text, snapshot.Text, StringComparison.Ordinal))
            {
                history.Entries[history.Index] = snapshot;
                return;
            }
        }

        if (history.Index < history.Entries.Count - 1)
        {
            history.Entries.RemoveRange(history.Index + 1, history.Entries.Count - history.Index - 1);
        }

        history.Entries.Add(snapshot);
        if (history.Entries.Count > MaxNoteEditorHistoryEntries)
        {
            history.Entries.RemoveAt(0);
        }

        history.Index = history.Entries.Count - 1;
    }

    private void UndoNoteEditor()
    {
        var history = GetCurrentNoteEditorHistory();
        if (history.Index <= 0)
        {
            return;
        }

        history.Index--;
        RestoreNoteEditorSnapshot(history.Entries[history.Index]);
    }

    private void RedoNoteEditor()
    {
        var history = GetCurrentNoteEditorHistory();
        if (history.Index < 0 || history.Index >= history.Entries.Count - 1)
        {
            return;
        }

        history.Index++;
        RestoreNoteEditorSnapshot(history.Entries[history.Index]);
    }

    private NoteEditorHistoryState GetCurrentNoteEditorHistory()
    {
        return DataContext is MainWindowViewModel { SelectedNoteTab: { } tab }
            ? GetNoteEditorHistory(tab)
            : _fallbackNoteEditorHistory;
    }

    private NoteEditorHistoryState GetNoteEditorHistory(NoteEditorTab tab)
    {
        if (!_noteEditorHistories.TryGetValue(tab, out var history))
        {
            history = new NoteEditorHistoryState();
            _noteEditorHistories[tab] = history;
        }

        return history;
    }

    private void RestoreNoteEditorSnapshot(NoteEditorSnapshot snapshot)
    {
        _isRestoringNoteEditorHistory = true;
        try
        {
            NoteContentEditor.Text = snapshot.Text;
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.NoteContent = snapshot.Text;
            }

            var length = snapshot.Text.Length;
            NoteContentEditor.SelectionStart = Math.Clamp(snapshot.SelectionStart, 0, length);
            NoteContentEditor.SelectionEnd = Math.Clamp(snapshot.SelectionEnd, 0, length);
            NoteContentEditor.Focus();
        }
        finally
        {
            _isRestoringNoteEditorHistory = false;
        }

        UpdateNoteEditorStatus();
    }

    private void NoteOutlineItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: NoteOutlineItem item })
        {
            return;
        }

        JumpToNoteLine(item.LineNumber);
    }

    private void ApplyMarkdownAction(string action)
    {
        switch (action)
        {
            case "h1":
                PrefixSelectedLines("# ", stripMarkdownPrefixes: true);
                break;
            case "h2":
                PrefixSelectedLines("## ", stripMarkdownPrefixes: true);
                break;
            case "h3":
                PrefixSelectedLines("### ", stripMarkdownPrefixes: true);
                break;
            case "bold":
                WrapSelection("**", "**", "bold text");
                break;
            case "italic":
                WrapSelection("_", "_", "italic text");
                break;
            case "strike":
                WrapSelection("~~", "~~", "strikethrough");
                break;
            case "inlinecode":
                WrapSelection("`", "`", "code");
                break;
            case "quote":
                PrefixSelectedLines("> ");
                break;
            case "code":
                WrapBlock("```\n", "\n```", "code");
                break;
            case "ul":
                PrefixSelectedLines("- ");
                break;
            case "ol":
                PrefixSelectedLines((index, _) => $"{index + 1}. ");
                break;
            case "todo":
                PrefixSelectedLines("- [ ] ");
                break;
            case "table":
                InsertMarkdownBlock("| Column | Column |\n| --- | --- |\n| Value | Value |");
                break;
            case "link":
                WrapLink();
                break;
            case "hr":
                InsertMarkdownBlock("---");
                break;
        }

        UpdateNoteEditorStatus();
    }

    private void UpdateNoteEditorStatus()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.UpdateNoteEditorStatus(
                NoteContentEditor.SelectionEnd,
                NoteContentEditor.SelectionStart,
                NoteContentEditor.SelectionEnd);
        }
    }

    private void JumpToNoteLine(int lineNumber)
    {
        if (lineNumber <= 0)
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NotePreviewMode = false;
        }

        var text = NoteContentEditor.Text ?? "";
        var index = GetLineStartIndex(text, lineNumber);
        NoteContentEditor.SelectionStart = index;
        NoteContentEditor.SelectionEnd = index;
        NoteContentEditor.Focus();
        UpdateNoteEditorStatus();
    }

    private void WrapSelection(string prefix, string suffix, string placeholder)
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (start, end) = GetSelectionRange(editor, text.Length);
        var selected = text[start..end];
        var inner = string.IsNullOrEmpty(selected) ? placeholder : selected;
        ReplaceEditorText(start, end, prefix + inner + suffix);
        editor.SelectionStart = start + prefix.Length;
        editor.SelectionEnd = start + prefix.Length + inner.Length;
        editor.Focus();
    }

    private void WrapBlock(string prefix, string suffix, string placeholder)
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (start, end) = GetSelectionRange(editor, text.Length);
        var selected = text[start..end];
        var inner = string.IsNullOrEmpty(selected) ? placeholder : selected.Trim('\r', '\n');
        var replacement = prefix + inner + suffix;
        ReplaceEditorText(start, end, replacement);
        editor.SelectionStart = start + prefix.Length;
        editor.SelectionEnd = start + prefix.Length + inner.Length;
        editor.Focus();
    }

    private void WrapLink()
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (start, end) = GetSelectionRange(editor, text.Length);
        var selected = text[start..end];
        var label = string.IsNullOrWhiteSpace(selected) ? "link text" : selected;
        var replacement = $"[{label}](https://)";
        ReplaceEditorText(start, end, replacement);
        var urlStart = start + label.Length + 3;
        editor.SelectionStart = urlStart;
        editor.SelectionEnd = urlStart + "https://".Length;
        editor.Focus();
    }

    private void InsertMarkdownBlock(string markdown)
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (start, end) = GetSelectionRange(editor, text.Length);
        var prefix = GetBlockPrefix(text, start);
        var suffix = GetBlockSuffix(text, end);
        var replacement = prefix + markdown + suffix;
        ReplaceEditorText(start, end, replacement);
        var caret = start + replacement.Length - suffix.Length;
        editor.SelectionStart = caret;
        editor.SelectionEnd = caret;
        editor.Focus();
    }

    private bool TryHandleMarkdownEnterContinuation()
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (start, end) = GetSelectionRange(editor, text.Length);
        if (start != end)
        {
            return false;
        }

        var lineStart = FindLineStart(text, start);
        var lineEnd = FindLineEnd(text, start);
        var currentLine = text[lineStart..lineEnd];
        if (!TryGetContinuationMarker(currentLine, out var markerLength, out var nextMarker, out var exitText))
        {
            return false;
        }

        if (start < lineStart + markerLength)
        {
            return false;
        }

        var currentBody = currentLine[markerLength..].Trim();
        if (currentBody.Length == 0)
        {
            ReplaceEditorText(lineStart, lineEnd, exitText);
            var caret = lineStart + exitText.Length;
            editor.SelectionStart = caret;
            editor.SelectionEnd = caret;
            editor.Focus();
            return true;
        }

        var replacement = "\n" + nextMarker;
        ReplaceEditorText(start, start, replacement);
        var nextCaret = start + replacement.Length;
        editor.SelectionStart = nextCaret;
        editor.SelectionEnd = nextCaret;
        editor.Focus();
        return true;
    }

    private static bool TryGetContinuationMarker(
        string line,
        out int markerLength,
        out string nextMarker,
        out string exitText)
    {
        markerLength = 0;
        nextMarker = "";
        exitText = "";

        var taskMatch = TaskListLineRegex.Match(line);
        if (taskMatch.Success)
        {
            var indent = taskMatch.Groups[1].Value;
            var bullet = taskMatch.Groups[2].Value;
            markerLength = indent.Length + bullet.Length + " [ ] ".Length;
            nextMarker = $"{indent}{bullet} [ ] ";
            exitText = indent;
            return true;
        }

        var orderedMatch = OrderedListLineRegex.Match(line);
        if (orderedMatch.Success)
        {
            var indent = orderedMatch.Groups[1].Value;
            var numberText = orderedMatch.Groups[2].Value;
            var delimiter = orderedMatch.Groups[3].Value;
            markerLength = indent.Length + numberText.Length + delimiter.Length + 1;
            var nextNumber = int.TryParse(numberText, out var number) ? number + 1 : 1;
            nextMarker = $"{indent}{nextNumber}{delimiter} ";
            exitText = indent;
            return true;
        }

        var unorderedMatch = UnorderedListLineRegex.Match(line);
        if (unorderedMatch.Success)
        {
            var indent = unorderedMatch.Groups[1].Value;
            var bullet = unorderedMatch.Groups[2].Value;
            markerLength = indent.Length + bullet.Length + 1;
            nextMarker = $"{indent}{bullet} ";
            exitText = indent;
            return true;
        }

        var quoteMatch = QuoteLineRegex.Match(line);
        if (quoteMatch.Success)
        {
            var prefix = quoteMatch.Groups[1].Value;
            markerLength = prefix.Length;
            nextMarker = prefix;
            exitText = "";
            return true;
        }

        return false;
    }

    private void PrefixSelectedLines(string prefix, bool stripMarkdownPrefixes = false) =>
        PrefixSelectedLines((_, _) => prefix, stripMarkdownPrefixes);

    private void IndentSelectedLines(bool outdent)
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (selectionStart, selectionEnd) = GetSelectionRange(editor, text.Length);
        var lineStart = FindLineStart(text, selectionStart);
        var adjustedEnd = selectionEnd > selectionStart ? Math.Max(selectionStart, selectionEnd - 1) : selectionEnd;
        var lineEnd = FindLineEnd(text, adjustedEnd);
        var selectedBlock = text[lineStart..lineEnd];
        var lines = selectedBlock.Split('\n');
        var transformed = lines
            .Select(line => outdent ? OutdentLine(line) : "    " + line)
            .ToArray();
        var replacement = string.Join("\n", transformed);
        ReplaceEditorText(lineStart, lineEnd, replacement);
        editor.SelectionStart = lineStart;
        editor.SelectionEnd = lineStart + replacement.Length;
        editor.Focus();
    }

    private void PrefixSelectedLines(Func<int, string, string> prefixFactory, bool stripMarkdownPrefixes = false)
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        var (selectionStart, selectionEnd) = GetSelectionRange(editor, text.Length);
        var lineStart = FindLineStart(text, selectionStart);
        var lineEnd = FindLineEnd(text, selectionEnd);
        var selectedBlock = text[lineStart..lineEnd];
        var lines = selectedBlock.Split('\n');
        var transformed = lines
            .Select((line, index) =>
            {
                var normalized = line.TrimEnd('\r');
                var body = stripMarkdownPrefixes ? StripHeadingPrefix(normalized) : normalized;
                return prefixFactory(index, body) + body;
            })
            .ToArray();
        var replacement = string.Join("\n", transformed);
        ReplaceEditorText(lineStart, lineEnd, replacement);
        editor.SelectionStart = lineStart;
        editor.SelectionEnd = lineStart + replacement.Length;
        editor.Focus();
    }

    private static string OutdentLine(string line)
    {
        if (line.StartsWith('\t'))
        {
            return line[1..];
        }

        var removeCount = 0;
        while (removeCount < 4 && removeCount < line.Length && line[removeCount] == ' ')
        {
            removeCount++;
        }

        return removeCount == 0 ? line : line[removeCount..];
    }

    private void ReplaceEditorText(int start, int end, string replacement)
    {
        var editor = NoteContentEditor;
        var text = editor.Text ?? "";
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);
        var updated = text[..start] + replacement + text[end..];
        editor.Text = updated;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NoteContent = updated;
        }
    }

    private static (int Start, int End) GetSelectionRange(TextBox editor, int textLength)
    {
        var start = Math.Clamp(Math.Min(editor.SelectionStart, editor.SelectionEnd), 0, textLength);
        var end = Math.Clamp(Math.Max(editor.SelectionStart, editor.SelectionEnd), start, textLength);
        return (start, end);
    }

    private static int FindLineStart(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        var lineBreak = text.LastIndexOf('\n', Math.Max(0, index - 1));
        return lineBreak < 0 ? 0 : lineBreak + 1;
    }

    private static int FindLineEnd(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        var lineBreak = text.IndexOf('\n', index);
        return lineBreak < 0 ? text.Length : lineBreak;
    }

    private static int GetLineStartIndex(string text, int lineNumber)
    {
        if (lineNumber <= 1 || string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var currentLine = 1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            currentLine++;
            if (currentLine == lineNumber)
            {
                return Math.Min(index + 1, text.Length);
            }
        }

        return text.Length;
    }

    private static string StripHeadingPrefix(string line)
    {
        var trimmed = line.TrimStart();
        var offset = line.Length - trimmed.Length;
        var markerLength = 0;
        while (markerLength < trimmed.Length && trimmed[markerLength] == '#')
        {
            markerLength++;
        }

        if (markerLength == 0 || markerLength >= trimmed.Length || trimmed[markerLength] != ' ')
        {
            return line;
        }

        return line[..offset] + trimmed[(markerLength + 1)..];
    }

    private static string GetBlockPrefix(string text, int start)
    {
        if (string.IsNullOrEmpty(text) || start == 0)
        {
            return "";
        }

        return text[Math.Max(0, start - 1)] == '\n' ? "" : "\n\n";
    }

    private static string GetBlockSuffix(string text, int end)
    {
        if (string.IsNullOrEmpty(text) || end >= text.Length)
        {
            return "";
        }

        return text[end] == '\n' ? "" : "\n\n";
    }
}
