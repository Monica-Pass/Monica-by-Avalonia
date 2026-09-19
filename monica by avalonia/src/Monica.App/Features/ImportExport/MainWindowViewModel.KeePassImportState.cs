using CommunityToolkit.Mvvm.ComponentModel;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeePassSelectedFile))]
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

    public bool HasKeePassSelectedFile => !string.IsNullOrWhiteSpace(KeePassSelectedFileName);
    public bool HasKeePassImportPreview => _keePassVaultSession is not null;
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

    private void ClearKeePassImportState(bool cancelActiveOperation)
    {
        if (cancelActiveOperation)
        {
            _keePassOperationCancellation?.Cancel();
        }

        KeePassImportPassword = "";
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
        _keePassVaultSession?.Dispose();
        _keePassVaultSession = null;
        OnPropertyChanged(nameof(HasKeePassImportPreview));
        OnPropertyChanged(nameof(KeePassPreviewSummaryText));
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
