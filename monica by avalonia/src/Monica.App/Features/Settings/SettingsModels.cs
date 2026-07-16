using Monica.Data.Services;

namespace Monica.App.ViewModels;

public sealed record SettingsChoice(object Value, string Label);

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
