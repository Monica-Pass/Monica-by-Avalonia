namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void RaiseFilteredPasswordsChanged()
    {
        _filteredPasswordsDirty = true;
        _filteredPasswordRowsDirty = true;
        if (_passwordProjectionNotificationDeferralDepth > 0)
        {
            _filteredPasswordsNotificationPending = true;
            _filteredPasswordRowsNotificationPending = true;
            return;
        }

        PublishFilteredPasswordsChanged();
    }

    private void PublishFilteredPasswordsChanged()
    {
        OnPropertyChanged(nameof(FilteredPasswords));
        OnPropertyChanged(nameof(FilteredPasswordRows));
        OnPropertyChanged(nameof(VisiblePasswordNavigationEntries));
        SyncSelectedPasswordListRow(SelectedPassword);
    }

    private void RaiseFilteredPasswordRowsChanged()
    {
        _filteredPasswordRowsDirty = true;
        if (_passwordProjectionNotificationDeferralDepth > 0)
        {
            _filteredPasswordRowsNotificationPending = true;
            return;
        }

        PublishFilteredPasswordRowsChanged();
    }

    private void PublishFilteredPasswordRowsChanged()
    {
        OnPropertyChanged(nameof(FilteredPasswordRows));
        OnPropertyChanged(nameof(VisiblePasswordNavigationEntries));
        SyncSelectedPasswordListRow(SelectedPassword);
    }

    private void BeginPasswordProjectionNotificationDeferral()
    {
        _passwordProjectionNotificationDeferralDepth++;
    }

    private void EndPasswordProjectionNotificationDeferral()
    {
        if (_passwordProjectionNotificationDeferralDepth == 0)
        {
            return;
        }

        if (_passwordProjectionNotificationDeferralDepth == 1 && _passwordSelectionReconciliationPending)
        {
            _passwordSelectionReconciliationPending = false;
            ReconcileSelectedPasswordDetailsImmediately();
        }

        _passwordProjectionNotificationDeferralDepth--;
        if (_passwordProjectionNotificationDeferralDepth > 0)
        {
            return;
        }

        if (_filteredPasswordsNotificationPending)
        {
            _filteredPasswordsNotificationPending = false;
            _filteredPasswordRowsNotificationPending = false;
            PublishFilteredPasswordsChanged();
            return;
        }

        if (_filteredPasswordRowsNotificationPending)
        {
            _filteredPasswordRowsNotificationPending = false;
            PublishFilteredPasswordRowsChanged();
        }
    }
}
