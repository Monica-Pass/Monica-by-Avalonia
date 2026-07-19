using System.Text;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task LoadWebDavBackupsAsync()
    {
        if (!TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        if (!TryBeginWebDavOperation(isLoading: true))
        {
            return;
        }

        try
        {
            WebDavOperationStageText = _localization.Get("WebDavLoadingBackups");
            var entries = await _webDavBackupService.ListAsync(profile, "");
            WebDavBackupHistory.Clear();
            foreach (var item in entries
                .Where(item => !item.IsDirectory)
                .OrderByDescending(item => item.LastModified ?? DateTimeOffset.MinValue)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                WebDavBackupHistory.Add(ToWebDavBackupHistoryItem(item));
            }

            SelectedWebDavBackupHistoryItem = WebDavBackupHistory.FirstOrDefault();
            RaiseWebDavBackupHistoryState();
            StatusMessage = _localization.Format("LoadedWebDavBackupsFormat", WebDavBackupHistory.Count);
        }
        catch (Exception ex)
        {
            ReportRemoteSyncFailure("Loading WebDAV backup history failed", "WebDavBackupHistoryFailed", ex);
        }
        finally
        {
            EndWebDavOperation(wasLoading: true);
        }
    }

    [RelayCommand]
    private async Task TestWebDavConnectionAsync()
    {
        if (!TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        if (!TryBeginWebDavOperation(isLoading: true))
        {
            return;
        }

        try
        {
            WebDavOperationStageText = _localization.Get("WebDavTestingConnection");
            var entries = await _webDavBackupService.ListAsync(profile, "");
            StatusMessage = _localization.Format("WebDavConnectionTestSucceededFormat", entries.Count);
        }
        catch (Exception ex)
        {
            ReportRemoteSyncFailure("WebDAV connection test failed", "WebDavConnectionTestFailed", ex);
        }
        finally
        {
            EndWebDavOperation(wasLoading: true);
        }
    }

    [RelayCommand]
    private async Task CreateWebDavBackupAsync()
    {
        if (!TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        if (!HasSelectedWebDavBackupOptions())
        {
            StatusMessage = _localization.Get("SelectWebDavBackupContent");
            return;
        }

        if (string.IsNullOrWhiteSpace(WebDavBackupEncryptionPassword))
        {
            StatusMessage = _localization.Get("WebDavEncryptionPasswordRequired");
            return;
        }

        if (!TryBeginWebDavOperation(isLoading: false))
        {
            return;
        }

        try
        {
            if (!await AuthorizeSensitiveExportAsync(grantFileExport: false))
            {
                return;
            }

            WebDavOperationStageText = _localization.Get("WebDavPreparingBackup");
            var json = await BuildMonicaJsonExportAsync(
                WebDavBackupIncludePasswords,
                WebDavBackupIncludeTotp,
                WebDavBackupIncludeNotes,
                WebDavBackupIncludeCards,
                WebDavBackupIncludeDocuments,
                WebDavBackupIncludeImages,
                WebDavBackupIncludeCategories);
            var fileName = $"monica_backup_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.monica.enc.json";
            WebDavOperationStageText = _localization.Get("WebDavEncryptingBackup");
            var content = await _webDavBackupCryptoService.EncryptAsync(json, WebDavBackupEncryptionPassword);

            WebDavOperationStageText = _localization.Get("WebDavUploadingBackup");
            await _webDavBackupService.UploadTextAsync(profile, fileName, content);
            var path = _webDavBackupService.NormalizeRemotePath(profile.RootPath, fileName);
            var existing = WebDavBackupHistory.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                WebDavBackupHistory.Remove(existing);
            }

            var backupItem = new WebDavBackupHistoryItem(
                fileName,
                path,
                DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy/MM/dd HH:mm", _localization.Culture),
                FormatByteSize(Encoding.UTF8.GetByteCount(content)),
                DateTimeOffset.UtcNow);
            WebDavBackupHistory.Insert(0, backupItem);
            SelectedWebDavBackupHistoryItem = backupItem;
            RaiseWebDavBackupHistoryState();
            StatusMessage = _localization.Format("CreatedWebDavBackupFormat", fileName);
        }
        catch (WebDavTextPayloadTooLargeException ex)
        {
            SetWebDavBackupSizeLimitError(ex);
        }
        catch (Exception ex)
        {
            ReportRemoteSyncFailure("Creating WebDAV backup failed", "CreateWebDavBackupFailed", ex);
        }
        finally
        {
            EndWebDavOperation(wasLoading: false);
        }
    }

    [RelayCommand]
    private async Task RestoreWebDavBackupAsync(WebDavBackupHistoryItem? item)
    {
        if (item is null || !TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        if (IsEncryptedWebDavBackup(item.FileName) && string.IsNullOrWhiteSpace(WebDavBackupEncryptionPassword))
        {
            StatusMessage = _localization.Get("WebDavEncryptionPasswordRequired");
            return;
        }

        if (!TryBeginWebDavOperation(isLoading: false))
        {
            return;
        }

        try
        {
            if (!await ConfirmRestoreWebDavBackupAsync(item.FileName))
            {
                return;
            }

            WebDavOperationStageText = _localization.Get("WebDavDownloadingBackup");
            var content = await _webDavBackupService.DownloadTextAsync(profile, item.FileName);
            var json = content;
            if (IsEncryptedWebDavBackup(item.FileName))
            {
                WebDavOperationStageText = _localization.Get("WebDavDecryptingBackup");
                json = await _webDavBackupCryptoService.DecryptAsync(content, WebDavBackupEncryptionPassword);
            }

            WebDavOperationStageText = _localization.Get("WebDavRestoringBackup");
            var result = await ImportMonicaJsonAsync(json);
            StatusMessage = _localization.Format("RestoredWebDavBackupFormat", item.FileName, result.Passwords, result.SecureItems, result.Categories);
        }
        catch (WebDavTextPayloadTooLargeException ex)
        {
            SetWebDavBackupSizeLimitError(ex);
        }
        catch (WebDavBackupCryptoException ex)
        {
            SetWebDavBackupCryptoError(ex);
        }
        catch (Exception ex)
        {
            ReportRemoteSyncFailure("Restoring WebDAV backup failed", "RestoreWebDavBackupFailed", ex);
        }
        finally
        {
            EndWebDavOperation(wasLoading: false);
        }
    }

    [RelayCommand]
    private async Task RestoreLatestWebDavBackupAsync()
    {
        if (!WebDavBackupHistory.Any())
        {
            await LoadWebDavBackupsAsync();
        }

        await RestoreWebDavBackupAsync(WebDavBackupHistory.FirstOrDefault());
    }

    [RelayCommand]
    private async Task DeleteWebDavBackupAsync(WebDavBackupHistoryItem? item)
    {
        if (item is null || !TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        if (!await ConfirmDeleteWebDavBackupAsync(item.FileName))
        {
            return;
        }

        if (!TryBeginWebDavOperation(isLoading: false))
        {
            return;
        }

        try
        {
            WebDavOperationStageText = _localization.Get("WebDavDeletingBackup");
            await _webDavBackupService.DeleteAsync(profile, item.FileName);
            WebDavBackupHistory.Remove(item);
            if (SelectedWebDavBackupHistoryItem == item)
            {
                SelectedWebDavBackupHistoryItem = WebDavBackupHistory.FirstOrDefault();
            }

            RaiseWebDavBackupHistoryState();
            StatusMessage = _localization.Format("DeletedWebDavBackupFormat", item.FileName);
        }
        catch (Exception ex)
        {
            ReportRemoteSyncFailure("Deleting WebDAV backup failed", "DeleteWebDavBackupFailed", ex);
        }
        finally
        {
            EndWebDavOperation(wasLoading: false);
        }
    }

    private Task<bool> ConfirmRestoreWebDavBackupAsync(string fileName) =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("RestoreWebDavBackupConfirmationTitle"),
            _localization.Format("RestoreWebDavBackupConfirmationMessageFormat", fileName),
            _localization.Get("OperationRestore"),
            _localization.Cancel);

    private Task<bool> ConfirmDeleteWebDavBackupAsync(string fileName) =>
        _confirmationDialogService.ConfirmTypedAsync(
            _localization.Get("DeleteWebDavBackupConfirmationTitle"),
            _localization.Format("DeleteWebDavBackupConfirmationMessageFormat", fileName),
            _localization.Get("DeleteWebDavBackupConfirmationPhrase"),
            _localization.Format(
                "DeleteWebDavBackupConfirmationInstructionFormat",
                _localization.Get("DeleteWebDavBackupConfirmationPhrase")),
            _localization.Get("Delete"),
            _localization.Cancel);
}
