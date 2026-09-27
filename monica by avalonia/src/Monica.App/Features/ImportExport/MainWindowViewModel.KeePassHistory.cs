using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

/// <summary>
/// The shapes the opened .kdbx remembers for the entry being looked at. A KeePass database keeps a
/// generation of every entry it edited, so the question this half answers is whether a person can see
/// those generations and put one back. Putting one back runs through the same staged write as typing in
/// the form: the tree shows it, the file stays as it was until they save.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Which entry the detail panel is currently answering. The history pane follows the panel rather
    /// than the tree selection, because applying an edit republishes the tree and leaves the entry on
    /// screen - a version list tied to the selection would go blank exactly when a person wants to read
    /// what the edit just filed away.
    /// </summary>
    private string _keePassDetailEntryUuid = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeePassHistory))]
    private IReadOnlyList<KeePassHistoryRow> _keePassHistoryVersions = [];

    public bool HasKeePassHistory => KeePassHistoryVersions.Count > 0;

    /// <summary>
    /// The heading and the button share one label: the list it opens is the only place that text turns
    /// up, so a second wording would name the same thing twice.
    /// </summary>
    public string KeePassHistoryLabel => _localization.Get("KeePassHistory");

    /// <summary>
    /// Lists the remembered versions of the entry on screen, newest first. The list is asked for rather
    /// than loaded with the detail because most of a library has never been edited twice, and an entry
    /// the person only clicked past should not pay for a walk of its history.
    /// </summary>
    [RelayCommand]
    private async Task ShowKeePassHistoryAsync()
    {
        var session = _keePassVaultSession;
        var entryUuid = _keePassDetailEntryUuid;
        if (session is null || entryUuid.Length == 0)
        {
            SetStatusFailure("KeePassEntryRequired");
            return;
        }

        try
        {
            await LoadKeePassHistoryAsync(session, entryUuid);
            if (!HasKeePassHistory)
            {
                SetStatusNotice("KeePassHistoryNone");
            }
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Reading the KeePass entry history failed", "KeePassHistoryFailed", error);
        }
    }

    /// <summary>
    /// Reverts the entry to the version the row points at. There is no confirmation because the revert
    /// is not the end of anything: the shape it overwrites is filed as the newest version on the way
    /// through, and none of it reaches the file until a save.
    /// </summary>
    [RelayCommand]
    private async Task RestoreKeePassHistoryVersionAsync(KeePassHistoryRow? row)
    {
        var session = _keePassVaultSession;
        var entryUuid = _keePassDetailEntryUuid;
        if (session is null || entryUuid.Length == 0 || row is null)
        {
            SetStatusFailure("KeePassEntryRequired");
            return;
        }

        try
        {
            var restored = await session.RestoreHistoryAsync(entryUuid, row.Index);
            if (restored is null)
            {
                // The version aged out between the click and here; the entry keeps what it had.
                SetStatusFailure("KeePassHistoryGone");
                return;
            }

            ShowKeePassEntryDetail(session, restored);
            await LoadKeePassHistoryAsync(session, entryUuid);
            await RebuildKeePassTreeAsync(session, CancellationToken.None);
            RaiseKeePassWriteState();
            SetStatusNotice("KeePassHistoryRestoredFormat", restored.Row.Title, row.SecondaryText);
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Restoring the KeePass entry version failed", "KeePassManageFailed", error);
        }
    }

    /// <summary>
    /// Reads the versions straight into the rows the panel shows. Called after a restore as well, because
    /// a revert files the shape it replaced - the list has to be the one the database now holds.
    /// </summary>
    private async Task LoadKeePassHistoryAsync(KeePassVaultSession session, string entryUuid)
    {
        var versions = await session.ReadHistoryAsync(entryUuid);
        var culture = _localization.Culture;
        KeePassHistoryVersions = versions
            .Reverse()
            .Select(version => new KeePassHistoryRow(
                version.Index,
                version.Title.Length > 0 ? version.Title : version.UserName,
                version.UpdatedAt.ToString("g", culture)))
            .ToArray();
    }
}

/// <summary>
/// One row of the version list. The index is the address inside the database's own history, carried
/// along so the newest-first ordering the list shows cannot point a restore at the wrong version.
/// </summary>
public sealed record KeePassHistoryRow(int Index, string PrimaryText, string SecondaryText);
