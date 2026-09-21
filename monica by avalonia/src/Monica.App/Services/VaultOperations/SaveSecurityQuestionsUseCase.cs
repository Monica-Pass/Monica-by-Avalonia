using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.Services.VaultOperations;

public sealed class SaveSecurityQuestionsUseCase
{
    private readonly SecurityQuestionService _securityQuestionService;

    public SaveSecurityQuestionsUseCase(SecurityQuestionService securityQuestionService)
    {
        _securityQuestionService = securityQuestionService;
    }

    public Task<SecurityRecoverySettings> ExecuteAsync(
        SecurityQuestionDraft question1,
        SecurityQuestionDraft question2,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => _securityQuestionService.CreateSetup(question1, question2), cancellationToken);
    }
}
