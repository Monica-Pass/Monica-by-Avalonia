namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ReportSettingsFailure(
        string diagnosticMessage,
        string userMessageKey,
        Exception exception)
    {
        AppDiagnostics.Error(diagnosticMessage, exception);
        SetStatusFailure(exception is Monica.Data.Mdbx.MdbxVaultReadOnlyException { ReasonCode: "unsupported-vault-objects" }
            ? "MdbxUnsupportedObjectsProtected"
            : userMessageKey);
    }

    private void ReportSettingsFailure(
        string diagnosticMessage,
        string userMessageKey,
        string diagnosticDetail)
    {
        ReportSettingsFailure(
            diagnosticMessage,
            userMessageKey,
            new InvalidOperationException(diagnosticDetail));
    }
}
