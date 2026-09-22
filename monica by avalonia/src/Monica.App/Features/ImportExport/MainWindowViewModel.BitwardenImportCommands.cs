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
            SetStatusMessage("BitwardenFileSelectedFormat", file.FileName);
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage("BitwardenImportCanceled");
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Selecting Bitwarden JSON import failed", "BitwardenFileSelectionFailed", error);
        }
    }

    [RelayCommand]
    private async Task PreviewBitwardenJsonImportAsync()
    {
        if (_bitwardenPendingJson is null)
        {
            SetStatusFailure("BitwardenFileRequired");
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
            SetStatusMessage("BitwardenPreviewLoading");
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
            SetStatusMessage(
                "BitwardenPreviewReadyFormat",
                BitwardenPreviewPasswordCount,
                BitwardenPreviewSecureItemCount,
                BitwardenPreviewFolderCount,
                BitwardenPreviewUnsupportedCount);
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage("BitwardenImportCanceled");
        }
        catch (BitwardenJsonImportException error)
        {
            BitwardenSelectedFileName = "";
            SetStatusFailure(error.Error switch
            {
                BitwardenJsonImportError.EncryptedExport => "BitwardenEncryptedExportRejected",
                BitwardenJsonImportError.ResourceLimitExceeded => "BitwardenResourceLimitExceeded",
                _ => "BitwardenInvalidExport"
            });
        }
        catch (Exception error)
        {
            BitwardenSelectedFileName = "";
            ReportImportExportFailure("Previewing Bitwarden JSON import failed", "BitwardenPreviewFailed", error);
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
            SetStatusFailure("BitwardenPreviewRequired");
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
            SetStatusMessage("BitwardenImportedFormat", progress.Imported, progress.Skipped, preview.UnsupportedItemCount);
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage("BitwardenImportCanceledAfterFormat", progress.Imported, progress.Skipped);
        }
        catch (Exception error)
        {
            RecordImportExportFailure("Importing Bitwarden JSON vault failed", error);
            SetStatusMessage("BitwardenImportPartialFailureFormat", progress.Imported, progress.Skipped);
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
        SetStatusMessage("BitwardenImportCanceled");
    }

    [RelayCommand]
    private void ResetBitwardenImport() => ClearBitwardenImportState(cancelActiveOperation: true);
}
