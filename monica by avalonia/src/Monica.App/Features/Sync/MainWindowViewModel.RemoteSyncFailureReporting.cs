namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ReportRemoteSyncFailure(
        string diagnosticMessage,
        string userMessageKey,
        Exception exception)
    {
        AppDiagnostics.Error(diagnosticMessage, exception);
        SetStatusFailure(userMessageKey);
    }
}
