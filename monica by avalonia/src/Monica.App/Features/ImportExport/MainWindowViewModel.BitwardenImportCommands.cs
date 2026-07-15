using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task SelectBitwardenJsonFileAsync()
    {
        if (IsBitwardenImportBusy || !CanUseFilePicker)
        {
            return;
        }

        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync(
                _localization.Get("SelectBitwardenJsonFile"),
                BitwardenJsonFileTypes);
            if (file is null)
            {
                return;
            }

            ClearBitwardenImportPreview();
            _bitwardenPendingJson = file.Content;
            BitwardenSelectedFileName = file.FileName;
            StatusMessage = _localization.Format("BitwardenFileSelectedFormat", file.FileName);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Get("BitwardenImportCanceled");
        }
        catch (Exception)
        {
            StatusMessage = _localization.Get("BitwardenFileSelectionFailed");
        }
    }

    [RelayCommand]
    private async Task PreviewBitwardenJsonImportAsync()
    {
        if (_bitwardenPendingJson is null)
        {
            StatusMessage = _localization.Get("BitwardenFileRequired");
            return;
        }

        if (!TryBeginBitwardenOperation(out var cancellationToken))
        {
            return;
        }

        var json = _bitwardenPendingJson;
        try
        {
            ClearBitwardenImportPreview();
            IsBitwardenImportProgressIndeterminate = true;
            StatusMessage = _localization.Get("BitwardenPreviewLoading");
            var preview = await Task.Run(
                () => _importExportService.ImportBitwardenJson(json),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _bitwardenImportPreview = preview;
            BitwardenPreviewPasswordCount = preview.Passwords.Count;
            BitwardenPreviewSecureItemCount = preview.SecureItems.Count;
            BitwardenPreviewFolderCount = preview.Folders.Count;
            BitwardenPreviewUnsupportedCount = preview.UnsupportedItemCount;
            BitwardenPreviewAttachmentCount = preview.AttachmentMetadataCount;
            OnPropertyChanged(nameof(HasBitwardenImportPreview));
            OnPropertyChanged(nameof(BitwardenPreviewSummaryText));
            OnPropertyChanged(nameof(BitwardenAttachmentNoticeText));
            StatusMessage = BitwardenPreviewSummaryText;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Get("BitwardenImportCanceled");
        }
        catch (BitwardenJsonImportException error)
        {
            BitwardenSelectedFileName = "";
            StatusMessage = _localization.Get(error.Error switch
            {
                BitwardenJsonImportError.EncryptedExport => "BitwardenEncryptedExportRejected",
                BitwardenJsonImportError.ResourceLimitExceeded => "BitwardenResourceLimitExceeded",
                _ => "BitwardenInvalidExport"
            });
        }
        catch (Exception)
        {
            BitwardenSelectedFileName = "";
            StatusMessage = _localization.Get("BitwardenPreviewFailed");
        }
        finally
        {
            json = "";
            _bitwardenPendingJson = null;
            EndBitwardenOperation();
        }
    }

    [RelayCommand]
    private async Task ImportBitwardenJsonVaultAsync()
    {
        var preview = _bitwardenImportPreview;
        if (preview is null)
        {
            StatusMessage = _localization.Get("BitwardenPreviewRequired");
            return;
        }

        var confirmed = await _confirmationDialogService.ConfirmAsync(
            _localization.Get("BitwardenImportConfirmationTitle"),
            _localization.Format(
                "BitwardenImportConfirmationMessageFormat",
                preview.SupportedItemCount,
                preview.UnsupportedItemCount),
            _localization.Get("Import"),
            _localization.Cancel);
        if (!confirmed || !TryBeginBitwardenOperation(out var cancellationToken))
        {
            return;
        }

        var progress = new BitwardenImportAccumulator();
        try
        {
            BitwardenImportProgress = 0;
            BitwardenImportProgressMaximum = preview.SupportedItemCount;
            IsBitwardenImportProgressIndeterminate = false;
            OnPropertyChanged(nameof(BitwardenImportProgressText));
            await ImportBitwardenSnapshotAsync(preview, progress, cancellationToken);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "VAULT",
                ItemTitle = _localization.Get("BitwardenJson"),
                OperationType = "IMPORT_BITWARDEN_JSON",
                ChangesJson = JsonSerializer.Serialize(new
                {
                    progress.Imported,
                    progress.Skipped,
                    progress.CategoriesCreated,
                    unsupported = preview.UnsupportedItemCount
                }),
                DeviceName = Environment.MachineName
            });
            ClearBitwardenImportState(cancelActiveOperation: false);
            await LoadAsync();
            StatusMessage = _localization.Format(
                "BitwardenImportedFormat",
                progress.Imported,
                progress.Skipped,
                preview.UnsupportedItemCount);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Format(
                "BitwardenImportCanceledAfterFormat",
                progress.Imported,
                progress.Skipped);
        }
        catch (Exception)
        {
            StatusMessage = _localization.Format(
                "BitwardenImportPartialFailureFormat",
                progress.Imported,
                progress.Skipped);
        }
        finally
        {
            EndBitwardenOperation();
        }
    }

    [RelayCommand]
    private void CancelBitwardenImport()
    {
        _bitwardenOperationCancellation?.Cancel();
        StatusMessage = _localization.Get("BitwardenImportCanceled");
    }

    [RelayCommand]
    private void ResetBitwardenImport() => ClearBitwardenImportState(cancelActiveOperation: true);
}
