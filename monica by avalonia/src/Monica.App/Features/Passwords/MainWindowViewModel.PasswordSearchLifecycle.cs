namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void SuspendPasswordSearchProjectionUpdates()
    {
        CancelPasswordSearchDebounce();
    }
}
