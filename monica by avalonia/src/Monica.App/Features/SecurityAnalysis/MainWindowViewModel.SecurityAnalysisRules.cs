using System.Security.Cryptography;
using System.Text;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly string[] KnownTwoFactorDomains =
    [
        "google.com", "gmail.com", "github.com", "microsoft.com", "apple.com",
        "amazon.com", "paypal.com", "dropbox.com", "facebook.com", "instagram.com",
        "linkedin.com", "reddit.com", "slack.com", "discord.com", "x.com", "twitter.com",
        "icloud.com", "outlook.com", "twitch.tv", "steam.com"
    ];

    private SecurityAnalysisResult BuildSecurityAnalysisResult(
        IReadOnlyList<SecurityPasswordSnapshot> snapshots,
        IReadOnlyDictionary<long, CompromisedPasswordResult> compromisedResults,
        bool hasCompromisedResults,
        CancellationToken cancellationToken)
    {
        var issues = new List<SecurityIssueItem>();
        var compromisedCount = AddCompromisedPasswordIssues(
            snapshots,
            compromisedResults,
            hasCompromisedResults,
            issues,
            cancellationToken);
        var weakCount = AddWeakPasswordIssues(snapshots, issues, cancellationToken);
        var duplicatePasswordCount = AddDuplicatePasswordIssues(snapshots, issues, cancellationToken);
        var duplicateWebsiteCount = AddDuplicateWebsiteIssues(snapshots, issues, cancellationToken);
        var missingTwoFactorCount = AddMissingTwoFactorIssues(snapshots, issues, cancellationToken);
        var staleCount = AddStalePasswordIssues(snapshots, issues, cancellationToken);

        var totalPenalty =
            compromisedCount * 10 +
            weakCount * 4 +
            duplicatePasswordCount * 6 +
            duplicateWebsiteCount * 2 +
            missingTwoFactorCount * 2 +
            staleCount;
        var score = Math.Clamp(100 - totalPenalty, 0, 100);
        var summaries = new[]
        {
            new SecuritySummaryItem(
                _localization.SecurityScore,
                _localization.Format("SecurityScoreFormat", score),
                _localization.Format("SecurityAnalyzedPasswordCountFormat", snapshots.Count)),
            new SecuritySummaryItem(
                _localization.CompromisedPasswords,
                hasCompromisedResults ? compromisedCount.ToString(_localization.Culture) : "-",
                _localization.Get("CompromisedPasswordsSummary")),
            new SecuritySummaryItem(
                _localization.WeakPasswords,
                weakCount.ToString(_localization.Culture),
                _localization.Get("WeakPasswordsSummary")),
            new SecuritySummaryItem(
                _localization.DuplicatePasswords,
                duplicatePasswordCount.ToString(_localization.Culture),
                _localization.Get("DuplicatePasswordsSummary")),
            new SecuritySummaryItem(
                _localization.MissingTwoFactor,
                missingTwoFactorCount.ToString(_localization.Culture),
                _localization.Get("MissingTwoFactorSummary"))
        };
        cancellationToken.ThrowIfCancellationRequested();
        var orderedIssues = issues
            .OrderByDescending(item => item.SeverityWeight)
            .ThenBy(item => item.Title, StringComparer.Create(_localization.Culture, ignoreCase: true))
            .ToArray();
        return new SecurityAnalysisResult(summaries, orderedIssues);
    }

    private int AddCompromisedPasswordIssues(
        IReadOnlyList<SecurityPasswordSnapshot> snapshots,
        IReadOnlyDictionary<long, CompromisedPasswordResult> compromisedResults,
        bool hasCompromisedResults,
        List<SecurityIssueItem> issues,
        CancellationToken cancellationToken)
    {
        if (!hasCompromisedResults || compromisedResults.Count == 0) return 0;
        var count = 0;
        foreach (var snapshot in snapshots.Where(item => !string.IsNullOrWhiteSpace(item.PlainPassword)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!compromisedResults.TryGetValue(snapshot.Entry.Id, out var result) ||
                !string.Equals(result.PasswordHash, HashPasswordForSecurityCache(snapshot.PlainPassword), StringComparison.Ordinal) ||
                result.ExposureCount <= 0) continue;

            count++;
            issues.Add(new SecurityIssueItem(snapshot.Entry.Title,
                _localization.Format("CompromisedPasswordIssueFormat", result.ExposureCount),
                _localization.CompromisedPasswords, _localization.Get("HighSeverity"),
                snapshot.Entry.Id, snapshot.Entry, 40));
        }
        return count;
    }

    private int AddWeakPasswordIssues(
        IReadOnlyList<SecurityPasswordSnapshot> snapshots,
        List<SecurityIssueItem> issues,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(snapshot.PlainPassword)) continue;
            var strength = _passwordGenerator.Analyze(snapshot.PlainPassword);
            if (strength.Score > 2) continue;
            count++;
            issues.Add(new SecurityIssueItem(snapshot.Entry.Title,
                _localization.Format("WeakPasswordIssueFormat", PasswordStrengthLocalization.Label(_localization, strength.Label)),
                _localization.WeakPasswords, _localization.Get("HighSeverity"),
                snapshot.Entry.Id, snapshot.Entry, 30));
        }
        return count;
    }

    private int AddDuplicatePasswordIssues(
        IReadOnlyList<SecurityPasswordSnapshot> snapshots,
        List<SecurityIssueItem> issues,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var group in snapshots.Where(item => !string.IsNullOrWhiteSpace(item.PlainPassword))
                     .GroupBy(item => item.PlainPassword, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var titles = string.Join(", ", group.Select(item => item.Entry.Title).Distinct().Take(3));
            foreach (var snapshot in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                count++;
                issues.Add(new SecurityIssueItem(snapshot.Entry.Title,
                    _localization.Format("DuplicatePasswordIssueFormat", group.Count(), titles),
                    _localization.DuplicatePasswords, _localization.Get("HighSeverity"),
                    snapshot.Entry.Id, snapshot.Entry, 28));
            }
        }
        return count;
    }

    private int AddDuplicateWebsiteIssues(
        IReadOnlyList<SecurityPasswordSnapshot> snapshots,
        List<SecurityIssueItem> issues,
        CancellationToken cancellationToken)
    {
        var websites = snapshots.SelectMany(snapshot => snapshot.NormalizedWebsites.Select(website => new WebsiteSnapshot(snapshot.Entry, website)))
            .GroupBy(item => item.Website, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Entry.Id).Distinct().Count() > 1);
        var count = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in websites)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = group.GroupBy(item => item.Entry.Id).Select(item => item.First().Entry).ToArray();
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add($"{entry.Id}:{group.Key}")) continue;
                count++;
                issues.Add(new SecurityIssueItem(entry.Title,
                    _localization.Format("DuplicateWebsiteIssueFormat", group.Key, entries.Length),
                    _localization.DuplicateWebsites, _localization.Get("MediumSeverity"),
                    entry.Id, entry, 18));
            }
        }
        return count;
    }

    private int AddMissingTwoFactorIssues(
        IReadOnlyList<SecurityPasswordSnapshot> snapshots,
        List<SecurityIssueItem> issues,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.Entry.HasAuthenticator || !string.IsNullOrWhiteSpace(snapshot.Entry.PasskeyBindings) ||
                snapshot.Entry.LoginType is not PasswordLoginType.Password || snapshot.NormalizedWebsites.Length == 0) continue;
            var domain = snapshot.NormalizedWebsites[0];
            if (!KnownTwoFactorDomains.Any(known => domain.Equals(known, StringComparison.OrdinalIgnoreCase) || domain.EndsWith($".{known}", StringComparison.OrdinalIgnoreCase))) continue;
            count++;
            issues.Add(new SecurityIssueItem(snapshot.Entry.Title,
                _localization.Format("MissingTwoFactorIssueFormat", domain),
                _localization.MissingTwoFactor, _localization.Get("MediumSeverity"),
                snapshot.Entry.Id, snapshot.Entry, 16));
        }
        return count;
    }

    private int AddStalePasswordIssues(
        IReadOnlyList<SecurityPasswordSnapshot> snapshots,
        List<SecurityIssueItem> issues,
        CancellationToken cancellationToken)
    {
        var threshold = DateTimeOffset.UtcNow.AddDays(-365);
        var count = 0;
        foreach (var snapshot in snapshots.Where(item => item.Entry.UpdatedAt < threshold))
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
            issues.Add(new SecurityIssueItem(snapshot.Entry.Title,
                _localization.Format("StalePasswordIssueFormat", snapshot.Entry.UpdatedAt.LocalDateTime.ToString("d", _localization.Culture)),
                _localization.StalePasswords, _localization.Get("LowSeverity"),
                snapshot.Entry.Id, snapshot.Entry, 8));
        }
        return count;
    }

    private static string HashPasswordForSecurityCache(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record SecurityPasswordSnapshot(PasswordEntry Entry, string PlainPassword, string[] NormalizedWebsites);
    private sealed record CompromisedPasswordCheckInput(SecurityPasswordSnapshot[] Snapshots, string[] PlainPasswords);
    private sealed record SecurityAnalysisResult(
        IReadOnlyList<SecuritySummaryItem> Summaries,
        IReadOnlyList<SecurityIssueItem> Issues);
    private sealed record WebsiteSnapshot(PasswordEntry Entry, string Website);
    private sealed record CompromisedPasswordResult(string PasswordHash, int ExposureCount);
}
