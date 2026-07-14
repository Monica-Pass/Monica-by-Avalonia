using Avalonia.Controls;
using Avalonia.Input;
using FluentAvalonia.UI.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public partial class NoteWorkspaceView : UserControl
{
    public NoteWorkspaceView()
    {
        InitializeComponent();
    }

    public bool TryHandleShortcut(KeyEventArgs e) => NoteEditorRegion.TryHandleWorkspaceShortcut(e);

    public Task CloseTabWithPromptAsync(MainWindowViewModel viewModel, NoteEditorTab tab) =>
        TabStrip.CloseTabWithPromptAsync(viewModel, tab);

    public Task<FAContentDialogResult> ShowUnsavedTabsDialogAsync(int dirtyCount) =>
        TabStrip.ShowUnsavedTabsDialogAsync(dirtyCount);

    public void UpdateTabScroll() => TabStrip.UpdateScrollButtons();

    public void HandleSelectedTabChanged()
    {
        NoteEditorRegion.RestoreSelectedTabSelection();
        NoteEditorRegion.EnsureSelectedHistory();
        TabStrip.ScrollSelectedIntoView();
        TabStrip.UpdateScrollButtons();
    }

    public void HandleTabWidthChanged()
    {
        TabStrip.ScrollSelectedIntoView();
        TabStrip.UpdateScrollButtons();
    }

    public void FocusNoteTree() => NoteTreeRegion.FocusSearch();

    public Task<bool> RunSmokeChecksAsync(MainWindowViewModel viewModel) => NoteEditorRegion.RunSmokeChecksAsync(viewModel);

    public Task RunKeyboardSmokeChecksAsync(Action<string, bool, string> check) => NoteEditorRegion.RunKeyboardSmokeChecksAsync(check);

    private void WorkspaceGrid_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NoteWorkspaceViewportWidth = e.NewSize.Width;
        }
    }

    private async void Editor_OnCloseRequested(object? sender, NoteEditorCloseRequestedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await TabStrip.CloseTabWithPromptAsync(viewModel, e.Tab);
        }
    }

    private void Inspector_OnLineRequested(object? sender, NoteLineRequestedEventArgs e) => NoteEditorRegion.JumpToLine(e.LineNumber);

    private void TabStrip_OnTabClosed(object? sender, NoteTabClosedEventArgs e) => NoteEditorRegion.RemoveHistory(e.Tab);
}
