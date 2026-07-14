namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private int _securityMaintenanceOperationActive;

    private bool TryBeginSecurityMaintenance(Action setBusy)
    {
        if (Interlocked.CompareExchange(ref _securityMaintenanceOperationActive, 1, 0) != 0)
        {
            StatusMessage = _localization.Get("SecurityMaintenanceInProgress");
            return false;
        }

        setBusy();
        return true;
    }

    private void EndSecurityMaintenance(Action clearBusy)
    {
        Interlocked.Exchange(ref _securityMaintenanceOperationActive, 0);
        clearBusy();
    }

    private void RaiseSecurityMaintenanceState()
    {
        OnPropertyChanged(nameof(IsSecurityMaintenanceBusy));
        OnPropertyChanged(nameof(CanRunResetMasterPassword));
        OnPropertyChanged(nameof(CanChangeMasterPassword));
        OnPropertyChanged(nameof(CanClearVaultData));
    }
}
