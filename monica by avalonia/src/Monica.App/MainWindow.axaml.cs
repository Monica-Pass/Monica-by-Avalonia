using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Monica.App.ViewModels;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Monica.App;

public partial class MainWindow : Window
{
    private static readonly Regex OrderedListLineRegex = new(@"^(\s*)(\d+)([.)])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex TaskListLineRegex = new(@"^(\s*)([-*+])\s+\[(?: |x|X)\]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex UnorderedListLineRegex = new(@"^(\s*)([-*+])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex QuoteLineRegex = new(@"^(\s*(?:>\s*)+)(.*)$", RegexOptions.Compiled);
    private bool _isClosingAfterUnsavedNoteDecision;
    private bool _isHandlingUnsavedWindowClose;
    private MainWindowViewModel? _observedViewModel;

    private sealed record SmokePageLayoutCheck(
        string Section,
        string[] RequiredClasses,
        string[] RequiredVisibleClasses);

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        DataContextChanged += OnDataContextChanged;
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

    private void MainWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            !viewModel.IsUnlocked)
        {
            return;
        }

        if (string.Equals(viewModel.SelectedSection, "Notes", StringComparison.OrdinalIgnoreCase))
        {
            HandleNoteWorkspaceShortcut(viewModel, e);
            if (e.Handled)
            {
                return;
            }
        }

        if (e.Key == Key.F5 && !IsTextEditingSource(e.Source))
        {
            if (viewModel.LoadCommand.CanExecute(null))
            {
                viewModel.LoadCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.N)
        {
            if (TryExecuteCurrentSectionNewCommand(viewModel))
            {
                e.Handled = true;
                return;
            }
        }

        if (!string.Equals(viewModel.SelectedSection, "Passwords", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        {
            PasswordVaultView.FocusSearch();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.N)
        {
            if (viewModel.AddPasswordCommand.CanExecute(null))
            {
                viewModel.AddPasswordCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape && viewModel.HasPasswordFilters)
        {
            if (viewModel.ClearPasswordFiltersCommand.CanExecute(null))
            {
                viewModel.ClearPasswordFiltersCommand.Execute(null);
                e.Handled = true;
            }
        }

        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (e.Key is Key.Up or Key.Down && !IsNonSearchPasswordTextEditingSource(e.Source))
        {
            SelectAdjacentPassword(viewModel, e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && viewModel.HasCurrentSelectedPasswordDetails && !IsNonSearchPasswordTextEditingSource(e.Source))
        {
            PasswordVaultView.FocusDetails();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && !IsTextEditingSource(e.Source))
        {
            if (viewModel.SelectedPassword is not null &&
                viewModel.DeletePasswordCommand.CanExecute(viewModel.SelectedPassword))
            {
                viewModel.DeletePasswordCommand.Execute(viewModel.SelectedPassword);
                e.Handled = true;
            }
        }
    }

    private static bool TryExecuteCurrentSectionNewCommand(MainWindowViewModel viewModel)
    {
        switch (viewModel.SelectedSection)
        {
            case "Passwords":
                if (viewModel.AddPasswordCommand.CanExecute(null))
                {
                    viewModel.AddPasswordCommand.Execute(null);
                    return true;
                }

                return false;

            case "Totp":
                if (viewModel.AddTotpCommand.CanExecute(null))
                {
                    viewModel.AddTotpCommand.Execute(null);
                    return true;
                }

                return false;

            case "Cards":
                if (viewModel.AddWalletItemCommand.CanExecute(null))
                {
                    viewModel.AddWalletItemCommand.Execute(null);
                    return true;
                }

                return false;

            case "Generator":
                if (viewModel.GeneratePasswordCommand.CanExecute(null))
                {
                    viewModel.GeneratePasswordCommand.Execute(null);
                    return true;
                }

                return false;

            case "Mdbx":
                if (viewModel.CreateMdbxVaultCommand.CanExecute(null))
                {
                    viewModel.CreateMdbxVaultCommand.Execute(null);
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private async void HandleNoteWorkspaceShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
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

        if (e.Key == Key.S)
        {
            e.Handled = true;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                await viewModel.SaveAllNoteTabsCommand.ExecuteAsync(null);
            }
            else
            {
                await viewModel.SaveNoteCommand.ExecuteAsync(null);
            }

            return;
        }

        if (e.Key == Key.N)
        {
            viewModel.AddNoteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.W && viewModel.SelectedNoteTab is not null)
        {
            e.Handled = true;
            await CloseNoteTabWithPromptAsync(viewModel, viewModel.SelectedNoteTab);
        }
    }

    private void SelectAdjacentPassword(MainWindowViewModel viewModel, int delta)
    {
        var visiblePasswords = viewModel.VisiblePasswordNavigationEntries.ToList();
        if (visiblePasswords.Count == 0)
        {
            return;
        }

        var currentIndex = viewModel.SelectedPassword is null
            ? -1
            : visiblePasswords.FindIndex(item => item.Id == viewModel.SelectedPassword.Id);
        var nextIndex = currentIndex < 0
            ? (delta > 0 ? 0 : visiblePasswords.Count - 1)
            : Math.Clamp(currentIndex + delta, 0, visiblePasswords.Count - 1);

        viewModel.SelectedPassword = visiblePasswords[nextIndex];
        if (viewModel.SelectedPasswordListRow is { } row)
        {
            Dispatcher.UIThread.Post(() => PasswordVaultView.ScrollIntoView(row));
        }
    }

    private bool IsNonSearchPasswordTextEditingSource(object? source) =>
        source is TextBox textBox && !PasswordVaultView.IsSearchBox(textBox);

    private static bool IsTextEditingSource(object? source) => source is TextBox;

    private void OnClosed(object? sender, EventArgs e)
    {
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

        Dispatcher.UIThread.Post(UpdateNoteTabScrollButtons);
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
                UpdateNoteTabScrollButtons();
            });
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.NoteTabWidth))
        {
            Dispatcher.UIThread.Post(() =>
            {
                ScrollSelectedNoteTabIntoView();
                UpdateNoteTabScrollButtons();
            });
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.SelectedSection))
        {
            QueueWorkspaceScrollResetForSelectedSection();
        }
    }

    private void QueueWorkspaceScrollResetForSelectedSection()
    {
        ResetWorkspaceScrollForSelectedSection();
        Dispatcher.UIThread.Post(ResetWorkspaceScrollForSelectedSection);
        _ = ResetWorkspaceScrollForSelectedSectionDeferredAsync();
    }

    private async Task ResetWorkspaceScrollForSelectedSectionDeferredAsync()
    {
        await Task.Delay(120);
        await Dispatcher.UIThread.InvokeAsync(ResetWorkspaceScrollForSelectedSection);
        await Task.Delay(480);
        await Dispatcher.UIThread.InvokeAsync(ResetWorkspaceScrollForSelectedSection);
    }

    private void ResetWorkspaceScrollForSelectedSection()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (string.Equals(viewModel.SelectedSection, "DatabaseManagement", StringComparison.OrdinalIgnoreCase))
        {
            DatabaseManagementScrollViewer.Offset = new Vector(0, 0);
        }
    }

