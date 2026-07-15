using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string SecurityIssueSeverityAll = "All";
    private const string SecurityIssueSeverityHigh = "High";
    private const string SecurityIssueSeverityMedium = "Medium";
    private const string SecurityIssueSeverityLow = "Low";
    private int _securityAnalysisOperationActive;
    private bool _suppressSecurityIssueFilterRefresh;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSecurityIssueSeverityFilter))]
    [NotifyPropertyChangedFor(nameof(HasSecurityIssueFilters))]
    [NotifyPropertyChangedFor(nameof(IsAllSecurityIssueSeveritySelected))]
    [NotifyPropertyChangedFor(nameof(IsHighSecurityIssueSeveritySelected))]
    [NotifyPropertyChangedFor(nameof(IsMediumSecurityIssueSeveritySelected))]
    [NotifyPropertyChangedFor(nameof(IsLowSecurityIssueSeveritySelected))]
    private string _selectedSecurityIssueSeverityFilter = SecurityIssueSeverityAll;

    public bool IsSecurityAnalysisBusy => IsCheckingCompromisedPasswords || IsRefreshingSecurityAnalysis;
    public bool HasSecurityIssueSearchText => !string.IsNullOrWhiteSpace(SecurityIssueSearchText);
    public bool HasSecurityIssueSeverityFilter =>
        !string.Equals(SelectedSecurityIssueSeverityFilter, SecurityIssueSeverityAll, StringComparison.Ordinal);
    public bool HasSecurityIssueFilters => HasSecurityIssueSearchText || HasSecurityIssueSeverityFilter;
    public bool HasFilteredSecurityIssues => FilteredSecurityIssueItems.Count > 0;
    public bool IsAllSecurityIssueSeveritySelected => IsSecurityIssueSeveritySelected(SecurityIssueSeverityAll);
    public bool IsHighSecurityIssueSeveritySelected => IsSecurityIssueSeveritySelected(SecurityIssueSeverityHigh);
    public bool IsMediumSecurityIssueSeveritySelected => IsSecurityIssueSeveritySelected(SecurityIssueSeverityMedium);
    public bool IsLowSecurityIssueSeveritySelected => IsSecurityIssueSeveritySelected(SecurityIssueSeverityLow);
    public string SecurityIssueSearchResultText => HasSecurityIssueFilters
        ? _localization.Format("SecurityIssueSearchResultFormat", FilteredSecurityIssueItems.Count, SecurityIssueItems.Count)
        : SecurityIssueCountText;

    partial void OnSecurityIssueSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSecurityIssueSearchText));
        OnPropertyChanged(nameof(HasSecurityIssueFilters));
        if (!_suppressSecurityIssueFilterRefresh)
        {
            RefreshSecurityIssueFilter();
        }
    }

    partial void OnSelectedSecurityIssueSeverityFilterChanged(string value)
    {
        if (!_suppressSecurityIssueFilterRefresh)
        {
            RefreshSecurityIssueFilter();
        }
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
    private void SelectSecurityIssueSeverityFilter(string? severity)
    {
        SelectedSecurityIssueSeverityFilter = NormalizeSecurityIssueSeverityFilter(severity);
    }

    [RelayCommand]
    private void ClearSecurityIssueFilters()
    {
        _suppressSecurityIssueFilterRefresh = true;
        try
        {
            SecurityIssueSearchText = "";
            SelectedSecurityIssueSeverityFilter = SecurityIssueSeverityAll;
        }
        finally
        {
            _suppressSecurityIssueFilterRefresh = false;
        }

        RefreshSecurityIssueFilter();
    }

    [RelayCommand]
    private void ShowSecurityIssueList()
    {
        SecurityAnalysisNarrowShowsList = true;
    }

    private void RefreshSecurityIssueFilter()
    {
        var selectedIssue = SelectedSecurityIssue;
        var query = SecurityIssueSearchText.Trim();
        IEnumerable<SecurityIssueItem> filtered = SecurityIssueItems
            .Where(MatchesSecurityIssueSeverity);
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(item => MatchesSecurityIssue(item, query));
        }

        ReplaceItems(FilteredSecurityIssueItems, filtered);

        SelectedSecurityIssue =
            FilteredSecurityIssueItems.FirstOrDefault(item => ReferenceEquals(item, selectedIssue)) ??
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

    private bool MatchesSecurityIssueSeverity(SecurityIssueItem item) =>
        SelectedSecurityIssueSeverityFilter switch
        {
            SecurityIssueSeverityHigh => item.SeverityLevel == SecurityIssueSeverityLevel.High,
            SecurityIssueSeverityMedium => item.SeverityLevel == SecurityIssueSeverityLevel.Medium,
            SecurityIssueSeverityLow => item.SeverityLevel == SecurityIssueSeverityLevel.Low,
            _ => true
        };

    private bool IsSecurityIssueSeveritySelected(string severity) =>
        string.Equals(SelectedSecurityIssueSeverityFilter, severity, StringComparison.Ordinal);

    private static string NormalizeSecurityIssueSeverityFilter(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "high" => SecurityIssueSeverityHigh,
            "medium" => SecurityIssueSeverityMedium,
            "low" => SecurityIssueSeverityLow,
            _ => SecurityIssueSeverityAll
        };

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
