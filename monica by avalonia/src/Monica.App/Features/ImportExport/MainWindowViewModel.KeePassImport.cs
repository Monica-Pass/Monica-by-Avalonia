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

        if (KeePassVaultIsDirty)
        {
            SetStatusFailure("KeePassDiscardBeforeOpening");
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
            SetStatusNotice("KeePassFileSelectedFormat", file.FileName);
        }
        catch (OperationCanceledException)
        {
            SetStatusNotice("KeePassImportCanceled");
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Selecting KeePass import failed", "KeePassFileSelectionFailed", error);
        }
    }

    [RelayCommand]
    private async Task PreviewKeePassImportAsync()
    {
        if (_keePassPendingFile is null)
        {
            SetStatusFailure("KeePassFileRequired");
            return;
        }

        if (KeePassVaultIsDirty)
        {
            SetStatusFailure("KeePassDiscardBeforeOpening");
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
            SetStatusMessage("KeePassPreviewLoading");
            var session = await _keePassVaultService.OpenAsync(
                _keePassPendingFile.Content,
                _keePassPendingFile.FileName,
                password,
                _keePassPendingFile.FullPath,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _keePassVaultSession = session;
            RememberKeePassVault(session);
            _keePassOpenFolders.Add(session.RootGroupUuid);
            await RebuildKeePassTreeAsync(session, cancellationToken);
            OnPropertyChanged(nameof(HasKeePassImportPreview));
            OnPropertyChanged(nameof(ShowKeePassOpenForm));
            OnPropertyChanged(nameof(KeePassPreviewSummaryText));
            SetStatusNotice("KeePassPreviewReadyFormat", session.DatabaseName, session.EntryCount, session.GroupCount);
        }
        catch (OperationCanceledException)
        {
            SetStatusNotice("KeePassImportCanceled");
        }
        catch (KeePassVaultException error)
        {
            SetStatusFailure(error.Error switch
            {
                KeePassVaultError.UnsupportedFormat => "KeePassUnsupportedFormat",
                KeePassVaultError.ResourceLimitExceeded => "KeePassResourceLimitExceeded",
                _ => "KeePassUnlockFailed"
            });
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Previewing KeePass import failed", "KeePassPreviewFailed", error);
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
        var session = _keePassVaultSession;
        if (session is null)
        {
            SetStatusFailure("KeePassPreviewRequired");
            return;
        }

        var confirmed = await _confirmationDialogService.ConfirmAsync(
            _localization.Get("KeePassImportConfirmationTitle"),
            _localization.Format(
                "KeePassImportConfirmationMessageFormat",
                session.DatabaseName,
                session.EntryCount),
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
            KeePassImportProgressMaximum = session.EntryCount;
            IsKeePassImportProgressIndeterminate = false;
            OnPropertyChanged(nameof(KeePassImportProgressText));

            await foreach (var source in session.ReadDetailsAsync(cancellationToken))
            {
                var sourceKey = CreateKeePassSourceKey(session.DatabaseId, source.Row.EntryUuid);
                if (!sourceKeys.Add(sourceKey))
                {
                    skipped++;
                    AdvanceKeePassImportProgress();
                    continue;
                }

                var entry = CreatePasswordFromKeePass(session.DatabaseId, source);
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
                            FileName = attachment.Row.Name,
                            ContentType = "application/octet-stream",
                            SizeBytes = attachment.Content.Length,
                            CreatedAt = source.Row.CreatedAt,
                            KeepassBinaryRef = attachment.Row.BinaryReference
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
                ItemTitle = session.DatabaseName,
                OperationType = "IMPORT_KEEPASS",
                ChangesJson = JsonSerializer.Serialize(new
                {
                    databaseId = session.DatabaseId,
                    imported,
                    skipped
                }),
                DeviceName = Environment.MachineName
            });
            // A database the user still has unsaved edits in stays open after the import; the ones
            // they came here with nothing pending on close the way they always did.
            ClearKeePassImportState(cancelActiveOperation: false, keepUnsavedDatabase: true);
            await LoadAsync();
            SetStatusNotice("KeePassImportedFormat", imported, skipped);
        }
        catch (OperationCanceledException)
        {
            SetStatusNotice("KeePassImportCanceledAfterFormat", imported, skipped);
        }
        catch (Exception error)
        {
            RecordImportExportFailure("Importing KeePass vault failed", error);
            SetStatusMessage("KeePassImportPartialFailureFormat", imported, skipped);
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
        SetStatusNotice("KeePassImportCanceled");
    }

    /// <summary>
    /// The one affordance that throws away an opened database on purpose, so if it still holds
    /// edits that never reached the file the user is asked before they are gone. Navigation keeps
    /// such a database open instead of deciding for them.
    /// </summary>
    [RelayCommand]
    private async Task ResetKeePassImportAsync()
    {
        if (KeePassVaultIsDirty)
        {
            var discard = await _confirmationDialogService.ConfirmAsync(
                _localization.Get("KeePassDiscardTitle"),
                _localization.Get("KeePassDiscardMessage"),
                _localization.Get("KeePassDiscardAction"),
                _localization.Cancel);
            if (!discard)
            {
                return;
            }
        }

        ClearKeePassImportState(cancelActiveOperation: true);
    }
}