    private void WorkspaceHost_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OtherWorkspaceViewportWidth = e.NewSize.Width;
            viewModel.OtherWorkspaceViewportHeight = e.NewSize.Height;
        }
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

    private void ShowNoteFindButton_OnClick(object? sender, RoutedEventArgs e) =>
        ShowNoteFindPanel(replaceMode: false);

    private void ShowNoteReplaceButton_OnClick(object? sender, RoutedEventArgs e) =>
        ShowNoteFindPanel(replaceMode: true);

    private void SetNoteEditModeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.NotePreviewMode = false;
        viewModel.NoteSplitPreviewMode = false;
    }

    private void SetNotePreviewModeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.NoteSplitPreviewMode = false;
        viewModel.NotePreviewMode = true;
    }

    private void SetNoteSplitModeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.NotePreviewMode = false;
        viewModel.NoteSplitPreviewMode = true;
    }

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

    public async Task<bool> RunSmokeUiNoteEditorChecksAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(RunSmokeUiNoteEditorChecksAsync);
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI note editor failed. reason=no-view-model");
            return false;
        }

        viewModel.SelectSectionCommand.Execute("Notes");
        if (viewModel.SelectedNoteTab is null)
        {
            viewModel.AddNoteCommand.Execute(null);
        }

        viewModel.NoteTitle = "Smoke Markdown editor";
        viewModel.NoteIsMarkdown = true;
        viewModel.NotePreviewMode = false;
        viewModel.NoteSplitPreviewMode = false;
        var content = string.Join("\n",
            "# Smoke Markdown editor",
            "",
            "This line contains NEEDLE and a second needle for replace-all.",
            "",
            "## Links and images",
            "",
            "![inline](monica-image://smoke-image)",
            "[reference](https://example.invalid/smoke)",
            "",
            "- [ ] Task item",
            "1. Ordered item",
            "",
            "```",
            "needle inside code is intentionally replaceable in plain editor search",
            "```");

        viewModel.NoteContent = content;
        NoteContentEditor.Text = content;
        NoteContentEditor.SelectionStart = 0;
        NoteContentEditor.SelectionEnd = 0;
        CaptureNoteEditorHistorySnapshot(force: true);
        UpdateNoteEditorStatus();

        ShowNoteFindPanel(replaceMode: true);
        NoteFindTextBox.Text = "needle";
        NoteReplaceTextBox.Text = "thread";
        ReplaceAllNoteMatches();

        viewModel.NoteSplitPreviewMode = true;
        await viewModel.SaveNoteCommand.ExecuteAsync(null);

        var updatedContent = viewModel.NoteContent;
        var success =
            viewModel.SelectedNote is not null &&
            viewModel.NoteItems.Any(item => item.Id == viewModel.SelectedNote.Id) &&
            updatedContent.Contains("thread", StringComparison.OrdinalIgnoreCase) &&
            !updatedContent.Contains("needle", StringComparison.OrdinalIgnoreCase) &&
            viewModel.HasNoteOutlineItems &&
            viewModel.HasNoteReferenceItems &&
            viewModel.NoteReferenceItems.Any(item => item.IsImage) &&
            viewModel.NoteLineCount >= 12 &&
            viewModel.NoteSplitPreviewMode;

        AppDiagnostics.Info(
            $"Smoke UI note editor checks completed. success={success}, " +
            $"lineCount={viewModel.NoteLineCount}, outline={viewModel.NoteOutlineCount}, " +
            $"references={viewModel.NoteReferenceCount}, selectedNote={viewModel.SelectedNote?.Title}");
        return success;
    }

    public async Task<bool> RunSmokeUiKeyboardChecksAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(RunSmokeUiKeyboardChecksAsync);
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI keyboard checks failed. reason=no-view-model");
            return false;
        }

        var failures = new List<string>();
        void Check(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                AppDiagnostics.Info($"Smoke UI keyboard check passed. check={name}, {detail}");
                return;
            }

            failures.Add(name);
            AppDiagnostics.Info($"Smoke UI keyboard check failed. check={name}, {detail}");
        }

        try
        {
            var vaultReady = await WaitForSmokeWindowConditionAsync(
                () => viewModel.IsUnlocked && !viewModel.IsLoadingVault && viewModel.Passwords.Count > 0,
                TimeSpan.FromSeconds(10));
            Check("vault-ready", vaultReady, $"passwords={viewModel.Passwords.Count}");

            viewModel.SelectSectionCommand.Execute("Passwords");
            await Task.Delay(50);
            PasswordVaultView.FocusSearch();
            Check("password-search-focus", PasswordVaultView.IsSearchFocused, $"section={viewModel.SelectedSection}");

            viewModel.PasswordSearchText = "Smoke";
            await Task.Delay(50);
            Check("password-filter-active", viewModel.HasPasswordFilters, $"summary={viewModel.PasswordFilterSummaryText}");
            if (viewModel.ClearPasswordFiltersCommand.CanExecute(null))
            {
                viewModel.ClearPasswordFiltersCommand.Execute(null);
            }

            await Task.Delay(50);
            Check(
                "password-escape-clear-filters",
                !viewModel.HasPasswordFilters && string.IsNullOrWhiteSpace(viewModel.PasswordSearchText),
                $"search='{viewModel.PasswordSearchText}'");

            var visiblePasswords = viewModel.FilteredPasswords.ToArray();
            Check("password-list-has-rows", visiblePasswords.Length >= 2, $"count={visiblePasswords.Length}");
            if (visiblePasswords.Length >= 2)
            {
                viewModel.SelectedPassword = visiblePasswords[0];
                SelectAdjacentPassword(viewModel, 1);
                await Task.Delay(50);
                Check(
                    "password-arrow-select-next",
                    viewModel.SelectedPassword?.Id == visiblePasswords[1].Id,
                    $"selected={viewModel.SelectedPassword?.Title}");

                var detailsReady = await WaitForSmokeWindowConditionAsync(
                    () => viewModel.SelectedPasswordDetails?.Entry.Id == viewModel.SelectedPassword?.Id &&
                          viewModel.HasCurrentSelectedPasswordDetails &&
                          !viewModel.IsLoadingSelectedPasswordDetails,
                    TimeSpan.FromSeconds(3));
                Check("password-details-ready", detailsReady, $"selected={viewModel.SelectedPassword?.Title}");
                PasswordVaultView.FocusDetails();
                Check("password-enter-focus-details", PasswordVaultView.IsDetailFocused, $"hasDetails={viewModel.HasCurrentSelectedPasswordDetails}");
                Check(
                    "password-delete-command-available",
                    viewModel.SelectedPassword is not null &&
                    viewModel.DeletePasswordCommand.CanExecute(viewModel.SelectedPassword),
                    $"selected={viewModel.SelectedPassword?.Title}");
            }

            viewModel.SelectSectionCommand.Execute("Notes");
            await Task.Delay(50);
            if (viewModel.OpenNoteTabs.Count < 2)
            {
                viewModel.AddNoteCommand.Execute(null);
                viewModel.AddNoteCommand.Execute(null);
            }

            Check("note-tabs-available", viewModel.OpenNoteTabs.Count >= 2, $"tabs={viewModel.OpenNoteTabs.Count}");
            if (viewModel.OpenNoteTabs.Count >= 2)
            {
                viewModel.SelectedNoteTab = viewModel.OpenNoteTabs[0];
                if (viewModel.SelectNextNoteTabCommand.CanExecute(null))
                {
                    viewModel.SelectNextNoteTabCommand.Execute(null);
                }

                await Task.Delay(50);
                Check(
                    "note-ctrl-pagedown-next-tab",
                    viewModel.SelectedNoteTab == viewModel.OpenNoteTabs[1],
                    $"selected={viewModel.SelectedNoteTab?.Title}");
                if (viewModel.SelectPreviousNoteTabCommand.CanExecute(null))
                {
                    viewModel.SelectPreviousNoteTabCommand.Execute(null);
                }

                await Task.Delay(50);
                Check(
                    "note-ctrl-pageup-previous-tab",
                    viewModel.SelectedNoteTab == viewModel.OpenNoteTabs[0],
                    $"selected={viewModel.SelectedNoteTab?.Title}");
            }

            viewModel.NotePreviewMode = false;
            viewModel.NoteSplitPreviewMode = false;
            viewModel.NoteContent = "alpha\nbeta";
            NoteContentEditor.Text = viewModel.NoteContent;
            NoteContentEditor.SelectionStart = 0;
            NoteContentEditor.SelectionEnd = NoteContentEditor.Text?.Length ?? 0;
            NoteContentEditor.Focus();
            Check("note-editor-focus", NoteContentEditor.IsFocused, $"section={viewModel.SelectedSection}");

            IndentSelectedLines(outdent: false);
            Check(
                "note-tab-indents-lines",
                (NoteContentEditor.Text ?? "").StartsWith("    alpha", StringComparison.Ordinal),
                $"content='{FormatSmokeLogValue(NoteContentEditor.Text)}'");
            IndentSelectedLines(outdent: true);
            Check(
                "note-shift-tab-outdents-lines",
                string.Equals(NoteContentEditor.Text, "alpha\nbeta", StringComparison.Ordinal),
                $"content='{FormatSmokeLogValue(NoteContentEditor.Text)}'");

            ShowNoteFindPanel(replaceMode: false);
            await Task.Delay(50);
            Check(
                "note-ctrl-f-focus-find",
                NoteFindPanel.IsVisible && NoteFindTextBox.IsFocused,
                $"findVisible={NoteFindPanel.IsVisible}");
            HideNoteFindPanel();
            Check(
                "note-escape-closes-find",
                !NoteFindPanel.IsVisible && NoteContentEditor.IsFocused,
                $"findVisible={NoteFindPanel.IsVisible}");
            ShowNoteFindPanel(replaceMode: true);
            await Task.Delay(50);
            Check(
                "note-ctrl-h-focus-replace",
                NoteFindPanel.IsVisible && NoteReplaceTextBox.IsVisible && NoteReplaceTextBox.IsFocused,
                $"replaceVisible={NoteReplaceTextBox.IsVisible}");
            HideNoteFindPanel();

            viewModel.SelectSectionCommand.Execute("Generator");
            await Task.Delay(50);
            viewModel.GeneratedPassword = "";
            var generatorNewExecuted = TryExecuteCurrentSectionNewCommand(viewModel);
            await Task.Delay(50);
            Check(
                "ctrl-n-generator-generates",
                generatorNewExecuted && !string.IsNullOrWhiteSpace(viewModel.GeneratedPassword),
                $"generatedLength={viewModel.GeneratedPassword?.Length ?? 0}");

            viewModel.SelectSectionCommand.Execute("Totp");
            await Task.Delay(50);
            Check(
                "ctrl-n-totp-action-available",
                viewModel.AddTotpCommand.CanExecute(null),
                $"section={viewModel.SelectedSection}");

            viewModel.SelectSectionCommand.Execute("Cards");
            await Task.Delay(50);
            Check(
                "ctrl-n-wallet-action-available",
                viewModel.AddWalletItemCommand.CanExecute(null),
                $"section={viewModel.SelectedSection}");

            viewModel.SelectSectionCommand.Execute("Mdbx");
            await Task.Delay(50);
            Check(
                "ctrl-n-mdbx-action-available",
                viewModel.CreateMdbxVaultCommand.CanExecute(null),
                $"section={viewModel.SelectedSection}");
        }
        catch (Exception ex)
        {
            failures.Add("exception");
            AppDiagnostics.Error("Smoke UI keyboard checks failed", ex);
        }

        var success = failures.Count == 0;
        AppDiagnostics.Info(
            $"Smoke UI keyboard checks completed. success={success}, " +
            $"failureCount={failures.Count}, failures={string.Join(",", failures)}");
        return success;
    }

    public async Task<bool> RunSmokeUiOtherPagesChecksAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(RunSmokeUiOtherPagesChecksAsync);
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI other pages checks failed. reason=no-view-model");
            return false;
        }

        var pages = new[]
        {
            new SmokePageLayoutCheck(
                "Totp",
                ["totpAccountList"],
                ["totpCodeConsole", "totpInspector"]),
            new SmokePageLayoutCheck(
                "Cards",
                ["walletItemList"],
                ["walletWorkbench", "walletInspector"]),
            new SmokePageLayoutCheck(
                "Generator",
                [],
                ["generatorResultPanel", "generatorOptionsPanel"]),
            new SmokePageLayoutCheck(
                "Archive",
                ["archiveRecoveryList"],
                ["archiveFilterRail", "archiveRecoveryPanel"]),
            new SmokePageLayoutCheck(
                "RecycleBin",
                ["recycleQueueList"],
                ["recycleFilterRail", "recycleRiskPanel"]),
            new SmokePageLayoutCheck(
                "Timeline",
                ["timelineEventStream"],
                ["timelineFilterRail", "timelineInspector"]),
            new SmokePageLayoutCheck(
                "Mdbx",
                ["mdbxWorkingCopyList", "mdbxPivotButton"],
                ["mdbxSourceRail", "mdbxWorkbench"]),
            new SmokePageLayoutCheck(
                "DatabaseManagement",
                ["databaseSourceList", "databasePivotButton"],
                ["databaseSourceRail", "databaseWorkbench"])
        };

        var failures = new List<string>();
        void Check(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                AppDiagnostics.Info($"Smoke UI other page check passed. check={name}, {detail}");
                return;
            }

            failures.Add(name);
            AppDiagnostics.Info($"Smoke UI other page check failed. check={name}, {detail}");
        }

        try
        {
            var viewportReady = await WaitForSmokeWindowConditionAsync(
                () => Bounds.Width >= MinWidth && Bounds.Height >= MinHeight,
                TimeSpan.FromSeconds(3));
            Check(
                "viewport-ready",
                viewportReady,
                $"bounds={Bounds.Width:0}x{Bounds.Height:0}, min={MinWidth:0}x{MinHeight:0}");

            foreach (var page in pages)
            {
                viewModel.SelectSectionCommand.Execute(page.Section);
                await Task.Delay(80);
                Check(
                    $"{page.Section}-selected",
                    string.Equals(viewModel.SelectedSection, page.Section, StringComparison.OrdinalIgnoreCase),
                    $"selected={viewModel.SelectedSection}");

                foreach (var className in page.RequiredClasses)
                {
                    Check(
                        $"{page.Section}-class-{className}",
                        HasControlClass(className),
                        $"class={className}");
                }

                foreach (var className in page.RequiredVisibleClasses)
                {
                    Check(
                        $"{page.Section}-visible-{className}",
                        HasVisibleControlClass(className),
                        $"class={className}");
                }

                Check(
                    $"{page.Section}-no-visible-workspacePageHeader",
                    !HasVisibleControlClass("workspacePageHeader"),
                    "legacyHeader=workspacePageHeader");
            }
        }
        catch (Exception ex)
        {
            failures.Add("exception");
            AppDiagnostics.Error("Smoke UI other pages checks failed", ex);
        }

        var success = failures.Count == 0;
        AppDiagnostics.Info(
            $"Smoke UI other pages checks completed. success={success}, " +
            $"failureCount={failures.Count}, failures={string.Join(",", failures)}");
        return success;
    }

    public async Task<bool> RunSmokeUiOtherPagesScreenshotsAsync(string screenshotDirectory)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => RunSmokeUiOtherPagesScreenshotsAsync(screenshotDirectory));
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            AppDiagnostics.Info("Smoke UI other pages screenshots failed. reason=no-view-model");
            return false;
        }

        var failures = new List<string>();
        var sections = new[]
        {
            "Totp",
            "Cards",
            "Generator",
            "Archive",
            "RecycleBin",
            "Timeline",
            "Mdbx",
            "DatabaseManagement"
        };

        try
        {
            Directory.CreateDirectory(screenshotDirectory);
            foreach (var section in sections)
            {
                viewModel.SelectSectionCommand.Execute(section);
                await Task.Delay(150);

                var fileName = $"{section}_{Math.Max(1, (int)Math.Round(Bounds.Width))}x{Math.Max(1, (int)Math.Round(Bounds.Height))}.png";
                var path = Path.Combine(screenshotDirectory, fileName);
                if (!await SaveSmokeScreenshotAsync(path))
                {
                    failures.Add(section);
                    AppDiagnostics.Info($"Smoke UI screenshot failed. section={section}, path={path}");
                    continue;
                }

                AppDiagnostics.Info($"Smoke UI screenshot saved. section={section}, path={path}");
            }
        }
        catch (Exception ex)
        {
            failures.Add("exception");
            AppDiagnostics.Error("Smoke UI other pages screenshots failed", ex);
        }

        var success = failures.Count == 0;
        AppDiagnostics.Info(
            $"Smoke UI other pages screenshots completed. success={success}, " +
            $"failureCount={failures.Count}, failures={string.Join(",", failures)}, directory={screenshotDirectory}");
        return success;
    }

    private static string FormatSmokeLogValue(string? value) =>
        (value ?? "").Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private async Task<bool> SaveSmokeScreenshotAsync(string path)
    {
        var width = Math.Max(1, (int)Math.Round(Bounds.Width));
        var height = Math.Max(1, (int)Math.Round(Bounds.Height));
        if (width < 1 || height < 1)
        {
            return false;
        }

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(this);
        bitmap.Save(path);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private bool HasControlClass(string className) =>
        this.GetVisualDescendants()
            .OfType<Control>()
            .Any(control => control.Classes.Contains(className));

    private bool HasVisibleControlClass(string className) =>
        this.GetVisualDescendants()
            .OfType<Control>()
            .Any(control => control.Classes.Contains(className) && IsControlEffectivelyVisible(control));

    private static bool IsControlEffectivelyVisible(Control control)
    {
        for (var current = control as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Control currentControl && !currentControl.IsVisible)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> WaitForSmokeWindowConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }
}
