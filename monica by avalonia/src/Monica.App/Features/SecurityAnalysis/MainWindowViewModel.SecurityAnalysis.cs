using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly IPwnedPasswordService _pwnedPasswordService;

    [RelayCommand]
    private void ShowSecurityIssueDetails(SecurityIssueItem? issue)
    {
        if (issue is not null)
        {
            SelectedSecurityIssue = issue;
            SecurityAnalysisNarrowShowsList = false;
        }
    }

    private IReadOnlyDictionary<long, CompromisedPasswordResult> _compromisedPasswordResults =
        new Dictionary<long, CompromisedPasswordResult>();
    private bool _hasCompromisedPasswordCheckResults;
    private bool _isSecurityAnalysisDirty;

    public ObservableCollection<SecuritySummaryItem> SecuritySummaryItems { get; } =
        new ObservableRangeCollection<SecuritySummaryItem>();
    public ObservableCollection<SecurityIssueItem> SecurityIssueItems { get; } =
        new ObservableRangeCollection<SecurityIssueItem>();
    public ObservableCollection<SecurityIssueItem> FilteredSecurityIssueItems { get; } =
        new ObservableRangeCollection<SecurityIssueItem>();

    [ObservableProperty]
    private bool _isCheckingCompromisedPasswords;

    [ObservableProperty]
    private bool _isRefreshingSecurityAnalysis;

    [ObservableProperty]
    private string _securityIssueSearchText = "";

    [ObservableProperty]
    private bool _securityAnalysisNarrowShowsList = true;

    [ObservableProperty]
    private string _compromisedPasswordStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSecurityIssue))]
    private SecurityIssueItem? _selectedSecurityIssue;

    public string SecurityIssueCountText =>
        _localization.Format("SecurityIssueCountFormat", SecurityIssueItems.Count);

    public bool HasSelectedSecurityIssue => SelectedSecurityIssue is not null;
    public bool HasSecurityIssues => SecurityIssueItems.Count > 0;

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task CheckCompromisedPasswordsAsync(CancellationToken cancellationToken)
    {
        if (!TryBeginSecurityAnalysisOperation(compromisedCheck: true))
        {
            return;
        }

        try
        {
            var entries = Passwords.ToArray();
            var checkInput = await Task.Run(
                () => BuildCompromisedPasswordCheckInput(entries, cancellationToken),
                cancellationToken);
            CompromisedPasswordStatus = _localization.Format(
                "CompromisedPasswordCheckingFormat",
                checkInput.PlainPasswords.Length);

            var exposureCounts = await _pwnedPasswordService.CheckPasswordsAsync(
                checkInput.PlainPasswords,
                cancellationToken);
            var next = await Task.Run(
                () => BuildCompromisedPasswordResults(
                    checkInput.Snapshots,
                    checkInput.PlainPasswords,
                    exposureCounts,
                    cancellationToken),
                cancellationToken);

            _compromisedPasswordResults = next;
            _hasCompromisedPasswordCheckResults = true;
            CompromisedPasswordStatus = _localization.Format(
                "CompromisedPasswordCheckCompleteFormat",
                checkInput.PlainPasswords.Length,
                next.Count);
            var currentEntries = Passwords.ToArray();
            var result = await BuildSecurityAnalysisAsync(currentEntries, cancellationToken);
            ApplySecurityAnalysisResult(result);
        }
        catch (OperationCanceledException)
        {
            CompromisedPasswordStatus = _localization.Get("CompromisedPasswordCheckCancelled");
        }
        catch (Exception ex)
        {
            CompromisedPasswordStatus = _localization.Format("CompromisedPasswordCheckUnavailableFormat", ex.Message);
        }
        finally
        {
            EndSecurityAnalysisOperation(compromisedCheck: true);
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RefreshSecurityAnalysisAsync(CancellationToken cancellationToken)
    {
        if (!TryBeginSecurityAnalysisOperation(compromisedCheck: false))
        {
            return;
        }
        try
        {
            var entries = Passwords.ToArray();
            var result = await BuildSecurityAnalysisAsync(entries, cancellationToken);
            ApplySecurityAnalysisResult(result);
            CompromisedPasswordStatus = _localization.Get("SecurityAnalysisRefreshed");
        }
        catch (OperationCanceledException)
        {
            CompromisedPasswordStatus = _localization.Get("SecurityAnalysisRefreshCancelled");
        }
        catch (Exception ex)
        {
            CompromisedPasswordStatus = _localization.Format("SecurityAnalysisRefreshFailedFormat", ex.Message);
        }
        finally
        {
            EndSecurityAnalysisOperation(compromisedCheck: false);
        }
    }

    public void RefreshSecurityAnalysis()
    {
        var snapshots = BuildSecurityPasswordSnapshots(Passwords.ToArray(), CancellationToken.None);
        ApplySecurityAnalysisResult(BuildSecurityAnalysisResult(
            snapshots,
            _compromisedPasswordResults,
            _hasCompromisedPasswordCheckResults,
            CancellationToken.None));
    }

    private Task<SecurityAnalysisResult> BuildSecurityAnalysisAsync(
        PasswordEntry[] entries,
        CancellationToken cancellationToken)
    {
        var compromisedResults = _compromisedPasswordResults;
        var hasCompromisedResults = _hasCompromisedPasswordCheckResults;
        return Task.Run(() =>
        {
            var snapshots = BuildSecurityPasswordSnapshots(entries, cancellationToken);
            return BuildSecurityAnalysisResult(
                snapshots,
                compromisedResults,
                hasCompromisedResults,
                cancellationToken);
        }, cancellationToken);
    }

    private void ApplySecurityAnalysisResult(SecurityAnalysisResult result)
    {
        var selectedPasswordId = SelectedSecurityIssue?.PasswordId;
        _isSecurityAnalysisDirty = false;
        ReplaceItems(SecuritySummaryItems, result.Summaries);
        ReplaceItems(SecurityIssueItems, result.Issues);

        SelectedSecurityIssue =
            SecurityIssueItems.FirstOrDefault(item => item.PasswordId == selectedPasswordId) ??
            SecurityIssueItems.FirstOrDefault();

        OnPropertyChanged(nameof(SecurityIssueCountText));
        OnPropertyChanged(nameof(HasSecurityIssues));
        RefreshSecurityIssueFilter();
    }

    private void InvalidateSecurityAnalysis()
    {
        _isSecurityAnalysisDirty = true;
        RefreshSecurityAnalysisIfNeeded();
    }

    private void RefreshSecurityAnalysisIfNeeded()
    {
        if (_isSecurityAnalysisDirty &&
            string.Equals(SelectedSection, "SecurityAnalysis", StringComparison.Ordinal))
        {
            if (Passwords.Count >= 500)
            {
                _ = RefreshSecurityAnalysisAsync(CancellationToken.None);
            }
            else
            {
                AppDiagnostics.Measure("Refresh visible security analysis", RefreshSecurityAnalysis);
            }
        }
    }

    private SecurityPasswordSnapshot[] BuildSecurityPasswordSnapshots(
        IReadOnlyList<PasswordEntry> entries,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<SecurityPasswordSnapshot>(entries.Count);
        foreach (var item in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.IsDeleted || item.IsArchived)
            {
                continue;
            }

            var password = ReadPasswordSecret(item.Password);
            if (!password.IsReadable)
            {
                continue;
            }

            snapshots.Add(new SecurityPasswordSnapshot(
                item,
                password.Value.Trim(),
                SplitAndNormalizeWebsites(item.Website).ToArray()));
        }

        return snapshots.ToArray();
    }

    private CompromisedPasswordCheckInput BuildCompromisedPasswordCheckInput(
        IReadOnlyList<PasswordEntry> entries,
        CancellationToken cancellationToken)
    {
        var snapshots = BuildSecurityPasswordSnapshots(entries, cancellationToken);
        var plainPasswords = snapshots
            .Select(item => item.PlainPassword)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new CompromisedPasswordCheckInput(snapshots, plainPasswords);
    }

    private static IReadOnlyDictionary<long, CompromisedPasswordResult> BuildCompromisedPasswordResults(
        IReadOnlyList<SecurityPasswordSnapshot> snapshots,
        IReadOnlyList<string> checkedPasswords,
        IReadOnlyList<int> exposureCounts,
        CancellationToken cancellationToken)
    {
        if (checkedPasswords.Count != exposureCounts.Count)
        {
            throw new InvalidOperationException("Compromised-password results did not match the requested input count.");
        }

        var countsByFingerprint = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < checkedPasswords.Count; index++)
        {
            if (exposureCounts[index] > 0)
            {
                countsByFingerprint[HashPasswordForSecurityCache(checkedPasswords[index])] = exposureCounts[index];
            }
        }

        var results = new Dictionary<long, CompromisedPasswordResult>();
        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(snapshot.PlainPassword))
            {
                continue;
            }

            var passwordFingerprint = HashPasswordForSecurityCache(snapshot.PlainPassword);
            if (!countsByFingerprint.TryGetValue(passwordFingerprint, out var count) ||
                count <= 0)
            {
                continue;
            }

            results[snapshot.Entry.Id] = new CompromisedPasswordResult(
                passwordFingerprint,
                count);
        }

        return results;
    }

}
