using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed record SecuritySummaryItem(string Label, string Value, string Detail);

public sealed record SecurityIssueItem(
    string Title,
    string Subtitle,
    string Category,
    string Severity,
    long PasswordId,
    PasswordEntry Entry,
    int SeverityWeight);
