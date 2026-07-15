namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isUnlockedShellHibernated;

    internal void SetUnlockedShellHibernated(bool isHibernated)
    {
        if (_isUnlockedShellHibernated == isHibernated)
        {
            return;
        }

        _isUnlockedShellHibernated = isHibernated;
        OnPropertyChanged(nameof(UnlockedShellContent));
    }
}
