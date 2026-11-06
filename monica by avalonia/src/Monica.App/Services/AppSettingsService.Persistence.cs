using System.Security.Cryptography;
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
            Migrate(Current);
            Normalize(Current);
            return;
        }

        await using var stream = File.OpenRead(_settingsPath);
        Current = await JsonSerializer.DeserializeAsync(
            stream,
            AppSettingsJsonContext.Default.DesktopAppSettings,
            cancellationToken) ?? new DesktopAppSettings();
        Migrate(Current);
        Normalize(Current);
        await UnprotectSecretsAsync(Current, cancellationToken);
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        Migrate(Current);
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

        if (!CanPersistSecrets)
        {
            // Without an operating-system secret store the value is never written, in the clear or
            // otherwise; it stays in memory for this session. Throwing here instead used to fail the
            // whole file, so on Linux and macOS one saved WebDAV password stopped every other setting
            // from persisting, and the lock path then discarded those unsaved changes.
            return "";
        }

        return ProtectedSettingPrefix + await _secretProtector!.ProtectAsync(value, cancellationToken);
    }

    private async Task<string> UnprotectSettingAsync(string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(ProtectedSettingPrefix, StringComparison.Ordinal))
        {
            return value;
        }

        if (!CanPersistSecrets)
        {
            AppDiagnostics.Info("A protected setting was skipped: no secret store is available on this platform.");
            return "";
        }

        try
        {
            return await _secretProtector!.UnprotectAsync(value[ProtectedSettingPrefix.Length..], cancellationToken);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // A value sealed for another user or machine (a data directory restored onto a new
            // install) can never be opened here. Failing the load would block Vault Access itself,
            // because settings load before the unlock screen is usable; the user re-enters it instead.
            AppDiagnostics.Error("A protected setting could not be opened and was cleared", ex);
            return "";
        }
    }

    private static string GetDefaultSettingsPath() => MonicaAppDataPaths.GetPath("settings.json");

    private static DesktopAppSettings Clone(DesktopAppSettings source) => new()
    {
        SettingsSchemaVersion = source.SettingsSchemaVersion,
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
        EmergencyKitLastExportedAtUtc = source.EmergencyKitLastExportedAtUtc,
        EmergencyKitLastFileName = source.EmergencyKitLastFileName,
        SecurityRecovery = source.SecurityRecovery,
        MinimizeToTray = source.MinimizeToTray,
        TrayHintShown = source.TrayHintShown,
        QuickSearchEnabled = source.QuickSearchEnabled,
        QuickSearchHotkey = source.QuickSearchHotkey,
        AutoTypeEnabled = source.AutoTypeEnabled,
        AutoTypeHotkey = source.AutoTypeHotkey,
        AutoTypeSequence = source.AutoTypeSequence,
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
        KeePassRecentVaults = [.. source.KeePassRecentVaults],
        FeatureToggles = new Dictionary<string, bool>(source.FeatureToggles, StringComparer.OrdinalIgnoreCase)
    };
}
