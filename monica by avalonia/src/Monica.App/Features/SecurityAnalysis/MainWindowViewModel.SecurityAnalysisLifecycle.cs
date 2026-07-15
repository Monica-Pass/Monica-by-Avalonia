namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isSecurityAnalysisRefreshResumeQueued;

    private void InvalidateSecurityAnalysis()
    {
        _isSecurityAnalysisDirty = true;
        RefreshSecurityAnalysisIfNeeded();
    }

    private void RefreshSecurityAnalysisIfNeeded()
    {
        if (_isUnlockedShellHibernated ||
            !_isSecurityAnalysisDirty ||
            !string.Equals(SelectedSection, "SecurityAnalysis", StringComparison.Ordinal))
        {
            return;
        }

        if (IsSecurityAnalysisBusy)
        {
            QueueSecurityAnalysisRefreshAfterCurrentOperation();
            return;
        }

        if (Passwords.Count >= 500)
        {
            _ = RefreshSecurityAnalysisCommand.ExecuteAsync(null);
        }
        else
        {
            AppDiagnostics.Measure("Refresh visible security analysis", RefreshSecurityAnalysis);
        }
    }

    private void SuspendSecurityAnalysis()
    {
        CheckCompromisedPasswordsCancelCommand.Execute(null);
        RefreshSecurityAnalysisCancelCommand.Execute(null);
    }

    private void QueueSecurityAnalysisRefreshAfterCurrentOperation()
    {
        if (_isSecurityAnalysisRefreshResumeQueued)
        {
            return;
        }

        _isSecurityAnalysisRefreshResumeQueued = true;
        _ = ResumeSecurityAnalysisRefreshAsync();
    }

    private async Task ResumeSecurityAnalysisRefreshAsync()
    {
        try
        {
            var operation = CheckCompromisedPasswordsCommand.IsRunning
                ? CheckCompromisedPasswordsCommand.ExecutionTask
                : RefreshSecurityAnalysisCommand.ExecutionTask;
            if (operation is not null)
            {
                await operation;
            }
            else
            {
                await Task.Yield();
            }
        }
        finally
        {
            _isSecurityAnalysisRefreshResumeQueued = false;
        }

        RefreshSecurityAnalysisIfNeeded();
    }
}
