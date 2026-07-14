using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Monica.App.Features.Notes;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public partial class NoteEditorView
{
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
                RequestClose(noteViewModel.SelectedNoteTab);
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

}
