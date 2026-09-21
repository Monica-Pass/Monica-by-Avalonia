using Monica.Data.Services;

namespace Monica.App.Services.VaultOperations;

public sealed class ChangeMasterPasswordUseCase
{
    private readonly IMasterPasswordMaintenanceService _service;

    public ChangeMasterPasswordUseCase(IMasterPasswordMaintenanceService service)
    {
        _service = service;
    }

    public async Task<ChangeMasterPasswordResult> ExecuteAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ChangeMasterPasswordAsync(currentPassword, newPassword, cancellationToken);
        if (!result.Success)
        {
            return result.FailureReason == MasterPasswordMaintenanceFailureReason.CurrentPasswordIncorrect
                ? ChangeMasterPasswordResult.IncorrectPassword()
                : ChangeMasterPasswordResult.Failed(result.Message);
        }

        return ChangeMasterPasswordResult.Succeeded(result.TotalSecretsReencrypted);
    }
}

public sealed record ChangeMasterPasswordResult
{
    public bool Success { get; init; }
    public bool IsCurrentPasswordIncorrect { get; init; }
    public int TotalSecretsReencrypted { get; init; }
    public string? FailureMessage { get; init; }

    public static ChangeMasterPasswordResult Succeeded(int totalSecretsReencrypted) =>
        new() { Success = true, TotalSecretsReencrypted = totalSecretsReencrypted };

    public static ChangeMasterPasswordResult IncorrectPassword() =>
        new() { Success = false, IsCurrentPasswordIncorrect = true };

    public static ChangeMasterPasswordResult Failed(string message) =>
        new() { Success = false, FailureMessage = message };
}
