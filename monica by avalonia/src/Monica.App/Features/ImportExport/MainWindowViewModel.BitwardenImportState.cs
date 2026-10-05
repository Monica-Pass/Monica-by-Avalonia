using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.ImportExport;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly PlatformFilePickerFileType[] BitwardenJsonFileTypes =
    [
        new("Bitwarden JSON", ["*.json"])
    ];

    private string? _bitwardenPendingJson;
    private BitwardenJsonImportSnapshot? _bitwardenImportPreview;
    private CancellationTokenSource? _bitwardenOperationCancellation;
    private TaskCompletionSource? _bitwardenImportOperationCompletion;
    private int _bitwardenOperationActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBitwardenSelectedFile))]
    private string _bitwardenSelectedFileName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBitwardenImportIdle))]
    [NotifyPropertyChangedFor(nameof(IsImportWorkspaceIdle))]
    private bool _isBitwardenImportBusy;

    [ObservableProperty]
    private int _bitwardenPreviewPasswordCount;

    [ObservableProperty]
    private int _bitwardenPreviewSecureItemCount;

    [ObservableProperty]
    private int _bitwardenPreviewFolderCount;

    [ObservableProperty]
    private int _bitwardenPreviewUnsupportedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBitwardenAttachmentMetadata))]
    private int _bitwardenPreviewAttachmentCount;

    [ObservableProperty]
    private int _bitwardenImportProgress;

    [ObservableProperty]
    private int _bitwardenImportProgressMaximum;

    [ObservableProperty]
    private bool _isBitwardenImportProgressIndeterminate = true;

    public bool HasBitwardenSelectedFile => !string.IsNullOrWhiteSpace(BitwardenSelectedFileName);
    public bool HasBitwardenImportPreview => _bitwardenImportPreview is not null;
    public bool HasBitwardenAttachmentMetadata => BitwardenPreviewAttachmentCount > 0;
    public bool IsBitwardenImportIdle => !IsMdbxBusy && !IsBitwardenImportBusy;
    public string BitwardenPreviewSummaryText => _bitwardenImportPreview is null
        ? _localization.Get("BitwardenPreviewEmpty")
        : _localization.Format(
            "BitwardenPreviewReadyFormat",
            BitwardenPreviewPasswordCount,
            BitwardenPreviewSecureItemCount,
            BitwardenPreviewFolderCount,
            BitwardenPreviewUnsupportedCount);
    public string BitwardenAttachmentNoticeText => BitwardenPreviewAttachmentCount <= 0
        ? ""
        : _localization.Format("BitwardenAttachmentMetadataFormat", BitwardenPreviewAttachmentCount);
    public string BitwardenImportProgressText => BitwardenImportProgressMaximum <= 0
        ? ""
        : _localization.Format(
            "BitwardenImportProgressFormat",
            BitwardenImportProgress,
            BitwardenImportProgressMaximum);

    private bool TryBeginBitwardenOperation(out CancellationToken cancellationToken)
    {
        if (IsMdbxBusy)
        {
            cancellationToken = CancellationToken.None;
            return false;
        }

        if (Interlocked.CompareExchange(ref _bitwardenOperationActive, 1, 0) != 0)
        {
            cancellationToken = CancellationToken.None;
            return false;
        }

        _bitwardenOperationCancellation?.Dispose();
        _bitwardenOperationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _vaultSessionService.IsUnlocked
                ? _vaultSessionService.SessionCancellationToken
                : CancellationToken.None);
        cancellationToken = _bitwardenOperationCancellation.Token;
        _bitwardenImportOperationCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IsBitwardenImportBusy = true;
        return true;
    }

    private void EndBitwardenOperation()
    {
        IsBitwardenImportBusy = false;
        Interlocked.Exchange(ref _bitwardenOperationActive, 0);
        _bitwardenOperationCancellation?.Dispose();
        _bitwardenOperationCancellation = null;
        _bitwardenImportOperationCompletion?.TrySetResult();
        _bitwardenImportOperationCompletion = null;
    }

    private void AdvanceBitwardenImportProgress()
    {
        BitwardenImportProgress++;
        OnPropertyChanged(nameof(BitwardenImportProgressText));
    }

    private void ClearBitwardenImportState(bool cancelActiveOperation)
    {
        if (cancelActiveOperation)
        {
            _bitwardenOperationCancellation?.Cancel();
        }

        _bitwardenPendingJson = null;
        BitwardenSelectedFileName = "";
        ClearBitwardenImportPreview();
        BitwardenImportProgress = 0;
        BitwardenImportProgressMaximum = 0;
        IsBitwardenImportProgressIndeterminate = true;
        OnPropertyChanged(nameof(BitwardenImportProgressText));
    }

    private void ClearBitwardenImportPreview()
    {
        _bitwardenImportPreview = null;
        BitwardenPreviewPasswordCount = 0;
        BitwardenPreviewSecureItemCount = 0;
        BitwardenPreviewFolderCount = 0;
        BitwardenPreviewUnsupportedCount = 0;
        BitwardenPreviewAttachmentCount = 0;
        OnPropertyChanged(nameof(HasBitwardenImportPreview));
        OnPropertyChanged(nameof(BitwardenPreviewSummaryText));
        OnPropertyChanged(nameof(BitwardenAttachmentNoticeText));
    }
}
