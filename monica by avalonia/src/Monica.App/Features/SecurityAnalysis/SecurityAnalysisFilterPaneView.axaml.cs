using Avalonia.Controls;

namespace Monica.App.Features.SecurityAnalysis;

public partial class SecurityAnalysisFilterPaneView : UserControl
{
    public SecurityAnalysisFilterPaneView()
    {
        InitializeComponent();
    }

    public TextBox SearchBox => SecurityIssueSearchBox;
}
