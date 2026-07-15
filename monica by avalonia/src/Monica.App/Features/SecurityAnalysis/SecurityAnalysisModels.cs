using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed record SecuritySummaryItem(string Label, string Value, string Detail);

public enum SecurityIssueSeverityLevel
{
    High,
    Medium,
    Low
}

public sealed record SecurityIssueItem(
    string Title,
    string Subtitle,
    string Category,
    string Severity,
    SecurityIssueSeverityLevel SeverityLevel,
    long PasswordId,
    PasswordEntry Entry,
    int SeverityWeight);
