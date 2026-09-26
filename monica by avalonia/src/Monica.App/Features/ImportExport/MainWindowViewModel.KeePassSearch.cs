using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Features.ImportExport;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

/// <summary>
/// Searching the database that is currently open. The scan runs on every keystroke because a measured
/// walk of 20,000 entries costs 18 ms, which is under the time a person notices; a debounce would
/// only add a delay between typing and seeing. What is typed is never echoed back - the summary line
/// carries counts, not the query - so the search box cannot become a way to read out a value.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private CancellationTokenSource? _keePassSearchCancellation;
    private int _keePassSearchTotalMatches;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeePassSearchText))]
    [NotifyPropertyChangedFor(nameof(KeePassSearchSummaryText))]
    private string _keePassSearchText = "";

    public bool HasKeePassSearchText => !string.IsNullOrWhiteSpace(KeePassSearchText);

    /// <summary>
    /// "First 200 of 1.234" rather than a truncated list that looks complete. A password manager that
    /// quietly stops at a page boundary is how an entry gets reported as missing.
    /// </summary>
    public string KeePassSearchSummaryText
    {
        get
        {
            if (!HasKeePassSearchText || _keePassVaultSession is null)
            {
                return "";
            }

            return _keePassSearchTotalMatches == 0
                ? _localization.Get("KeePassSearchNoMatch")
                : _keePassSearchTotalMatches > KeePassTreeRowsPublic.Count
                    ? _localization.Format(
                        "KeePassSearchTruncatedFormat",
                        KeePassTreeRowsPublic.Count,
                        _keePassSearchTotalMatches)
                    : _localization.Format("KeePassSearchMatchCountFormat", _keePassSearchTotalMatches);
        }
    }

    partial void OnKeePassSearchTextChanged(string value)
    {
        _ = RunKeePassSearchAsync(value);
    }

    [RelayCommand]
    private void ClearKeePassSearch()
    {
        KeePassSearchText = "";
    }

    private async Task RunKeePassSearchAsync(string query)
    {
        var session = _keePassVaultSession;
        if (session is null)
        {
            return;
        }

        // The previous walk is still holding the session's gate; cancelling it is what keeps a fast
        // typist from queueing ten scans that all land after the eleventh.
        _keePassSearchCancellation?.Cancel();
        _keePassSearchCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _vaultSessionService.IsUnlocked
                ? _vaultSessionService.SessionCancellationToken
                : CancellationToken.None);
        _keePassSearchCancellation = cancellation;

        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                _keePassSearchTotalMatches = 0;
                await RebuildKeePassTreeAsync(session, cancellation.Token);
            }
            else
            {
                await PublishKeePassSearchRowsAsync(session, query, cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Searching the KeePass database failed", "KeePassSearchFailed", error);
        }
        finally
        {
            if (ReferenceEquals(cancellation, _keePassSearchCancellation))
            {
                cancellation.Dispose();
                _keePassSearchCancellation = null;
            }
        }
    }

    /// <summary>
    /// Scans and publishes the flat list. Kept apart from the rebuild above because the tree refresh on
    /// the other side calls it: a query that is still typed has to survive a folder create, a move or a
    /// save without the browser ever flashing back to the hierarchy.
    /// </summary>
    private async Task PublishKeePassSearchRowsAsync(
        KeePassVaultSession session,
        string query,
        CancellationToken cancellationToken)
    {
        var results = await session.SearchEntriesAsync(query, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _keePassSearchTotalMatches = results.TotalMatches;
        KeePassTreeRowsPublic = results.Entries
            .Select(entry => new KeePassTreeRow
            {
                Kind = KeePassTreeRowKind.Entry,
                Group = null,
                Entry = entry,
                ShowsGroupPath = true
            })
            .ToArray();
        OnPropertyChanged(nameof(KeePassSearchSummaryText));
    }
}
