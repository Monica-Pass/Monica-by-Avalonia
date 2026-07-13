using System.Text;
using CommunityToolkit.Mvvm.Input;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void SelectSyncPage(string? page)
    {
        SelectedSyncPage = NormalizeSyncPage(page);
    }

    private static string NormalizeSyncPage(string? page) =>
        page?.Trim().ToLowerInvariant() switch
        {
            "backup" or "backups" or "history" => "Backup",
            "sources" or "vaults" or "database" => "Sources",
            "import" => "Import",
            "export" => "Export",
            _ => "Configuration"
        };

    [RelayCommand]
    private void ShowVaultSourceDetails(VaultSourceDisplayItem? source)
    {
        if (source is not null)
        {
            SelectedVaultSource = source;
        }
    }

    [RelayCommand]
    private void ShowWebDavBackupDetails(WebDavBackupHistoryItem? item)
    {
        if (item is not null)
        {
            SelectedWebDavBackupHistoryItem = item;
        }
    }

    [RelayCommand]
    private async Task LoadWebDavBackupsAsync()
    {
        if (!TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        try
        {
            IsLoadingWebDavBackups = true;
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
            StatusMessage = _localization.Format("WebDavBackupHistoryFailedFormat", ex.Message);
        }
        finally
        {
            IsLoadingWebDavBackups = false;
        }
    }

    [RelayCommand]
    private async Task TestWebDavConnectionAsync()
    {
        if (!TryCreateWebDavProfile(out var profile))
        {
            return;
        }

        try
        {
            IsLoadingWebDavBackups = true;
            var entries = await _webDavBackupService.ListAsync(profile, "");
            StatusMessage = _localization.Format("WebDavConnectionTestSucceededFormat", entries.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("WebDavConnectionTestFailedFormat", ex.Message);
        }
        finally
        {
            IsLoadingWebDavBackups = false;
            RaiseSyncPageState();
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

        if (WebDavBackupEncryptionEnabled && string.IsNullOrWhiteSpace(WebDavBackupEncryptionPassword))
        {
            StatusMessage = _localization.Get("WebDavEncryptionPasswordRequired");
            return;
        }

        try
        {
            IsRunningWebDavBackup = true;
            var json = await BuildMonicaJsonExportAsync(
                WebDavBackupIncludePasswords,
                WebDavBackupIncludeTotp,
                WebDavBackupIncludeNotes,
                WebDavBackupIncludeCards,
                WebDavBackupIncludeDocuments,
                WebDavBackupIncludeImages,
                WebDavBackupIncludeCategories);
            var extension = WebDavBackupEncryptionEnabled ? "monica.enc.json" : "monica.json";
            var fileName = $"monica_backup_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.{extension}";
            var content = WebDavBackupEncryptionEnabled
                ? EncryptWebDavBackupPayload(json, WebDavBackupEncryptionPassword)
                : json;

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
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("CreateWebDavBackupFailedFormat", ex.Message);
        }
        finally
        {
            IsRunningWebDavBackup = false;
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

        try
        {
            IsRunningWebDavBackup = true;
            var content = await _webDavBackupService.DownloadTextAsync(profile, item.FileName);
            var json = IsEncryptedWebDavBackup(item.FileName)
                ? DecryptWebDavBackupPayload(content, WebDavBackupEncryptionPassword)
                : content;
            var result = await ImportMonicaJsonAsync(json);
            StatusMessage = _localization.Format("RestoredWebDavBackupFormat", item.FileName, result.Passwords, result.SecureItems, result.Categories);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("RestoreWebDavBackupFailedFormat", ex.Message);
        }
        finally
        {
            IsRunningWebDavBackup = false;
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

        try
        {
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
            StatusMessage = _localization.Format("DeleteWebDavBackupFailedFormat", ex.Message);
        }
    }

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
