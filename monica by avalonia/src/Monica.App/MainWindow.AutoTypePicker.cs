using Monica.App.Services;
using Monica.App.ViewModels;

namespace Monica.App;

// The auto-type list is a window of its own because it has to sit on top of the application the user
// is filling in and take the keyboard for exactly one choice. Which entry is headed where is the
// ViewModel's business; this partial only owns the window and the hand-back it has to make.
public partial class MainWindow
{
    private AutoTypePickerWindow? _autoTypePickerWindow;

    internal bool IsAutoTypePickerVisible => _autoTypePickerWindow?.IsVisible == true;

    internal AutoTypePickerWindow? ActiveAutoTypePicker => _autoTypePickerWindow;

    /// <summary>
    /// Draws the list the ViewModel asked for. Called right after the hotkey press decides that the
    /// foreground window did not name exactly one entry, so the candidates and the heading are read
    /// from the ViewModel rather than passed in.
    /// </summary>
    internal void ShowAutoTypePicker()
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsAutoTypePickerOpen)
        {
            return;
        }

        CloseAutoTypeWindow("ReplacedByFreshList");
        var picker = new AutoTypePickerWindow(
            viewModel.AutoTypePickerCandidates,
            viewModel.AutoTypePickerHeadingText,
            viewModel.AutoTypePickerFilterPlaceholderText,
            viewModel.AutoTypePickerHintText);
        picker.EntryPicked += OnAutoTypePickerEntryPicked;
        picker.DismissRequested += OnAutoTypePickerDismissed;
        _autoTypePickerWindow = picker;
        AppDiagnostics.Info($"Auto-type picker surfaced with {viewModel.AutoTypePickerCandidates.Count} rows.");
        picker.Show();
        picker.FocusForMode(viewModel.AutoTypePickerListsAllEntries);
    }

    /// <summary>
    /// Takes the list down for any reason that is not the user choosing a row: the vault locked, a
    /// second hotkey press meaning "forget it", or the main window going away.
    /// </summary>
    internal void CloseAutoTypePicker(string reason)
    {
        CloseAutoTypeWindow(reason);
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CancelAutoTypePicker();
        }
    }

    private void OnAutoTypePickerEntryPicked(AutoTypeCandidate candidate)
    {
        // Close first: the ViewModel hands the keyboard to the target window next, and that has to
        // happen with this list out of the way or the keystrokes land on our own window.
        CloseAutoTypeWindow("EntryPicked");
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteAutoTypeFromPicker(candidate);
        }
    }

    private void OnAutoTypePickerDismissed()
    {
        CloseAutoTypeWindow("DismissKey");
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CancelAutoTypePicker();
        }
    }

    private void CloseAutoTypeWindow(string reason)
    {
        if (_autoTypePickerWindow is not { } picker)
        {
            return;
        }

        _autoTypePickerWindow = null;
        picker.EntryPicked -= OnAutoTypePickerEntryPicked;
        picker.DismissRequested -= OnAutoTypePickerDismissed;
        AppDiagnostics.Info($"Auto-type picker taken down. reason={reason}");
        picker.Close();
    }
}
