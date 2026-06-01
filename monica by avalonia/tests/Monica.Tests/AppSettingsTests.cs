using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public async Task App_settings_roundtrip_interactive_values()
    {
        var path = GetTempPath();
        var first = new AppSettingsService(path);
        await first.LoadAsync();
        first.Current.Language = "zh-CN";
        first.Current.Theme = "dark";
        first.Current.AutoLockEnabled = false;
        first.Current.ClipboardClearSeconds = 120;
        first.Current.WebDavEnabled = true;
        first.Current.WebDavServerUrl = "https://dav.example.com";
        first.Current.SyncConflictStrategy = "local-wins";
        await first.SaveAsync();

        var second = new AppSettingsService(path);
        await second.LoadAsync();

        Assert.Equal("zh-CN", second.Current.Language);
        Assert.Equal("dark", second.Current.Theme);
        Assert.False(second.Current.AutoLockEnabled);
        Assert.Equal(120, second.Current.ClipboardClearSeconds);
        Assert.True(second.Current.WebDavEnabled);
        Assert.Equal("https://dav.example.com", second.Current.WebDavServerUrl);
        Assert.Equal("local-wins", second.Current.SyncConflictStrategy);
    }

    [Fact]
    public async Task App_settings_allows_archive_startup_section()
    {
        var path = GetTempPath();
        var settings = new AppSettingsService(path);
        await settings.LoadAsync();
        settings.Current.StartupSection = "Archive";
        await settings.SaveAsync();

        var reloaded = new AppSettingsService(path);
        await reloaded.LoadAsync();

        Assert.Equal("Archive", reloaded.Current.StartupSection);
    }

    [Fact]
    public async Task App_settings_allows_security_analysis_startup_section()
    {
        var path = GetTempPath();
        var settings = new AppSettingsService(path);
        await settings.LoadAsync();
        settings.Current.StartupSection = "SecurityAnalysis";
        await settings.SaveAsync();

        var reloaded = new AppSettingsService(path);
        await reloaded.LoadAsync();

        Assert.Equal("SecurityAnalysis", reloaded.Current.StartupSection);
    }

    [Fact]
    public async Task App_settings_allows_database_management_startup_section()
    {
        var path = GetTempPath();
        var settings = new AppSettingsService(path);
        await settings.LoadAsync();
        settings.Current.StartupSection = "DatabaseManagement";
        await settings.SaveAsync();

        var reloaded = new AppSettingsService(path);
        await reloaded.LoadAsync();

        Assert.Equal("DatabaseManagement", reloaded.Current.StartupSection);
    }

    [Fact]
    public async Task App_settings_initializes_feature_toggles_from_catalog()
    {
        var path = GetTempPath();
        var settings = new AppSettingsService(path);
        await settings.LoadAsync();

        var toggles = settings.GetFeatureToggles();

        Assert.All(FeatureCatalog.AndroidParityFeatures, feature => Assert.Contains(feature.Key, toggles.Keys));
        Assert.True(settings.IsFeatureEnabled("passwords"));
        Assert.False(settings.IsFeatureEnabled("autofill"));
    }

    [Fact]
    public async Task App_settings_persists_feature_toggle_overrides_and_removes_unknown_keys()
    {
        var path = GetTempPath();
        var settings = new AppSettingsService(path);
        await settings.LoadAsync();
        settings.SetFeatureEnabled("generator", false);
        settings.Current.FeatureToggles["unknown-feature"] = true;
        await settings.SaveAsync();

        var reloaded = new AppSettingsService(path);
        await reloaded.LoadAsync();

        Assert.False(reloaded.IsFeatureEnabled("generator"));
        Assert.False(reloaded.Current.FeatureToggles.ContainsKey("unknown-feature"));
    }

    [Fact]
    public async Task ViewModel_initializes_chinese_language_and_saves_changed_settings()
    {
        var settingsPath = GetTempPath();
        var settingsService = new AppSettingsService(settingsPath);
        await settingsService.LoadAsync();
        settingsService.Current.Language = "zh-CN";
        settingsService.Current.Theme = "light";
        await settingsService.SaveAsync();

        var viewModel = CreateViewModel(settingsPath);
        await viewModel.InitializeAsync();

        Assert.Equal("设置", viewModel.L.Settings);
        Assert.Equal("创建 Monica 保险库", viewModel.LoginTitle);
        Assert.Contains(viewModel.Capabilities, capability => capability.Title == "密码" && capability.Status == "可用");

        var passwordCapability = Assert.Single(viewModel.Capabilities, capability => capability.Key == "passwords");
        Assert.True(passwordCapability.CanToggle);
        Assert.True(passwordCapability.IsEnabled);
        Assert.Equal(viewModel.L.FeatureEnabled, passwordCapability.ToggleStatus);
        var autofillCapability = Assert.Single(viewModel.Capabilities, capability => capability.Key == "autofill");
        Assert.False(autofillCapability.IsEnabled);
        var credentialProviderCapability = Assert.Single(viewModel.Capabilities, capability => capability.Key == "credential-provider");
        Assert.False(credentialProviderCapability.IsEnabled);
        Assert.False(string.IsNullOrWhiteSpace(credentialProviderCapability.UnsupportedReason));

        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com";
        passwordCapability.IsEnabled = false;
        await Task.Delay(250);

        var reloaded = new AppSettingsService(settingsPath);
        await reloaded.LoadAsync();
        Assert.True(reloaded.Current.WebDavEnabled);
        Assert.Equal("https://dav.example.com", reloaded.Current.WebDavServerUrl);
        Assert.False(reloaded.IsFeatureEnabled("passwords"));
    }

    [Fact]
    public async Task ViewModel_loads_and_deletes_webdav_backup_history()
    {
        var settingsPath = GetTempPath();
        var webDav = new CapturingWebDavBackupService(
        [
            new RemoteFileEntry("/Monica/backup_20260101_120000.zip", false, 2048, new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)),
            new RemoteFileEntry("/Monica/folder", true, null, null)
        ]);
        var viewModel = CreateViewModel(settingsPath, webDav);
        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com";
        viewModel.WebDavUsername = "user";
        viewModel.WebDavPassword = "secret";
        viewModel.WebDavRemotePath = "/Monica";

        await viewModel.LoadWebDavBackupsCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.WebDavBackupHistory);
        Assert.Equal("backup_20260101_120000.zip", item.FileName);
        Assert.Equal("2 KB", item.SizeText);
        Assert.Equal("https://dav.example.com/", webDav.LastProfile?.BaseUri.ToString());
        Assert.Equal("/Monica", webDav.LastProfile?.RootPath);

        await viewModel.DeleteWebDavBackupCommand.ExecuteAsync(item);

        Assert.Empty(viewModel.WebDavBackupHistory);
        Assert.Equal(item.FileName, webDav.DeletedPath);
    }

    [Fact]
    public async Task ViewModel_surfaces_platform_integration_statuses()
    {
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.Tray, "Tray integration is ready."),
            PlatformIntegrationService.PlatformLimited(PlatformFeatureKeys.GlobalHotkey, "Global hotkeys need a native adapter.")
        ]);
        var viewModel = CreateViewModel(GetTempPath(), platformIntegrationService: integration);

        await viewModel.InitializeAsync();

        Assert.Equal("TestOS", viewModel.PlatformName);
        Assert.Equal(viewModel.L.Get("PlatformIntegrations"), viewModel.PlatformIntegrationsTitle);
        Assert.Contains("TestOS", viewModel.PlatformIntegrationSummaryText);
        Assert.Contains("1/2", viewModel.PlatformIntegrationSummaryText);

        var tray = Assert.Single(viewModel.PlatformIntegrationCapabilities, item => item.Key == PlatformFeatureKeys.Tray);
        Assert.Equal(viewModel.L.Get("Integration.tray.Title"), tray.Title);
        Assert.Equal(viewModel.L.Available, tray.Status);
        Assert.True(tray.IsUsable);
        Assert.False(tray.HasUnsupportedReason);

        var hotkey = Assert.Single(viewModel.PlatformIntegrationCapabilities, item => item.Key == PlatformFeatureKeys.GlobalHotkey);
        Assert.Equal(viewModel.L.Get("Integration.global-hotkey.Title"), hotkey.Title);
        Assert.Equal(viewModel.L.PlatformLimited, hotkey.Status);
        Assert.False(hotkey.IsUsable);
        Assert.True(hotkey.HasUnsupportedReason);
        Assert.Equal("Global hotkeys need a native adapter.", hotkey.UnsupportedReason);
    }

    private static MainWindowViewModel CreateViewModel(
        string settingsPath,
        IWebDavBackupService? webDavBackupService = null,
        IPlatformIntegrationService? platformIntegrationService = null)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var factory = new SqliteConnectionFactory(databasePath);
        var migrator = new DatabaseMigrator(factory);
        platformIntegrationService ??= new PlatformIntegrationService();
        return new MainWindowViewModel(
            new MonicaRepository(factory, migrator),
            new VaultCredentialStore(factory, migrator),
            new CryptoService(),
            new TotpService(),
            new PasswordGeneratorService(),
            new ImportExportService(),
            new PlatformCapabilityService(platformIntegrationService),
            platformIntegrationService,
            new NoopClipboardService(),
            webDavBackupService ?? new NoopWebDavBackupService(),
            new MdbxVaultService(),
            new NoopPasswordAttachmentFileService(),
            new NoopPasswordEditorDialogService(),
            new NoopPasswordDetailDialogService(),
            new NoopCategoryPickerDialogService(),
            new LegacyVaultDetector(factory),
            new AppSettingsService(settingsPath),
            new LocalizationService());
    }

    private static string GetTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private sealed class NoopClipboardService : IClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopWebDavBackupService : IWebDavBackupService
    {
        public string NormalizeRemotePath(string rootPath, string relativePath) => relativePath;
        public Task<IReadOnlyList<RemoteFileEntry>> ListAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteFileEntry>>([]);
        public Task UploadTextAsync(WebDavProfile profile, string relativePath, string content, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> DownloadTextAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task DeleteAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CapturingWebDavBackupService(IReadOnlyList<RemoteFileEntry> entries) : IWebDavBackupService
    {
        public WebDavProfile? LastProfile { get; private set; }
        public string DeletedPath { get; private set; } = "";

        public string NormalizeRemotePath(string rootPath, string relativePath) => relativePath;

        public Task<IReadOnlyList<RemoteFileEntry>> ListAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default)
        {
            LastProfile = profile;
            return Task.FromResult(entries);
        }

        public Task UploadTextAsync(WebDavProfile profile, string relativePath, string content, CancellationToken cancellationToken = default)
        {
            LastProfile = profile;
            return Task.CompletedTask;
        }

        public Task<string> DownloadTextAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default)
        {
            LastProfile = profile;
            return Task.FromResult("");
        }

        public Task DeleteAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default)
        {
            LastProfile = profile;
            DeletedPath = relativePath;
            return Task.CompletedTask;
        }
    }

    private sealed class NoopPasswordEditorDialogService : IPasswordEditorDialogService
    {
        public Task<PasswordEditorViewModel?> ShowAsync(
            PasswordEntry? entry,
            IReadOnlyList<Category> categories,
            string plainPassword,
            IReadOnlyList<string>? siblingPasswords = null,
            IReadOnlyList<SecureItem>? notes = null,
            IReadOnlyList<CustomField>? customFields = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PasswordEditorViewModel?>(null);
    }

    private sealed class NoopPasswordDetailDialogService : IPasswordDetailDialogService
    {
        public Task ShowAsync(
            PasswordEntry entry,
            IReadOnlyList<PasswordEntry> siblings,
            Category? category,
            SecureItem? boundNote,
            IReadOnlyList<Attachment> attachments,
            IReadOnlyList<CustomField> customFields,
            IReadOnlyList<PasswordHistoryDisplayItem> passwordHistory,
            Func<PasswordEntry, Task>? addAttachment,
            Func<Attachment, Task>? deleteAttachment,
            Func<PasswordHistoryEntry, Task>? deletePasswordHistory,
            Func<long, Task>? clearPasswordHistory,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopPasswordAttachmentFileService : IPasswordAttachmentFileService
    {
        public Task<PasswordAttachmentFileDraft?> PickAndStoreAttachmentAsync(PasswordEntry entry, CancellationToken cancellationToken = default) =>
            Task.FromResult<PasswordAttachmentFileDraft?>(null);

        public Task DeleteStoredAttachmentAsync(string storagePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopCategoryPickerDialogService : ICategoryPickerDialogService
    {
        public Task<PasswordCategoryChoice?> ShowAsync(
            IReadOnlyList<Category> categories,
            long? selectedCategoryId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PasswordCategoryChoice?>(null);
    }
}
