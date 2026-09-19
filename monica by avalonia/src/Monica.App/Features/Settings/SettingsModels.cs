using System.ComponentModel;
using Monica.Data.Services;

namespace Monica.App.ViewModels;

/// <summary>
/// Label stays mutable so a language switch can relabel an existing option list in place; replacing
/// the items would drop the ComboBox selection and write a null value back into the persisted setting.
/// </summary>
public sealed record SettingsChoice(object Value) : INotifyPropertyChanged
{
    private string _label = "";

    public SettingsChoice(object value, string label) : this(value) => _label = label;

    public string Label
    {
        get => _label;
        set
        {
            if (_label == value)
            {
                return;
            }

            _label = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class DisabledMasterPasswordMaintenanceService : IMasterPasswordMaintenanceService
{
    public Task<MasterPasswordMaintenanceResult> ChangeMasterPasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(MasterPasswordMaintenanceResult.Failure(
            "Master password maintenance is not available.",
            MasterPasswordMaintenanceFailureReason.Unavailable));

    public Task<MasterPasswordMaintenanceResult> ResetMasterPasswordFromUnlockedVaultAsync(
        string newPassword,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(MasterPasswordMaintenanceResult.Failure(
            "Master password maintenance is not available.",
            MasterPasswordMaintenanceFailureReason.Unavailable));
}
