using System.Text.Json.Serialization;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Platform.Services;

namespace Monica.App.Services;

public sealed class DesktopAppSettings
{
    public int SettingsSchemaVersion { get; set; }
    public string Language { get; set; } = "system";
    public string Theme { get; set; } = "system";
    public string StartupSection { get; set; } = "Passwords";
    public bool AutoLockEnabled { get; set; } = true;
    public int AutoLockMinutes { get; set; } = 5;
    public bool ClearClipboardEnabled { get; set; } = true;
    public int ClipboardClearSeconds { get; set; } = 30;
    public bool RequirePasswordBeforeExport { get; set; } = true;
    public bool WindowCaptureProtectionEnabled { get; set; }
    public int RecycleBinRetentionDays { get; set; } = 30;
    public string LegacyBusinessDataNoticeAcknowledgedSignature { get; set; } = "";
    public SecurityRecoverySettings SecurityRecovery { get; set; } = new();
    public bool MinimizeToTray { get; set; }
    public bool QuickSearchEnabled { get; set; } = true;
    public string QuickSearchHotkey { get; set; } = "Ctrl+Shift+Space";
    public bool BrowserIntegrationEnabled { get; set; }
    public int BrowserIntegrationPort { get; set; } = 49152;
    public bool CompactPasswordList { get; set; }
    public string PasswordSortOrder { get; set; } = "updated-desc";
    public bool WebDavEnabled { get; set; }
    public string WebDavServerUrl { get; set; } = "";
    public string WebDavUsername { get; set; } = "";
    public string WebDavPassword { get; set; } = "";
    public string WebDavRemotePath { get; set; } = "/Monica";
    public bool WebDavSyncOnStartup { get; set; }
    public bool WebDavSyncAfterChanges { get; set; }
    public bool WebDavBackupIncludePasswords { get; set; } = true;
    public bool WebDavBackupIncludeTotp { get; set; } = true;
    public bool WebDavBackupIncludeNotes { get; set; } = true;
    public bool WebDavBackupIncludeCards { get; set; } = true;
    public bool WebDavBackupIncludeDocuments { get; set; } = true;
    public bool WebDavBackupIncludeImages { get; set; } = true;
    public bool WebDavBackupIncludeCategories { get; set; } = true;
    public bool WebDavBackupEncryptionEnabled { get; set; } = true;
    public string WebDavBackupEncryptionPassword { get; set; } = "";
    public string SyncConflictStrategy { get; set; } = "ask";
    public bool OneDriveEnabled { get; set; }
    public bool MdbxLocalCacheEnabled { get; set; } = true;
    public Dictionary<string, bool> FeatureToggles { get; set; } = [];
}

public interface IAppSettingsService
{
    DesktopAppSettings Current { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
    Task ClearSensitiveCacheAsync(CancellationToken cancellationToken = default);
    IReadOnlyDictionary<string, bool> GetFeatureToggles();
    bool IsFeatureEnabled(string featureKey);
    void SetFeatureEnabled(string featureKey, bool isEnabled);
}

public sealed partial class AppSettingsService : IAppSettingsService
{
    private const int CurrentSettingsSchemaVersion = 1;
    private const string ProtectedSettingPrefix = "secret:v1:";

    private static readonly IReadOnlyDictionary<string, bool> DefaultFeatureToggles =
        FeatureCatalog.AndroidParityFeatures.ToDictionary(
            item => item.Key,
            item => item.Status is PlatformFeatureStatus.Available or PlatformFeatureStatus.DesktopEquivalent,
            StringComparer.OrdinalIgnoreCase);

    private readonly string _settingsPath;
    private readonly ISecretProtector? _secretProtector;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private bool _sensitiveCacheCleared;

    public AppSettingsService(string? settingsPath = null, ISecretProtector? secretProtector = null)
    {
        _settingsPath = settingsPath ?? GetDefaultSettingsPath();
        _secretProtector = secretProtector;
        Migrate(Current);
        Normalize(Current);
    }

    public DesktopAppSettings Current { get; private set; } = new();

    public IReadOnlyDictionary<string, bool> GetFeatureToggles()
    {
        NormalizeFeatureToggles(Current);
        return Current.FeatureToggles;
    }

    public bool IsFeatureEnabled(string featureKey)
    {
        NormalizeFeatureToggles(Current);
        return Current.FeatureToggles.TryGetValue(featureKey, out var isEnabled) && isEnabled;
    }

    public void SetFeatureEnabled(string featureKey, bool isEnabled)
    {
        NormalizeFeatureToggles(Current);
        if (!DefaultFeatureToggles.ContainsKey(featureKey))
        {
            return;
        }

        Current.FeatureToggles[featureKey] = isEnabled;
    }

