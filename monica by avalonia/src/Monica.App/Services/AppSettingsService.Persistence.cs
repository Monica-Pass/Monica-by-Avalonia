using System.Text.Json;
using Monica.Data;

namespace Monica.App.Services;

public sealed partial class AppSettingsService
{
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            await LoadCoreAsync(cancellationToken);
            _sensitiveCacheCleared = false;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            if (_sensitiveCacheCleared)
            {
                throw new InvalidOperationException("Settings cannot be saved while the vault is locked.");
            }

            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task ClearSensitiveCacheAsync(CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            Current.WebDavUsername = "";
            Current.WebDavPassword = "";
            Current.WebDavBackupEncryptionPassword = "";
            _sensitiveCacheCleared = true;
            _saveGate.Release();
        }
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            Current = new DesktopAppSettings();
            Normalize(Current);
            return;
        }

        await using var stream = File.OpenRead(_settingsPath);
        Current = await JsonSerializer.DeserializeAsync(
            stream,
            AppSettingsJsonContext.Default.DesktopAppSettings,
            cancellationToken) ?? new DesktopAppSettings();
        Normalize(Current);
        await UnprotectSecretsAsync(Current, cancellationToken);
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        Normalize(Current);
        var settingsToSave = Clone(Current);
        await ProtectSecretsAsync(settingsToSave, cancellationToken);
        await AtomicFileWriter.WriteAsync(
            _settingsPath,
            async (stream, token) =>
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settingsToSave,
                    AppSettingsJsonContext.Default.DesktopAppSettings,
                    token);
            },
            cancellationToken);
    }

    private async Task ProtectSecretsAsync(DesktopAppSettings settings, CancellationToken cancellationToken)
    {
        settings.WebDavPassword = await ProtectSettingAsync(settings.WebDavPassword, cancellationToken);
        settings.WebDavBackupEncryptionPassword = await ProtectSettingAsync(settings.WebDavBackupEncryptionPassword, cancellationToken);
    }

    private async Task UnprotectSecretsAsync(DesktopAppSettings settings, CancellationToken cancellationToken)
    {
        settings.WebDavPassword = await UnprotectSettingAsync(settings.WebDavPassword, cancellationToken);
        settings.WebDavBackupEncryptionPassword = await UnprotectSettingAsync(settings.WebDavBackupEncryptionPassword, cancellationToken);
    }

    private async Task<string> ProtectSettingAsync(string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith(ProtectedSettingPrefix, StringComparison.Ordinal))
        {
            return value;
        }

        var protector = _secretProtector
            ?? throw new InvalidOperationException("Sensitive settings require operating-system secret protection.");
        return ProtectedSettingPrefix + await protector.ProtectAsync(value, cancellationToken);
    }

    private async Task<string> UnprotectSettingAsync(string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(ProtectedSettingPrefix, StringComparison.Ordinal))
        {
            return value;
        }

        var protector = _secretProtector
            ?? throw new InvalidOperationException("Protected settings cannot be opened without operating-system secret protection.");
        return await protector.UnprotectAsync(value[ProtectedSettingPrefix.Length..], cancellationToken);
    }

    private static string GetDefaultSettingsPath() => MonicaAppDataPaths.GetPath("settings.json");

    private static DesktopAppSettings Clone(DesktopAppSettings source) => new()
    {
        Language = source.Language,
        Theme = source.Theme,
        StartupSection = source.StartupSection,
        AutoLockEnabled = source.AutoLockEnabled,
        AutoLockMinutes = source.AutoLockMinutes,
        ClearClipboardEnabled = source.ClearClipboardEnabled,
        ClipboardClearSeconds = source.ClipboardClearSeconds,
        RequirePasswordBeforeExport = source.RequirePasswordBeforeExport,
        WindowCaptureProtectionEnabled = source.WindowCaptureProtectionEnabled,
        LegacyBusinessDataNoticeAcknowledgedSignature = source.LegacyBusinessDataNoticeAcknowledgedSignature,
        SecurityRecovery = source.SecurityRecovery,
        MinimizeToTray = source.MinimizeToTray,
        QuickSearchEnabled = source.QuickSearchEnabled,
        QuickSearchHotkey = source.QuickSearchHotkey,
        BrowserIntegrationEnabled = source.BrowserIntegrationEnabled,
        BrowserIntegrationPort = source.BrowserIntegrationPort,
        CompactPasswordList = source.CompactPasswordList,
        PasswordSortOrder = source.PasswordSortOrder,
        WebDavEnabled = source.WebDavEnabled,
        WebDavServerUrl = source.WebDavServerUrl,
        WebDavUsername = source.WebDavUsername,
        WebDavPassword = source.WebDavPassword,
        WebDavRemotePath = source.WebDavRemotePath,
        WebDavSyncOnStartup = source.WebDavSyncOnStartup,
        WebDavSyncAfterChanges = source.WebDavSyncAfterChanges,
        WebDavBackupIncludePasswords = source.WebDavBackupIncludePasswords,
        WebDavBackupIncludeTotp = source.WebDavBackupIncludeTotp,
        WebDavBackupIncludeNotes = source.WebDavBackupIncludeNotes,
        WebDavBackupIncludeCards = source.WebDavBackupIncludeCards,
        WebDavBackupIncludeDocuments = source.WebDavBackupIncludeDocuments,
        WebDavBackupIncludeImages = source.WebDavBackupIncludeImages,
        WebDavBackupIncludeCategories = source.WebDavBackupIncludeCategories,
        WebDavBackupEncryptionEnabled = source.WebDavBackupEncryptionEnabled,
        WebDavBackupEncryptionPassword = source.WebDavBackupEncryptionPassword,
        SyncConflictStrategy = source.SyncConflictStrategy,
        OneDriveEnabled = source.OneDriveEnabled,
        MdbxLocalCacheEnabled = source.MdbxLocalCacheEnabled,
        FeatureToggles = new Dictionary<string, bool>(source.FeatureToggles, StringComparer.OrdinalIgnoreCase)
    };
}
