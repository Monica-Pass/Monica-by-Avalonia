using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
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
        Dispatcher.UIThread.Post(UpdateNoteTabScrollButtons);
    }

    private void NoteTabsScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs e) =>
        UpdateNoteTabScrollButtons();

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
        UpdateNoteTabScrollButtons();
    }

    private void UpdateNoteTabScrollButtons()
    {
        var viewportWidth = NoteTabsScrollViewer.Viewport.Width;
        var extentWidth = NoteTabsScrollViewer.Extent.Width;
        if (viewportWidth <= 0 ||
            extentWidth <= 0 ||
            double.IsNaN(viewportWidth) ||
            double.IsNaN(extentWidth))
        {
            PreviousNoteTabButton.IsEnabled = false;
            NextNoteTabButton.IsEnabled = false;
            return;
        }

        var maxOffset = Math.Max(0, extentWidth - viewportWidth);
        var offset = Math.Clamp(NoteTabsScrollViewer.Offset.X, 0, maxOffset);
        PreviousNoteTabButton.IsEnabled = offset > 0.5;
        NextNoteTabButton.IsEnabled = offset < maxOffset - 0.5;
    }

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
        NoteEditorView.RemoveHistory(tab);
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
}
