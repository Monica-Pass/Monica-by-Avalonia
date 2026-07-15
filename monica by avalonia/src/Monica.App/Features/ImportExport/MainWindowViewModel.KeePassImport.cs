using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task SelectKeePassFileAsync()
    {
        if (IsKeePassImportBusy || !CanUseFilePicker)
        {
            return;
        }

        try
        {
            var file = await _fileSystemPickerService.OpenBinaryFileAsync(
                _localization.Get("SelectKeePassFile"),
                KeePassFileTypes);
            if (file is null)
            {
                return;
            }

            ClearKeePassImportPreview();
            _keePassPendingFile = file;
            KeePassSelectedFileName = file.FileName;
            StatusMessage = _localization.Format("KeePassFileSelectedFormat", file.FileName);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Get("KeePassImportCanceled");
        }
        catch (Exception)
        {
            StatusMessage = _localization.Get("KeePassFileSelectionFailed");
        }
    }

    [RelayCommand]
    private async Task PreviewKeePassImportAsync()
    {
        if (_keePassPendingFile is null)
        {
            StatusMessage = _localization.Get("KeePassFileRequired");
            return;
        }

        if (!TryBeginKeePassOperation(out var cancellationToken))
        {
            return;
        }

        var password = KeePassImportPassword;
        try
        {
            ClearKeePassImportPreview();
            IsKeePassImportProgressIndeterminate = true;
            StatusMessage = _localization.Get("KeePassPreviewLoading");
            var preview = await _keePassVaultService.ReadAsync(
                _keePassPendingFile.Content,
                _keePassPendingFile.FileName,
                password,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _keePassImportPreview = preview;
            KeePassPreviewEntryCount = preview.Entries.Count;
            KeePassPreviewGroupCount = preview.Groups.Count;
            OnPropertyChanged(nameof(HasKeePassImportPreview));
            OnPropertyChanged(nameof(KeePassPreviewSummaryText));
            StatusMessage = _localization.Format(
                "KeePassPreviewReadyFormat",
                preview.DatabaseName,
                preview.Entries.Count,
                preview.Groups.Count);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Get("KeePassImportCanceled");
        }
        catch (KeePassVaultException error)
        {
            StatusMessage = _localization.Get(error.Error switch
            {
                KeePassVaultError.UnsupportedFormat => "KeePassUnsupportedFormat",
                KeePassVaultError.ResourceLimitExceeded => "KeePassResourceLimitExceeded",
                _ => "KeePassUnlockFailed"
            });
        }
        catch (Exception)
        {
            StatusMessage = _localization.Get("KeePassPreviewFailed");
        }
        finally
        {
            password = "";
            KeePassImportPassword = "";
            EndKeePassOperation();
        }
    }

    [RelayCommand]
    private async Task ImportKeePassVaultAsync()
    {
        var preview = _keePassImportPreview;
        if (preview is null)
        {
            StatusMessage = _localization.Get("KeePassPreviewRequired");
            return;
        }

        var confirmed = await _confirmationDialogService.ConfirmAsync(
            _localization.Get("KeePassImportConfirmationTitle"),
            _localization.Format(
                "KeePassImportConfirmationMessageFormat",
                preview.DatabaseName,
                preview.Entries.Count),
            _localization.Get("Import"),
            _localization.Cancel);
        if (!confirmed || !TryBeginKeePassOperation(out var cancellationToken))
        {
            return;
        }

        var imported = 0;
        var skipped = 0;
        try
        {
            var existing = await _repository.GetPasswordsAsync(
                includeDeleted: true,
                includeArchived: true,
                cancellationToken);
            var sourceKeys = existing
                .Where(item => item.KeepassDatabaseId is not null && !string.IsNullOrWhiteSpace(item.KeepassEntryUuid))
                .Select(item => CreateKeePassSourceKey(item.KeepassDatabaseId!.Value, item.KeepassEntryUuid!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            KeePassImportProgress = 0;
            KeePassImportProgressMaximum = preview.Entries.Count;
            IsKeePassImportProgressIndeterminate = false;
            OnPropertyChanged(nameof(KeePassImportProgressText));

            foreach (var source in preview.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceKey = CreateKeePassSourceKey(preview.DatabaseId, source.EntryUuid);
                if (!sourceKeys.Add(sourceKey))
                {
                    skipped++;
                    AdvanceKeePassImportProgress();
                    continue;
                }

                var entry = CreatePasswordFromKeePass(preview.DatabaseId, source);
                await _repository.SavePasswordAsync(entry, cancellationToken);
                if (source.CustomFields.Count > 0)
                {
                    await _repository.ReplaceCustomFieldsAsync(
                        entry.Id,
                        source.CustomFields.Select((field, index) => new CustomField
                        {
                            EntryId = entry.Id,
                            Title = field.Name,
                            Value = field.Value,
                            IsProtected = field.IsProtected,
                            SortOrder = index
                        }).ToArray(),
                        cancellationToken);
                }

                foreach (var attachment in source.Attachments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ImportPasswordAttachmentAsync(
                        new Attachment
                        {
                            OwnerType = "PASSWORD",
                            OwnerId = entry.Id,
                            FileName = attachment.Name,
                            ContentType = "application/octet-stream",
                            SizeBytes = attachment.Content.Length,
                            CreatedAt = source.CreatedAt,
                            KeepassBinaryRef = attachment.BinaryReference
                        },
                        entry.Id,
                        attachment.Content.ToArray());
                }

                if (!string.IsNullOrWhiteSpace(entry.AuthenticatorKey))
                {
                    await SynchronizeBoundTotpAsync(entry);
                }

                imported++;
                AdvanceKeePassImportProgress();
            }

            await LogOperationAsync(new OperationLog
            {
                ItemType = "VAULT",
                ItemTitle = preview.DatabaseName,
                OperationType = "IMPORT_KEEPASS",
                ChangesJson = JsonSerializer.Serialize(new
                {
                    databaseId = preview.DatabaseId,
                    imported,
                    skipped
                }),
                DeviceName = Environment.MachineName
            });
            ClearKeePassImportState(cancelActiveOperation: false);
            await LoadAsync();
            StatusMessage = _localization.Format("KeePassImportedFormat", imported, skipped);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Format("KeePassImportCanceledAfterFormat", imported, skipped);
        }
        catch (Exception)
        {
            StatusMessage = _localization.Format("KeePassImportPartialFailureFormat", imported, skipped);
        }
        finally
        {
            EndKeePassOperation();
        }
    }

    [RelayCommand]
    private void CancelKeePassImport()
    {
        _keePassOperationCancellation?.Cancel();
        KeePassImportPassword = "";
        StatusMessage = _localization.Get("KeePassImportCanceled");
    }

    [RelayCommand]
    private void ResetKeePassImport() => ClearKeePassImportState(cancelActiveOperation: true);
}
