namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ReportRemoteSyncFailure(
        string diagnosticMessage,
        string userMessageKey,
        Exception exception)
    {
        AppDiagnostics.Error(diagnosticMessage, exception);
        SetStatusFailure(exception is Monica.Data.Mdbx.MdbxVaultReadOnlyException { ReasonCode: "unsupported-vault-objects" }
            ? "MdbxUnsupportedObjectsProtected"
            : userMessageKey);
    }
}
