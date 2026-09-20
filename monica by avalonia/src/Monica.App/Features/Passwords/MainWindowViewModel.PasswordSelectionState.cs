using System.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void RaisePasswordSelectionState()
    {
        OnPropertyChanged(nameof(SelectedPasswordCount));
        OnPropertyChanged(nameof(HasSelectedPasswords));
        OnPropertyChanged(nameof(CanStackSelectedPasswords));
        RaiseArchiveSelectionState();
        RaiseRecycleBinSelectionState();
    }

    private void RefreshPasswordSelectionStateFromPasswords()
    {
        _selectedPasswordCount = Passwords.Count(item => item.IsSelected);
        RaisePasswordSelectionState();
    }

    private void UpdatePasswordSelectionsInBatch(Action updateSelections)
    {
        var wasSuppressed = _suppressPasswordSelectionStateNotifications;
        _suppressPasswordSelectionStateNotifications = true;
        try
        {
            updateSelections();
        }
        finally
        {
            _suppressPasswordSelectionStateNotifications = wasSuppressed;
        }

        if (!wasSuppressed)
        {
            RefreshPasswordSelectionStateFromPasswords();
        }
    }

    private void ReconcileSelectedPasswordDetails()
    {
        ReconcileSelectedPasswordDetailsImmediately();
    }

    private void ReconcileSelectedPasswordDetailsImmediately()
    {
        if (SelectedPassword is not null && !Passwords.Contains(SelectedPassword))
        {
            SelectedPassword = null;
        }
    }

    private void TrackPasswordSelection(PasswordEntry entry)
    {
        entry.PropertyChanged -= PasswordEntryPropertyChanged;
        entry.PropertyChanged += PasswordEntryPropertyChanged;
    }

    private void PasswordEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PasswordEntry.IsSelected))
        {
            if (_suppressPasswordSelectionStateNotifications)
            {
                return;
            }

            if (sender is PasswordEntry entry)
            {
                if (Passwords.Contains(entry))
                {
                    var delta = entry.IsSelected ? 1 : -1;
                    _selectedPasswordCount = Math.Clamp(_selectedPasswordCount + delta, 0, Passwords.Count);
                }
                else
                {
                    _selectedPasswordCount = Passwords.Count(item => item.IsSelected);
                }
            }

            RaisePasswordSelectionState();
        }
    }
}
