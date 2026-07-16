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
            !string.Equals(SelectedSection, "Passwords", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(PasswordSearchQuery, PasswordSearchText, StringComparison.Ordinal))
        {
            return;
        }

        // Restore the inexpensive in-memory projection immediately so returning from the
        // background never waits on MDBX custom-field I/O. The debounced query below only
        // enriches the already-visible results with custom-field matches.
        PasswordSearchQuery = PasswordSearchText;
        QueuePasswordSearchQuery(PasswordSearchText);
    }
}
