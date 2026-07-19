using Avalonia.Controls;

namespace Monica.App.Features.SecurityAnalysis;

public partial class SecurityAnalysisInspectorView : UserControl
{
    public SecurityAnalysisInspectorView()
    {
        InitializeComponent();
    }

    public Button BackButton => BackToSecurityIssueListButton;
}
