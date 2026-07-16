namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ReportSettingsFailure(
        string diagnosticMessage,
        string userMessageKey,
        Exception exception)
    {
        AppDiagnostics.Error(diagnosticMessage, exception);
        StatusMessage = _localization.Get(userMessageKey);
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
