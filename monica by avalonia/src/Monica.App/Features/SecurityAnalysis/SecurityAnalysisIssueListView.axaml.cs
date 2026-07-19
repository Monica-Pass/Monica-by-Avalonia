using Avalonia.Controls;

namespace Monica.App.Features.SecurityAnalysis;

public partial class SecurityAnalysisIssueListView : UserControl
{
    public SecurityAnalysisIssueListView()
    {
        InitializeComponent();
    }

    public ListBox IssueList => SecurityIssueList;
}
