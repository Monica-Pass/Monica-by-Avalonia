using Monica.App.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static void RecordImportExportFailure(string diagnosticMessage, Exception exception) =>
        AppDiagnostics.Error(diagnosticMessage, exception);

    private void ReportImportExportFailure(
        string diagnosticMessage,
        string userMessageKey,
        Exception exception)
    {
        RecordImportExportFailure(diagnosticMessage, exception);
        StatusMessage = _localization.Get(userMessageKey);
    }

    private string GetPasswordSecretUnavailableMessage(PasswordSecretUnavailableException error) =>
        _localization.Get(error.Reason switch
        {
            PasswordSecretUnavailableReason.VaultLocked => "VaultLocked",
            _ => "PasswordSecretUnavailable"
        });
}
