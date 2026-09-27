using CommunityToolkit.Mvvm.ComponentModel;
using Monica.App.Features.ImportExport;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly PlatformFilePickerFileType[] KeePassFileTypes =
    [
        new("KeePass KDBX", ["*.kdbx"])
    ];

    private readonly IKeePassVaultService _keePassVaultService;
    private PickedBinaryFile? _keePassPendingFile;
    private KeePassVaultSession? _keePassVaultSession;
    private CancellationTokenSource? _keePassOperationCancellation;
    private int _keePassOperationActive;
    private readonly HashSet<string> _keePassOpenFolders = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<KeePassTreeRow> _keePassTreeRows = [];
    private KeePassTreeRow? _selectedKeePassTreeRow;
    private PasswordDetailViewModel? _keePassEntryDetails;
    private string? _incomingKeePassFilePath;
    private bool _applyingIncomingKeePassFiles;

    /// <summary>
    /// Which of the import tabs is showing. The view mirrors it both ways, so the page a database
    /// arrives on is the page that stays up when the tab is opened again.
    /// </summary>
    [ObservableProperty]
    private bool _keePassImportTabSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeePassSelectedFile))]
    [NotifyPropertyChangedFor(nameof(ShowsKeePassSelectedFileName))]
    [NotifyPropertyChangedFor(nameof(ShowKeePassOpenForm))]
    private string _keePassSelectedFileName = "";

    [ObservableProperty]
    private string _keePassImportPassword = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeePassImportIdle))]
    [NotifyPropertyChangedFor(nameof(IsImportWorkspaceIdle))]
    private bool _isKeePassImportBusy;

    [ObservableProperty]
    private int _keePassImportProgress;

    [ObservableProperty]
    private int _keePassImportProgressMaximum;

    [ObservableProperty]
    private bool _isKeePassImportProgressIndeterminate = true;

    [ObservableProperty]
    private IReadOnlyList<KeePassTreeRow> _keePassTreeRowsPublic = [];

    [ObservableProperty]
    private KeePassTreeRow? _selectedKeePassTreeRowPublic;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsKeePassDetailColumn))]
    [NotifyPropertyChangedFor(nameof(ShowsKeePassRail))]
    private PasswordDetailViewModel? _keePassEntryDetailsPublic;

    public bool HasKeePassSelectedFile => !string.IsNullOrWhiteSpace(KeePassSelectedFileName);
    public bool HasKeePassImportPreview => _keePassVaultSession is not null;

    /// <summary>
    /// The line the opened database writes for itself already names the file it came from, so the second
    /// copy of the name retires with the open form and hands its row to the browse tree. It comes back the
    /// moment a file is picked and not yet opened, where it is the only place the name is spelled out.
    /// </summary>
    public bool ShowsKeePassSelectedFileName => HasKeePassSelectedFile && !HasKeePassImportPreview;

    /// <summary>
    /// The master password is cleared the moment a database unlocks, so the field that took it can
    /// only ever render empty afterwards. Retiring it keeps the browse tree above the fold; closing
    /// the file brings the open form back. The create form wants the same stretch of screen, so the
    /// two never show at once.
    /// </summary>
    public bool ShowKeePassOpenForm =>
        HasKeePassSelectedFile && !HasKeePassImportPreview && !ShowKeePassCreateForm;
    public bool IsKeePassImportIdle => !IsKeePassImportBusy;
    public string KeePassPreviewSummaryText => _keePassVaultSession is null
        ? _localization.Get("KeePassPreviewEmpty")
        : _localization.Format(
            "KeePassPreviewReadyFormat",
            _keePassVaultSession.DatabaseName,
            _keePassVaultSession.EntryCount,
            _keePassVaultSession.GroupCount);
    public string KeePassImportProgressText => KeePassImportProgressMaximum <= 0
        ? ""
        : _localization.Format("KeePassImportProgressFormat", KeePassImportProgress, KeePassImportProgressMaximum);

    private bool TryBeginKeePassOperation(out CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _keePassOperationActive, 1, 0) != 0)
        {
            cancellationToken = CancellationToken.None;
            return false;
        }

        _keePassOperationCancellation?.Dispose();
        _keePassOperationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _vaultSessionService.IsUnlocked
                ? _vaultSessionService.SessionCancellationToken
                : CancellationToken.None);
        cancellationToken = _keePassOperationCancellation.Token;
        IsKeePassImportBusy = true;
        return true;
    }

    private void EndKeePassOperation()
    {
        IsKeePassImportBusy = false;
        Interlocked.Exchange(ref _keePassOperationActive, 0);
        _keePassOperationCancellation?.Dispose();
        _keePassOperationCancellation = null;
    }

    private void AdvanceKeePassImportProgress()
    {
        KeePassImportProgress++;
        OnPropertyChanged(nameof(KeePassImportProgressText));
    }

    /// <param name="keepUnsavedDatabase">
    /// Navigation asks to keep a database whose edits have not reached the file yet, because leaving
    /// the tab and coming back must not quietly throw away work the user still has to save — and an
    /// in-flight save must not be cancelled by a click somewhere else. Locking is not navigation: it
    /// passes false, and the decrypted database goes with the session. Opening a different file
    /// refuses outright instead of discarding on the user's behalf.
    /// </param>
    private void ClearKeePassImportState(bool cancelActiveOperation, bool keepUnsavedDatabase = false)
    {
        KeePassImportPassword = "";
        ClearKeePassCreateForm();
        if (keepUnsavedDatabase && KeePassVaultIsDirty)
        {
            return;
        }

        if (cancelActiveOperation)
        {
            _keePassOperationCancellation?.Cancel();
        }

        _keePassPendingFile = null;
        KeePassSelectedFileName = "";
        ClearKeePassImportPreview();
        KeePassImportProgress = 0;
        KeePassImportProgressMaximum = 0;
        IsKeePassImportProgressIndeterminate = true;
        OnPropertyChanged(nameof(KeePassImportProgressText));
    }

    private void ClearKeePassImportPreview()
    {
        // A search walking a database that is already gone would publish rows against a disposed
        // session, so the scan is cancelled first and the query is dropped once the session is gone -
        // setting it while a database is still open would simply start another one.
        _keePassSearchCancellation?.Cancel();
        _keePassSearchCancellation?.Dispose();
        _keePassSearchCancellation = null;
        _keePassVaultSession?.Dispose();
        _keePassVaultSession = null;
        KeePassSearchText = "";
        _keePassSearchTotalMatches = 0;
        _keePassEntryDetails?.Dispose();
        _keePassEntryDetails = null;
        KeePassEntryDetailsPublic = null;
        KeePassEditorPublic = null;
        _keePassOpenFolders.Clear();
        _keePassTreeRows = [];
        KeePassTreeRowsPublic = [];
        _selectedKeePassTreeRow = null;
        SelectedKeePassTreeRowPublic = null;
        KeePassPolicyMaxItemsText = "";
        KeePassPolicyMaxSizeMbText = "";
        _keePassPolicyMaxSizeBytes = null;
        KeePassPolicyMaintenanceDaysText = "";
        RaiseKeePassRail();
        OnPropertyChanged(nameof(ShowKeePassOpenForm));
        OnPropertyChanged(nameof(KeePassPreviewSummaryText));
        RaiseKeePassWriteState();
    }

    private static string CreateKeePassSourceKey(long databaseId, string entryUuid) =>
        $"{databaseId}:{entryUuid.Trim()}";

    private PasswordEntry CreatePasswordFromKeePass(long databaseId, KeePassEntryDetail source)
    {
        var row = source.Row;
        return new PasswordEntry
        {
            Title = string.IsNullOrWhiteSpace(row.Title) ? _localization.Untitled : row.Title,
            Website = row.Url,
            Username = row.UserName,
            Password = source.Password,
            Notes = source.Notes,
            AuthenticatorKey = source.AuthenticatorKey,
            KeepassDatabaseId = databaseId,
            KeepassGroupPath = row.GroupPath,
            KeepassEntryUuid = row.EntryUuid,
            KeepassGroupUuid = row.GroupUuid,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }
}
