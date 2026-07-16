namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void SuspendPasswordSearchProjectionUpdates()
    {
        CancelPasswordSearchDebounce();
    }

    private void RestorePasswordSearchQueryIfActive()
    {
        if (_isUnlockedShellHibernated ||
            !IsUnlocked ||
            !string.Equals(SelectedSection, "Passwords", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Restore the inexpensive in-memory projection immediately so returning from the
        // background never waits on MDBX metadata I/O. The debounced query below only
        // enriches the already-visible results with custom-field and attachment matches.
        if (string.Equals(PasswordSearchQuery, PasswordSearchText, StringComparison.Ordinal))
        {
            RaiseFilteredPasswordsChanged();
        }
        else
        {
            PasswordSearchQuery = PasswordSearchText;
        }

        QueuePasswordSearchQuery(PasswordSearchText);
    }
}
