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
        SetStatusFailure(userMessageKey);
    }

    private static string PasswordSecretUnavailableKey(PasswordSecretUnavailableException error) =>
        error.Reason switch
        {
            PasswordSecretUnavailableReason.VaultLocked => "VaultLocked",
            _ => "PasswordSecretUnavailable"
        };
}
