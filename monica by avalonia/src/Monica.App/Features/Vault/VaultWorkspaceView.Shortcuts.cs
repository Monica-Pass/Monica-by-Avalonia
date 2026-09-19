using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Monica.App.Features.Notes;
using Monica.App.ViewModels;

namespace Monica.App.Features.Vault;

// The library replaced four pages, so it also answers the keys those pages answered. What a row
// means decides which command runs; the tree itself only owns folder rename and native navigation.
public partial class VaultWorkspaceView
{
    internal NoteEditorView? NoteSurface =>
        _surfaces.TryGetValue(VaultSurface.Note, out var surface) ? surface as NoteEditorView : null;

    internal bool IsSearchFocused => VaultSearchBox.IsFocused;

    internal bool IsTreeFocused => VaultTree.IsTreeFocused;

    internal void FocusSearch() => VaultSearchBox.Focus();

    internal void HandleSelectedNoteTabChanged()
    {
        if (NoteSurface is not { } editor)
        {
            return;
        }

        editor.RestoreSelectedTabSelection();
        editor.EnsureSelectedHistory();
    }

    internal bool TryHandleShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        {
            VaultSearchBox.Focus();
            e.Handled = true;
            return true;
        }

        if (NoteSurface is { } note && note.TryHandleWorkspaceShortcut(e))
        {
            return true;
        }

        if (NoteSurface is not null && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.S)
            {
                e.Handled = true;
                SaveNoteSurface(viewModel, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                return true;
            }

            if (e.Key == Key.W && viewModel.SelectedNoteTab is { } tab)
            {
                e.Handled = true;
                CloseNoteTabWithPrompt(viewModel, tab);
                return true;
            }
        }

        if (e.Key == Key.Escape)
        {
            return TryHandleEscape(viewModel, e);
        }

        if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt && viewModel.SelectedVaultRow is not null)
        {
            CloseVaultSelection(viewModel);
            e.Handled = true;
            return true;
        }

        if (IsTypingOutsideSearch(e.Source))
        {
            return false;
        }

        return TryHandleRowShortcut(viewModel, e);
    }

    // Escape unwinds one layer at a time so a single key never loses both the filter and the row.
    private bool TryHandleEscape(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (viewModel.HasVaultSearchText)
        {
            if (!viewModel.ClearVaultSearchCommand.CanExecute(null))
            {
                return false;
            }

            viewModel.ClearVaultSearchCommand.Execute(null);
        }
        else if (viewModel.SelectedVaultRow is not null)
        {
            CloseVaultSelection(viewModel);
        }
        else if (viewModel.VaultFavoritesOnly)
        {
            viewModel.ToggleVaultFavoritesCommand.Execute(null);
        }
        else
        {
            return false;
        }

        e.Handled = true;
        return true;
    }

    private bool TryHandleRowShortcut(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (e.Key == Key.C &&
            e.KeyModifiers is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift) &&
            viewModel.SelectedVaultRow is VaultTreeEntryRow row)
        {
            return TryCopySelected(viewModel, row, e, copySecret: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }

        if (e.KeyModifiers != KeyModifiers.None)
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.F2 when viewModel.SelectedVaultRow is VaultTreeEntryRow:
                return ExecuteOnSelection(viewModel.EditSelectedVaultEntryCommand, viewModel, e);

            case Key.Delete when viewModel.SelectedVaultRow is not null:
                return ExecuteOnSelection(
                    viewModel.SelectedVaultRow is VaultTreeFolderRow
                        ? viewModel.DeleteSelectedVaultFolderCommand
                        : viewModel.DeleteSelectedVaultEntryCommand,
                    viewModel,
                    e);

            case Key.Up:
                SelectAdjacentEntry(viewModel, -1);
                e.Handled = true;
                return true;

            case Key.Down:
                SelectAdjacentEntry(viewModel, 1);
                e.Handled = true;
                return true;

            default:
                return false;
        }
    }

    private static bool TryCopySelected(
        MainWindowViewModel viewModel,
        VaultTreeEntryRow row,
        KeyEventArgs e,
        bool copySecret)
    {
        ICommand command;
        object? parameter;

        if (row.Password is { } password)
        {
            command = copySecret ? viewModel.CopyPasswordCommand : viewModel.CopyUsernameCommand;
            parameter = password;
        }
        else if (row is { Kind: VaultEntryKind.Totp, Item: { } totp })
        {
            command = viewModel.CopyTotpCommand;
            parameter = totp;
        }
        else if (row is { Item: not null, Kind: VaultEntryKind.BankCard or VaultEntryKind.Document or
                VaultEntryKind.BillingAddress or VaultEntryKind.PaymentAccount })
        {
            command = viewModel.CopySelectedWalletPrimaryFieldCommand;
            parameter = null;
        }
        else
        {
            return false;
        }

        if (!command.CanExecute(parameter))
        {
            return false;
        }

        command.Execute(parameter);
        e.Handled = true;
        return true;
    }

    private static bool ExecuteOnSelection(ICommand command, MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        e.Handled = true;
        return true;
    }

    internal void SelectAdjacentEntry(MainWindowViewModel viewModel, int delta)
    {
        var rows = viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>().ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var selectedIndex = viewModel.SelectedVaultRow is { } selected
            ? rows.FindIndex(row => string.Equals(row.Key, selected.Key, StringComparison.Ordinal))
            : -1;
        var nextIndex = selectedIndex < 0
            ? delta > 0 ? 0 : rows.Count - 1
            : Math.Clamp(selectedIndex + delta, 0, rows.Count - 1);

        viewModel.SelectedVaultRow = rows[nextIndex];
        var target = rows[nextIndex];
        Dispatcher.UIThread.Post(() => VaultTree.ScrollIntoView(target));
    }

    private void CloseVaultSelection(MainWindowViewModel viewModel)
    {
        if (viewModel.SelectedVaultSurface == VaultSurface.Password)
        {
            viewModel.CloseSelectedPasswordDetailsCommand.Execute(null);
        }

        viewModel.SelectedVaultRow = null;
        VaultTree.FocusTree();
    }

    private void SaveNoteSurface(MainWindowViewModel viewModel, bool saveAll)
    {
        if (saveAll)
        {
            SaveAllNotes(viewModel);
            return;
        }

        SaveCurrentNote(viewModel);
    }

    private async void SaveCurrentNote(MainWindowViewModel viewModel) =>
        await viewModel.SaveNoteCommand.ExecuteAsync(null);

    private async void SaveAllNotes(MainWindowViewModel viewModel) =>
        await viewModel.SaveAllNoteTabsCommand.ExecuteAsync(null);

    private async void CloseNoteTabWithPrompt(MainWindowViewModel viewModel, NoteEditorTab tab) =>
        await CloseTabWithPromptAsync(viewModel, tab);

    internal async Task CloseTabWithPromptAsync(MainWindowViewModel viewModel, NoteEditorTab tab)
    {
        if (!tab.IsDirty)
        {
            CloseNoteTab(viewModel, tab);
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            return;
        }

        var result = await NoteClosePrompts.ShowUnsavedTabAsync(owner, viewModel, tab);
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
        var wasSelected = ReferenceEquals(viewModel.SelectedNoteTab, tab);
        viewModel.CloseNoteTabCommand.Execute(tab);
        if (wasSelected)
        {
            viewModel.SelectedVaultRow = null;
        }
    }

    private bool IsTypingOutsideSearch(object? source) =>
        source is TextBox box && !ReferenceEquals(box, VaultSearchBox);
}
