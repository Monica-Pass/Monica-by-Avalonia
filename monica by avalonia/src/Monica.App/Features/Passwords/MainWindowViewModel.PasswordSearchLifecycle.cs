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

        SetPasswordSearchImmediately(PasswordSearchText);
    }
}