    private static void Normalize(DesktopAppSettings settings)
    {
        settings.Language = NormalizeChoice(settings.Language, "system", "system", "en-US", "zh-CN");
        settings.Theme = NormalizeChoice(settings.Theme, "system", "system", "light", "dark", "high-contrast");
        settings.StartupSection = NormalizeChoice(settings.StartupSection, "Passwords", "Passwords", "Notes", "Totp", "Cards", "Generator", "Archive", "RecycleBin", "SecurityAnalysis", "Timeline", "Mdbx", "DatabaseManagement", "Sync", "Settings");
        settings.SyncConflictStrategy = NormalizeChoice(settings.SyncConflictStrategy, "ask", "ask", "local-wins", "remote-wins");
        settings.PasswordSortOrder = NormalizeChoice(settings.PasswordSortOrder, "updated-desc", "updated-desc", "title-asc", "website-asc", "username-asc", "created-desc", "favorites-first");
        settings.AutoLockMinutes = Clamp(settings.AutoLockMinutes, 1, 120);
        settings.ClipboardClearSeconds = Clamp(settings.ClipboardClearSeconds, 10, 600);
        settings.RecycleBinRetentionDays = settings.RecycleBinRetentionDays is 7 or 30 or 90 or 365
            ? settings.RecycleBinRetentionDays
            : 30;
        settings.BrowserIntegrationPort = Clamp(settings.BrowserIntegrationPort, 1024, 65535);
        settings.WebDavBackupEncryptionEnabled = true;
        settings.LegacyBusinessDataNoticeAcknowledgedSignature ??= "";
        settings.SecurityRecovery ??= new SecurityRecoverySettings();
        NormalizeSecurityRecovery(settings.SecurityRecovery);

        if (string.IsNullOrWhiteSpace(settings.WebDavRemotePath))
        {
            settings.WebDavRemotePath = "/Monica";
        }

        if (string.IsNullOrWhiteSpace(settings.QuickSearchHotkey))
        {
            settings.QuickSearchHotkey = "Ctrl+Shift+Space";
        }

        NormalizeFeatureToggles(settings);
    }

    private static void Migrate(DesktopAppSettings settings)
    {
        if (settings.SettingsSchemaVersion < 1)
        {
            // Screenshot protection was introduced enabled by default. Treat
            // that legacy value as an unset preference so upgrades do not
            // silently hide the app from the desktop capture surface.
            settings.WindowCaptureProtectionEnabled = false;
        }

        if (settings.SettingsSchemaVersion < CurrentSettingsSchemaVersion)
        {
            settings.SettingsSchemaVersion = CurrentSettingsSchemaVersion;
        }
    }

    private static void NormalizeSecurityRecovery(SecurityRecoverySettings settings)
    {
        var securityQuestions = new SecurityQuestionService();
        var question1 = securityQuestions.GetQuestion(settings.Question1Id);
        var question2 = securityQuestions.GetQuestion(settings.Question2Id);
        settings.Question1Id = settings.Question1Id == SecurityQuestionService.CustomQuestionId
            ? SecurityQuestionService.CustomQuestionId
            : question1.Id;
        settings.Question2Id = settings.Question2Id == SecurityQuestionService.CustomQuestionId
            ? SecurityQuestionService.CustomQuestionId
            : question2.Id;

        if (string.IsNullOrWhiteSpace(settings.Question1Text) || settings.Question1Id != SecurityQuestionService.CustomQuestionId)
        {
            settings.Question1Text = question1.Text;
        }

        if (string.IsNullOrWhiteSpace(settings.Question2Text) || settings.Question2Id != SecurityQuestionService.CustomQuestionId)
        {
            settings.Question2Text = question2.Text;
        }

        if (settings.Question1Id == settings.Question2Id && settings.Question1Id != SecurityQuestionService.CustomQuestionId)
        {
            var fallback = securityQuestions.GetQuestion(1);
            settings.Question2Id = fallback.Id == settings.Question1Id ? 2 : fallback.Id;
            settings.Question2Text = securityQuestions.GetQuestion(settings.Question2Id).Text;
        }
    }

    private static void NormalizeFeatureToggles(DesktopAppSettings settings)
    {
        var normalized = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, defaultValue) in DefaultFeatureToggles)
        {
            normalized[key] = settings.FeatureToggles.TryGetValue(key, out var existingValue)
                ? existingValue
                : defaultValue;
        }

        settings.FeatureToggles = normalized;
    }

    private static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    private static string NormalizeChoice(string? value, string fallback, params string[] allowed)
    {
        return allowed.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
            ? allowed.First(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
            : fallback;
    }
}

[JsonSerializable(typeof(DesktopAppSettings))]
[JsonSerializable(typeof(Dictionary<string, bool>))]
[JsonSerializable(typeof(SecurityRecoverySettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
