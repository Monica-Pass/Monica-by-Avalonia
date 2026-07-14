using CommunityToolkit.Mvvm.Input;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private int _securityAnalysisOperationActive;

    public bool IsSecurityAnalysisBusy => IsCheckingCompromisedPasswords || IsRefreshingSecurityAnalysis;
    public bool HasSecurityIssueSearchText => !string.IsNullOrWhiteSpace(SecurityIssueSearchText);
    public bool HasFilteredSecurityIssues => FilteredSecurityIssueItems.Count > 0;
    public string SecurityIssueSearchResultText => HasSecurityIssueSearchText
        ? _localization.Format("SecurityIssueSearchResultFormat", FilteredSecurityIssueItems.Count, SecurityIssueItems.Count)
        : SecurityIssueCountText;

    partial void OnSecurityIssueSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSecurityIssueSearchText));
        RefreshSecurityIssueFilter();
    }

    partial void OnIsCheckingCompromisedPasswordsChanged(bool value) =>
        OnPropertyChanged(nameof(IsSecurityAnalysisBusy));

    partial void OnIsRefreshingSecurityAnalysisChanged(bool value) =>
        OnPropertyChanged(nameof(IsSecurityAnalysisBusy));

    [RelayCommand]
    private void ClearSecurityIssueSearch()
    {
        SecurityIssueSearchText = "";
    }

    [RelayCommand]
    private void ShowSecurityIssueList()
    {
        SecurityAnalysisNarrowShowsList = true;
    }

    private void RefreshSecurityIssueFilter()
    {
        var selectedPasswordId = SelectedSecurityIssue?.PasswordId;
        var query = SecurityIssueSearchText.Trim();
        IEnumerable<SecurityIssueItem> filtered = string.IsNullOrEmpty(query)
            ? SecurityIssueItems
            : SecurityIssueItems.Where(item => MatchesSecurityIssue(item, query));

        ReplaceItems(FilteredSecurityIssueItems, filtered);

        SelectedSecurityIssue =
            FilteredSecurityIssueItems.FirstOrDefault(item => item.PasswordId == selectedPasswordId) ??
            FilteredSecurityIssueItems.FirstOrDefault();
        if (SelectedSecurityIssue is null)
        {
            SecurityAnalysisNarrowShowsList = true;
        }

        OnPropertyChanged(nameof(HasFilteredSecurityIssues));
        OnPropertyChanged(nameof(SecurityIssueSearchResultText));
    }

    private static bool MatchesSecurityIssue(SecurityIssueItem item, string query) =>
        item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        item.Subtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        item.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        item.Severity.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private bool TryBeginSecurityAnalysisOperation(bool compromisedCheck)
    {
        if (Interlocked.CompareExchange(ref _securityAnalysisOperationActive, 1, 0) != 0)
        {
            return false;
        }

        if (compromisedCheck)
        {
            IsCheckingCompromisedPasswords = true;
        }
        else
        {
            IsRefreshingSecurityAnalysis = true;
        }

        return true;
    }

    private void EndSecurityAnalysisOperation(bool compromisedCheck)
    {
        Interlocked.Exchange(ref _securityAnalysisOperationActive, 0);
        if (compromisedCheck)
        {
            IsCheckingCompromisedPasswords = false;
        }
        else
        {
            IsRefreshingSecurityAnalysis = false;
        }
    }
}
