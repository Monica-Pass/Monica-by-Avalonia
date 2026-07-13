using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly string[] KnownTwoFactorDomains =
    [
        "google.com",
        "gmail.com",
        "github.com",
        "microsoft.com",
        "apple.com",
        "amazon.com",
        "paypal.com",
        "dropbox.com",
        "facebook.com",
        "instagram.com",
        "linkedin.com",
        "reddit.com",
        "slack.com",
        "discord.com",
        "x.com",
        "twitter.com",
        "icloud.com",
        "outlook.com",
        "twitch.tv",
        "steam.com"
    ];

    private IReadOnlyDictionary<long, CompromisedPasswordResult> _compromisedPasswordResults =
        new Dictionary<long, CompromisedPasswordResult>();
    private bool _hasCompromisedPasswordCheckResults;

    public ObservableCollection<SecuritySummaryItem> SecuritySummaryItems { get; } = [];
    public ObservableCollection<SecurityIssueItem> SecurityIssueItems { get; } = [];

    [ObservableProperty]
    private bool _isCheckingCompromisedPasswords;

    [ObservableProperty]
    private string _compromisedPasswordStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSecurityIssue))]
    private SecurityIssueItem? _selectedSecurityIssue;

    public string SecurityIssueCountText =>
        _localization.Format("SecurityIssueCountFormat", SecurityIssueItems.Count);

    public bool HasSelectedSecurityIssue => SelectedSecurityIssue is not null;
    public bool HasSecurityIssues => SecurityIssueItems.Count > 0;

    [RelayCommand]
    private async Task CheckCompromisedPasswordsAsync()
    {
        if (IsCheckingCompromisedPasswords)
        {
            return;
        }

        var snapshots = BuildSecurityPasswordSnapshots();
        var plainPasswords = snapshots
            .Select(item => item.PlainPassword)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        IsCheckingCompromisedPasswords = true;
        CompromisedPasswordStatus = _localization.Format("CompromisedPasswordCheckingFormat", plainPasswords.Length);

        try
        {
            var countsByPassword = await _pwnedPasswordService.CheckPasswordsAsync(plainPasswords);
            var next = new Dictionary<long, CompromisedPasswordResult>();
            foreach (var snapshot in snapshots.Where(item => !string.IsNullOrWhiteSpace(item.PlainPassword)))
            {
                if (!countsByPassword.TryGetValue(snapshot.PlainPassword, out var count) || count <= 0)
                {
                    continue;
                }

                next[snapshot.Entry.Id] = new CompromisedPasswordResult(HashPasswordForSecurityCache(snapshot.PlainPassword), count);
            }

            _compromisedPasswordResults = next;
            _hasCompromisedPasswordCheckResults = true;
            CompromisedPasswordStatus = _localization.Format(
                "CompromisedPasswordCheckCompleteFormat",
                plainPasswords.Length,
                next.Count);
            RefreshSecurityAnalysis();
        }
        catch (Exception ex)
        {
            CompromisedPasswordStatus = _localization.Format("CompromisedPasswordCheckUnavailableFormat", ex.Message);
        }
        finally
        {
            IsCheckingCompromisedPasswords = false;
        }
    }
    public void RefreshSecurityAnalysis()
    {
        SecuritySummaryItems.Clear();
        SecurityIssueItems.Clear();

        var analyzed = BuildSecurityPasswordSnapshots();

        var compromisedCount = AddCompromisedPasswordIssues(analyzed);
        var weakCount = AddWeakPasswordIssues(analyzed);
        var duplicatePasswordCount = AddDuplicatePasswordIssues(analyzed);
        var duplicateWebsiteCount = AddDuplicateWebsiteIssues(analyzed);
        var missingTwoFactorCount = AddMissingTwoFactorIssues(analyzed);
        var staleCount = AddStalePasswordIssues(analyzed);

        var totalPenalty =
            compromisedCount * 10 +
            weakCount * 4 +
            duplicatePasswordCount * 6 +
            duplicateWebsiteCount * 2 +
            missingTwoFactorCount * 2 +
            staleCount;
        var score = Math.Clamp(100 - totalPenalty, 0, 100);

        SecuritySummaryItems.Add(new SecuritySummaryItem(
            _localization.SecurityScore,
            _localization.Format("SecurityScoreFormat", score),
            _localization.Format("SecurityAnalyzedPasswordCountFormat", analyzed.Length)));
        SecuritySummaryItems.Add(new SecuritySummaryItem(
            _localization.CompromisedPasswords,
            _hasCompromisedPasswordCheckResults ? compromisedCount.ToString(_localization.Culture) : "-",
            _localization.Get("CompromisedPasswordsSummary")));
        SecuritySummaryItems.Add(new SecuritySummaryItem(
            _localization.WeakPasswords,
            weakCount.ToString(_localization.Culture),
            _localization.Get("WeakPasswordsSummary")));
        SecuritySummaryItems.Add(new SecuritySummaryItem(
            _localization.DuplicatePasswords,
            duplicatePasswordCount.ToString(_localization.Culture),
            _localization.Get("DuplicatePasswordsSummary")));
        SecuritySummaryItems.Add(new SecuritySummaryItem(
            _localization.MissingTwoFactor,
            missingTwoFactorCount.ToString(_localization.Culture),
            _localization.Get("MissingTwoFactorSummary")));

        var orderedIssues = SecurityIssueItems
            .OrderByDescending(item => item.SeverityWeight)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        SecurityIssueItems.Clear();
        foreach (var issue in orderedIssues)
        {
            SecurityIssueItems.Add(issue);
        }

        SelectedSecurityIssue =
            SecurityIssueItems.FirstOrDefault(item => item.PasswordId == SelectedSecurityIssue?.PasswordId) ??
            SecurityIssueItems.FirstOrDefault();

        OnPropertyChanged(nameof(SecurityIssueCountText));
        OnPropertyChanged(nameof(HasSecurityIssues));
    }

    private async Task RefreshSecurityAnalysisDeferredAsync()
    {
        try
        {
            await Task.Delay(750);
            if (!IsUnlocked)
            {
                return;
            }

            AppDiagnostics.Measure("Refresh security analysis deferred", RefreshSecurityAnalysis);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Deferred security analysis failed", ex);
        }
    }

    private SecurityPasswordSnapshot[] BuildSecurityPasswordSnapshots()
    {
        return Passwords
            .Where(item => !item.IsDeleted && !item.IsArchived)
            .Select(item => new SecurityPasswordSnapshot(
                item,
                UnprotectPassword(item.Password).Trim(),
                SplitAndNormalizeWebsites(item.Website).ToArray()))
            .ToArray();
    }

    private int AddCompromisedPasswordIssues(IReadOnlyList<SecurityPasswordSnapshot> snapshots)
    {
        if (!_hasCompromisedPasswordCheckResults || _compromisedPasswordResults.Count == 0)
        {
            return 0;
        }

        var count = 0;
        foreach (var snapshot in snapshots.Where(item => !string.IsNullOrWhiteSpace(item.PlainPassword)))
        {
            if (!_compromisedPasswordResults.TryGetValue(snapshot.Entry.Id, out var result) ||
                !string.Equals(result.PasswordHash, HashPasswordForSecurityCache(snapshot.PlainPassword), StringComparison.Ordinal) ||
                result.ExposureCount <= 0)
            {
                continue;
            }

            count++;
            SecurityIssueItems.Add(new SecurityIssueItem(
                snapshot.Entry.Title,
                _localization.Format("CompromisedPasswordIssueFormat", result.ExposureCount),
                _localization.CompromisedPasswords,
                _localization.Get("HighSeverity"),
                snapshot.Entry.Id,
                snapshot.Entry,
                40));
        }

        return count;
    }

    private int AddWeakPasswordIssues(IReadOnlyList<SecurityPasswordSnapshot> snapshots)
    {
        var count = 0;
        foreach (var snapshot in snapshots)
        {
            if (string.IsNullOrWhiteSpace(snapshot.PlainPassword))
            {
                continue;
            }

            var strength = _passwordGenerator.Analyze(snapshot.PlainPassword);
            if (strength.Score > 2)
            {
                continue;
            }

            count++;
            SecurityIssueItems.Add(new SecurityIssueItem(
                snapshot.Entry.Title,
                _localization.Format(
                    "WeakPasswordIssueFormat",
                    PasswordStrengthLocalization.Label(_localization, strength.Label)),
                _localization.WeakPasswords,
                _localization.Get("HighSeverity"),
                snapshot.Entry.Id,
                snapshot.Entry,
                30));
        }

        return count;
    }

    private int AddDuplicatePasswordIssues(IReadOnlyList<SecurityPasswordSnapshot> snapshots)
    {
        var count = 0;
        foreach (var group in snapshots
            .Where(item => !string.IsNullOrWhiteSpace(item.PlainPassword))
            .GroupBy(item => item.PlainPassword, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            var titles = string.Join(", ", group.Select(item => item.Entry.Title).Distinct().Take(3));
            foreach (var snapshot in group)
            {
                count++;
                SecurityIssueItems.Add(new SecurityIssueItem(
                    snapshot.Entry.Title,
                    _localization.Format("DuplicatePasswordIssueFormat", group.Count(), titles),
                    _localization.DuplicatePasswords,
                    _localization.Get("HighSeverity"),
                    snapshot.Entry.Id,
                    snapshot.Entry,
                    28));
            }
        }

        return count;
    }

    private int AddDuplicateWebsiteIssues(IReadOnlyList<SecurityPasswordSnapshot> snapshots)
    {
        var websites = snapshots
            .SelectMany(snapshot => snapshot.NormalizedWebsites.Select(website => new WebsiteSnapshot(snapshot.Entry, website)))
            .GroupBy(item => item.Website, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Entry.Id).Distinct().Count() > 1);

        var count = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in websites)
        {
            var entries = group
                .GroupBy(item => item.Entry.Id)
                .Select(item => item.First().Entry)
                .ToArray();
            foreach (var entry in entries)
            {
                if (!seen.Add($"{entry.Id}:{group.Key}"))
                {
                    continue;
                }

                count++;
                SecurityIssueItems.Add(new SecurityIssueItem(
                    entry.Title,
                    _localization.Format("DuplicateWebsiteIssueFormat", group.Key, entries.Length),
                    _localization.DuplicateWebsites,
                    _localization.Get("MediumSeverity"),
                    entry.Id,
                    entry,
                    18));
            }
        }

        return count;
    }

    private int AddMissingTwoFactorIssues(IReadOnlyList<SecurityPasswordSnapshot> snapshots)
    {
        var count = 0;
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Entry.HasAuthenticator ||
                !string.IsNullOrWhiteSpace(snapshot.Entry.PasskeyBindings) ||
                snapshot.Entry.LoginType is not PasswordLoginType.Password ||
                snapshot.NormalizedWebsites.Length == 0)
            {
                continue;
            }

            var domain = snapshot.NormalizedWebsites.First();
            if (!KnownTwoFactorDomains.Any(known => domain.Equals(known, StringComparison.OrdinalIgnoreCase) || domain.EndsWith($".{known}", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            count++;
            SecurityIssueItems.Add(new SecurityIssueItem(
                snapshot.Entry.Title,
                _localization.Format("MissingTwoFactorIssueFormat", domain),
                _localization.MissingTwoFactor,
                _localization.Get("MediumSeverity"),
                snapshot.Entry.Id,
                snapshot.Entry,
                16));
        }

        return count;
    }
    private int AddStalePasswordIssues(IReadOnlyList<SecurityPasswordSnapshot> snapshots)
    {
        var threshold = DateTimeOffset.UtcNow.AddDays(-365);
        var count = 0;
        foreach (var snapshot in snapshots.Where(item => item.Entry.UpdatedAt < threshold))
        {
            count++;
            SecurityIssueItems.Add(new SecurityIssueItem(
                snapshot.Entry.Title,
                _localization.Format("StalePasswordIssueFormat", snapshot.Entry.UpdatedAt.LocalDateTime.ToString("d", _localization.Culture)),
                _localization.StalePasswords,
                _localization.Get("LowSeverity"),
                snapshot.Entry.Id,
                snapshot.Entry,
                8));
        }

        return count;
    }

    private static string HashPasswordForSecurityCache(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private sealed record SecurityPasswordSnapshot(PasswordEntry Entry, string PlainPassword, string[] NormalizedWebsites);
    private sealed record WebsiteSnapshot(PasswordEntry Entry, string Website);
    private sealed record CompromisedPasswordResult(string PasswordHash, int ExposureCount);
}
