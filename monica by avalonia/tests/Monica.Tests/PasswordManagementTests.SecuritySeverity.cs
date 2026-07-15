using Monica.App.ViewModels;
using Monica.Core.Models;

namespace Monica.Tests;

public sealed partial class PasswordManagementTests
{
    [Theory]
    [InlineData(SecurityIssueSeverityLevel.High, 20)]
    [InlineData(SecurityIssueSeverityLevel.Medium, 12)]
    [InlineData(SecurityIssueSeverityLevel.Low, 11)]
    public void Security_issue_severity_filter_selects_the_expected_level(
        SecurityIssueSeverityLevel severity,
        int expectedWeight)
    {
        var harness = CreateHarness();
        AddSecurityIssue(harness.ViewModel, 1, "High account", SecurityIssueSeverityLevel.High, 20);
        AddSecurityIssue(harness.ViewModel, 2, "Medium account", SecurityIssueSeverityLevel.Medium, 12);
        AddSecurityIssue(harness.ViewModel, 3, "Low account", SecurityIssueSeverityLevel.Low, 11);

        harness.ViewModel.SelectSecurityIssueSeverityFilterCommand.Execute(severity.ToString());

        var issue = Assert.Single(harness.ViewModel.FilteredSecurityIssueItems);
        Assert.Equal(expectedWeight, issue.SeverityWeight);
    }

    [Fact]
    public void Security_issue_severity_filter_composes_with_search_and_reconciles_selection()
    {
        var harness = CreateHarness();
        var highAlpha = AddSecurityIssue(harness.ViewModel, 1, "Alpha account", SecurityIssueSeverityLevel.High, 40);
        var highBeta = AddSecurityIssue(harness.ViewModel, 2, "Beta account", SecurityIssueSeverityLevel.High, 30);
        AddSecurityIssue(harness.ViewModel, 3, "Alpha service", SecurityIssueSeverityLevel.Medium, 18);
        AddSecurityIssue(harness.ViewModel, 4, "Older account", SecurityIssueSeverityLevel.Low, 8);
        harness.ViewModel.SelectedSecurityIssue = highBeta;

        harness.ViewModel.SelectSecurityIssueSeverityFilterCommand.Execute("High");

        Assert.Equal("High", harness.ViewModel.SelectedSecurityIssueSeverityFilter);
        Assert.True(harness.ViewModel.HasSecurityIssueSeverityFilter);
        Assert.Equal([highAlpha, highBeta], harness.ViewModel.FilteredSecurityIssueItems);

        harness.ViewModel.SecurityIssueSearchText = "Alpha";

        Assert.Equal([highAlpha], harness.ViewModel.FilteredSecurityIssueItems);
        Assert.Same(highAlpha, harness.ViewModel.SelectedSecurityIssue);
        Assert.Equal(
            harness.ViewModel.L.Format("SecurityIssueSearchResultFormat", 1, 4),
            harness.ViewModel.SecurityIssueSearchResultText);
    }

    [Fact]
    public void Security_issue_severity_clear_restores_ordered_issues_and_filter_state()
    {
        var harness = CreateHarness();
        var high = AddSecurityIssue(harness.ViewModel, 1, "High account", SecurityIssueSeverityLevel.High, 40);
        var medium = AddSecurityIssue(harness.ViewModel, 2, "Medium account", SecurityIssueSeverityLevel.Medium, 18);
        var low = AddSecurityIssue(harness.ViewModel, 3, "Low account", SecurityIssueSeverityLevel.Low, 8);
        harness.ViewModel.SelectSecurityIssueSeverityFilterCommand.Execute("Medium");
        harness.ViewModel.SecurityIssueSearchText = "Medium";

        harness.ViewModel.ClearSecurityIssueFiltersCommand.Execute(null);

        Assert.Equal("All", harness.ViewModel.SelectedSecurityIssueSeverityFilter);
        Assert.False(harness.ViewModel.HasSecurityIssueSeverityFilter);
        Assert.False(harness.ViewModel.HasSecurityIssueFilters);
        Assert.Equal("", harness.ViewModel.SecurityIssueSearchText);
        Assert.Equal([high, medium, low], harness.ViewModel.FilteredSecurityIssueItems);
        Assert.Same(medium, harness.ViewModel.SelectedSecurityIssue);
        Assert.Equal(harness.ViewModel.SecurityIssueCountText, harness.ViewModel.SecurityIssueSearchResultText);
    }

    private static SecurityIssueItem AddSecurityIssue(
        MainWindowViewModel viewModel,
        long id,
        string title,
        SecurityIssueSeverityLevel severity,
        int severityWeight)
    {
        var entry = new PasswordEntry { Id = id, Title = title };
        var issue = new SecurityIssueItem(
            title,
            $"{severity} finding",
            "Test category",
            severity.ToString(),
            severity,
            id,
            entry,
            severityWeight);
        viewModel.SecurityIssueItems.Add(issue);
        return issue;
    }
}
