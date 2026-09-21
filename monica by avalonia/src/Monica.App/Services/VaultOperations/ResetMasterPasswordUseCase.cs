using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data.Services;

namespace Monica.App.Services.VaultOperations;

public sealed class ResetMasterPasswordUseCase
{
    private readonly SecurityQuestionService _securityQuestionService;
    private readonly IMasterPasswordMaintenanceService _masterPasswordService;

    public ResetMasterPasswordUseCase(
        SecurityQuestionService securityQuestionService,
        IMasterPasswordMaintenanceService masterPasswordService)
    {
        _securityQuestionService = securityQuestionService;
        _masterPasswordService = masterPasswordService;
    }

    public async Task<ResetMasterPasswordResult> ExecuteAsync(
        SecurityRecoverySettings recovery,
        string answer1,
        string answer2,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var answersValid = await Task.Run(() =>
            _securityQuestionService.VerifyAnswer(answer1, recovery.Question1AnswerHash, recovery.Question1AnswerSalt) &&
            _securityQuestionService.VerifyAnswer(answer2, recovery.Question2AnswerHash, recovery.Question2AnswerSalt));

        if (!answersValid)
        {
            return ResetMasterPasswordResult.AnswersIncorrect();
        }

        var result = await _masterPasswordService.ResetMasterPasswordFromUnlockedVaultAsync(newPassword, cancellationToken);
        if (!result.Success)
        {
            return ResetMasterPasswordResult.Failed(result.Message);
        }

        return ResetMasterPasswordResult.Succeeded(result.TotalSecretsReencrypted);
    }
}

public sealed record ResetMasterPasswordResult
{
    public bool Success { get; init; }
    public bool IsAnswersIncorrect { get; init; }
    public int TotalSecretsReencrypted { get; init; }
    public string? FailureMessage { get; init; }

    public static ResetMasterPasswordResult Succeeded(int totalSecretsReencrypted) =>
        new() { Success = true, TotalSecretsReencrypted = totalSecretsReencrypted };

    public static ResetMasterPasswordResult AnswersIncorrect() =>
        new() { Success = false, IsAnswersIncorrect = true };

    public static ResetMasterPasswordResult Failed(string message) =>
        new() { Success = false, FailureMessage = message };
}
