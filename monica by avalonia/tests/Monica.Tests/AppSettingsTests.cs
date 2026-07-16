using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Data.Services;
using Monica.Platform.Services;
using Microsoft.Data.Sqlite;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task App_settings_migrates_legacy_disabled_webdav_backup_encryption()
    {
        var path = GetTempPath();
        await File.WriteAllTextAsync(path, "{\"WebDavBackupEncryptionEnabled\":false}");

        var settings = new AppSettingsService(path);
        await settings.LoadAsync();

        Assert.True(settings.Current.WebDavBackupEncryptionEnabled);
    }

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
    public async Task App_settings_protects_persisted_secret_values()
    {
        var path = GetTempPath();
        var protector = new ReversingSecretProtector();
        var first = new AppSettingsService(path, protector);
        await first.LoadAsync();
        first.Current.WebDavPassword = "secret-webdav-password";
        first.Current.WebDavBackupEncryptionPassword = "secret-backup-password";

        await first.SaveAsync();

        var rawJson = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("secret-webdav-password", rawJson);
        Assert.DoesNotContain("secret-backup-password", rawJson);
        Assert.Contains("secret:v1:", rawJson);

        var second = new AppSettingsService(path, protector);
        await second.LoadAsync();

        Assert.Equal("secret-webdav-password", second.Current.WebDavPassword);
        Assert.Equal("secret-backup-password", second.Current.WebDavBackupEncryptionPassword);
    }

    [Fact]
    public async Task Security_baseline_app_settings_refuses_plaintext_secret_fallback()
    {
        var path = GetTempPath();
        var settings = new AppSettingsService(path, new ThrowingSecretProtector());
        settings.Current.WebDavPassword = "must-not-reach-disk";

        await Assert.ThrowsAsync<InvalidOperationException>(() => settings.SaveAsync());

        Assert.False(File.Exists(path));
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
    public async Task App_settings_allows_high_contrast_theme()
    {
        var path = GetTempPath();
        var settings = new AppSettingsService(path);
        await settings.LoadAsync();
        settings.Current.Theme = "high-contrast";
        await settings.SaveAsync();

        var reloaded = new AppSettingsService(path);
        await reloaded.LoadAsync();

        Assert.Equal("high-contrast", reloaded.Current.Theme);
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
    public async Task App_settings_allows_mdbx_startup_section()
    {
        var path = GetTempPath();
        var settings = new AppSettingsService(path);
        await settings.LoadAsync();
        settings.Current.StartupSection = "Mdbx";
        await settings.SaveAsync();

        var reloaded = new AppSettingsService(path);
        await reloaded.LoadAsync();

        Assert.Equal("Mdbx", reloaded.Current.StartupSection);
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

        var reloaded = await LoadSettingsUntilAsync(settingsPath, settings =>
            settings.WebDavEnabled &&
            string.Equals(settings.WebDavServerUrl, "https://dav.example.com", StringComparison.Ordinal) &&
            settings.FeatureToggles.TryGetValue("passwords", out var enabled) &&
            !enabled);
        Assert.True(reloaded.Current.WebDavEnabled);
        Assert.Equal("https://dav.example.com", reloaded.Current.WebDavServerUrl);
        Assert.False(reloaded.IsFeatureEnabled("passwords"));
    }

    [Fact]
    public async Task ViewModel_exposes_high_contrast_theme_option()
    {
        var settingsPath = GetTempPath();
        var settingsService = new AppSettingsService(settingsPath);
        await settingsService.LoadAsync();
        settingsService.Current.Theme = "high-contrast";
        await settingsService.SaveAsync();

        var viewModel = CreateViewModel(settingsPath);
        await viewModel.InitializeAsync();

        Assert.Equal("high-contrast", viewModel.SettingsTheme);
        Assert.Contains(viewModel.ThemeOptions, option =>
            string.Equals(option.Value?.ToString(), "high-contrast", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(option.Label));
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
    public async Task ViewModel_creates_local_mdbx_metadata_without_duplicates()
    {
        var viewModel = CreateViewModel(GetTempPath());

        await viewModel.CreateMdbxVaultCommand.ExecuteAsync(null);
        await viewModel.CreateMdbxVaultCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.MdbxDatabaseItems);
        Assert.Single(viewModel.MdbxDatabases);
        Assert.True(viewModel.HasMdbxDatabases);
        Assert.True(item.IsLocal);
        Assert.True(item.IsDefault);
        Assert.Equal(1, viewModel.MdbxLocalDatabaseCount);
        Assert.Equal(0, viewModel.MdbxWebDavDatabaseCount);
    }

    [Fact]
    public async Task ViewModel_registers_remote_mdbx_sources_for_page_state()
    {
        var webDav = new CapturingWebDavBackupService([]);
        var oneDrive = new RecordingOneDriveBackupService();
        var viewModel = CreateViewModel(
            GetTempPath(),
            webDavBackupService: webDav,
            oneDriveBackupService: oneDrive);
        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com";
        viewModel.WebDavUsername = "user";
        viewModel.WebDavPassword = "secret";
        viewModel.WebDavRemotePath = "/Monica";
        viewModel.OneDriveEnabled = true;

        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);
        await viewModel.CreateOneDriveMdbxVaultCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.MdbxDatabaseItems.Count);
        Assert.Equal(1, viewModel.MdbxWebDavDatabaseCount);
        Assert.Equal(1, viewModel.MdbxOneDriveDatabaseCount);
        Assert.All(viewModel.MdbxDatabaseItems, item => Assert.True(item.IsRemote));
        Assert.Contains(viewModel.MdbxDatabaseItems, item => item.RemotePath == "/Monica/local.mdbx");
        Assert.All(viewModel.MdbxDatabases, database =>
        {
            Assert.False(string.IsNullOrWhiteSpace(database.EncryptedPassword));
            Assert.False(string.IsNullOrWhiteSpace(database.WorkingCopyPath));
            Assert.True(File.Exists(database.WorkingCopyPath));
        });
        var mdbxSources = viewModel.VaultSources.Where(source => source.Kind == "MDBX").ToArray();
        Assert.Equal(2, mdbxSources.Length);
        foreach (var database in viewModel.MdbxDatabases)
        {
            var source = Assert.Single(mdbxSources, source => source.DisplayName == database.Name);
            Assert.Equal(database.WorkingCopyPath, source.LocalPath);
            Assert.Equal(database.FilePath, source.RemoteUrl);
        }

        var webDavDatabase = Assert.Single(
            viewModel.MdbxDatabases,
            database => database.StorageLocation == MdbxStorageLocation.RemoteWebDav);
        Assert.Equal("MDBX-1", await ReadMdbxFormatVersionAsync(webDavDatabase.WorkingCopyPath!));
        Assert.Equal(SyncStatus.Synced, webDavDatabase.LastSyncStatus);
        Assert.NotNull(webDavDatabase.LastSyncedAt);
        Assert.Equal(webDavDatabase.FilePath, webDav.UploadedBinaryPath);
        Assert.NotEmpty(webDav.UploadedBytes);
        Assert.Equal(webDav.RemoteVersion.ETag, webDavDatabase.RemoteETag);
        Assert.Equal(webDav.RemoteVersion.LastModified, webDavDatabase.RemoteLastModifiedAt);

        var webDavItem = Assert.Single(
            viewModel.MdbxDatabaseItems,
            item => item.Database.StorageLocation == MdbxStorageLocation.RemoteWebDav);
        await viewModel.OpenMdbxDatabaseCommand.ExecuteAsync(webDavItem);

        Assert.Equal(SyncStatus.Synced, webDavDatabase.LastSyncStatus);
        Assert.StartsWith("Opened ", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ViewModel_downloads_missing_webdav_mdbx_working_copy_before_opening()
    {
        var webDav = new CapturingWebDavBackupService([]);
        var viewModel = CreateViewModel(GetTempPath(), webDavBackupService: webDav);
        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com";
        viewModel.WebDavUsername = "user";
        viewModel.WebDavPassword = "secret";
        viewModel.WebDavRemotePath = "/Monica";
        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);
        var item = Assert.Single(viewModel.MdbxDatabaseItems);
        var workingCopyPath = item.Database.WorkingCopyPath!;
        webDav.DownloadBytes = webDav.UploadedBytes;
        SqliteConnection.ClearAllPools();
        File.Delete(workingCopyPath);

        await viewModel.OpenMdbxDatabaseCommand.ExecuteAsync(item);

        Assert.True(File.Exists(workingCopyPath));
        Assert.Equal(webDav.DownloadBytes, await File.ReadAllBytesAsync(workingCopyPath));
        Assert.Equal(item.Database.FilePath, webDav.DownloadedBinaryPath);
        Assert.Equal(SyncStatus.Synced, item.Database.LastSyncStatus);
        Assert.NotNull(item.Database.LastSyncedAt);
        Assert.StartsWith("Opened ", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ViewModel_preserves_webdav_mdbx_working_copy_when_download_is_invalid()
    {
        var webDav = new CapturingWebDavBackupService([]);
        var viewModel = CreateViewModel(GetTempPath(), webDavBackupService: webDav);
        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com";
        viewModel.WebDavUsername = "user";
        viewModel.WebDavPassword = "secret";
        viewModel.WebDavRemotePath = "/Monica";
        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);
        var item = Assert.Single(viewModel.MdbxDatabaseItems);
        var workingCopyPath = item.Database.WorkingCopyPath!;
        SqliteConnection.ClearAllPools();
        var originalBytes = await File.ReadAllBytesAsync(workingCopyPath);
        webDav.DownloadBytes = [1, 2, 3, 4];

        await viewModel.SyncMdbxDatabaseCommand.ExecuteAsync(item);

        SqliteConnection.ClearAllPools();
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(workingCopyPath));
        var currentDatabase = Assert.Single(viewModel.MdbxDatabases);
        Assert.Equal(SyncStatus.Failed, currentDatabase.LastSyncStatus);
        Assert.False(string.IsNullOrWhiteSpace(currentDatabase.LastSyncError));
        Assert.Equal(viewModel.L.Get("Failed"), Assert.Single(viewModel.MdbxDatabaseItems).SyncStatus);
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(workingCopyPath)!,
            $".{Path.GetFileName(workingCopyPath)}.*.download"));
    }

    [Fact]
    public async Task ViewModel_retries_pending_webdav_mdbx_upload_after_failure()
    {
        var webDav = new CapturingWebDavBackupService([])
        {
            UploadBinaryFailure = new InvalidOperationException("fixture upload failure")
        };
        var viewModel = CreateViewModel(GetTempPath(), webDavBackupService: webDav);
        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com";
        viewModel.WebDavUsername = "user";
        viewModel.WebDavPassword = "secret";
        viewModel.WebDavRemotePath = "/Monica";

        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);

        var database = Assert.Single(viewModel.MdbxDatabases);
        Assert.Equal(SyncStatus.PendingUpload, database.LastSyncStatus);
        Assert.Contains("fixture upload failure", database.LastSyncError, StringComparison.Ordinal);
        webDav.UploadBinaryFailure = null;

        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);

        Assert.Equal(SyncStatus.Synced, database.LastSyncStatus);
        Assert.Null(database.LastSyncError);
        Assert.NotEmpty(webDav.UploadedBytes);
    }

    [Fact]
    public async Task ViewModel_refreshes_persisted_mdbx_status_before_choosing_sync_direction()
    {
        var databasePath = TestTempPaths.CreateFilePath(".db");
        var factory = new SqliteConnectionFactory(databasePath);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var webDav = new CapturingWebDavBackupService([]);
        var viewModel = CreateViewModel(
            GetTempPath(),
            webDavBackupService: webDav,
            repository: repository);
        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com";
        viewModel.WebDavUsername = "user";
        viewModel.WebDavPassword = "secret";
        viewModel.WebDavRemotePath = "/Monica";
        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);
        var staleItem = Assert.Single(viewModel.MdbxDatabaseItems);
        Assert.Equal(SyncStatus.Synced, staleItem.Database.LastSyncStatus);

        var persisted = Assert.Single(await repository.GetMdbxDatabasesAsync());
        persisted.LastSyncStatus = SyncStatus.PendingUpload;
        await repository.SaveMdbxDatabaseAsync(persisted);

        await viewModel.SyncMdbxDatabaseCommand.ExecuteAsync(staleItem);

        Assert.Equal(2, webDav.UploadBinaryCallCount);
        Assert.Empty(webDav.DownloadedBinaryPath);
        Assert.Equal(SyncStatus.Synced, Assert.Single(viewModel.MdbxDatabases).LastSyncStatus);
    }

    [Fact]
    public async Task ViewModel_uses_distinct_working_copies_for_webdav_mdbx_sources()
    {
        var webDav = new CapturingWebDavBackupService([]);
        var viewModel = CreateViewModel(GetTempPath(), webDavBackupService: webDav);
        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com/dav";
        viewModel.WebDavUsername = "user";
        viewModel.WebDavPassword = "secret";
        viewModel.WebDavRemotePath = "/Monica";
        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);
        viewModel.WebDavRemotePath = "/Team";

        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.MdbxDatabases.Count);
        Assert.Equal(
            2,
            viewModel.MdbxDatabases
                .Select(database => database.WorkingCopyPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.All(viewModel.MdbxDatabases, database => Assert.Equal(SyncStatus.Synced, database.LastSyncStatus));
    }

    [Fact]
    public async Task ViewModel_updates_mdbx_management_summaries()
    {
        var webDav = new CapturingWebDavBackupService([]);
        var oneDrive = new RecordingOneDriveBackupService();
        var viewModel = CreateViewModel(
            GetTempPath(),
            webDavBackupService: webDav,
            oneDriveBackupService: oneDrive);
        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com";
        viewModel.WebDavUsername = "user";
        viewModel.WebDavPassword = "secret";
        viewModel.WebDavRemotePath = "/Monica";
        viewModel.OneDriveEnabled = true;

        await viewModel.CreateMdbxVaultCommand.ExecuteAsync(null);
        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);
        await viewModel.CreateOneDriveMdbxVaultCommand.ExecuteAsync(null);

        Assert.Equal(3, viewModel.MdbxDatabaseItems.Count);
        Assert.Equal(3, viewModel.MdbxWorkingCopyCount);
        Assert.Equal(3, viewModel.MdbxOfflineCopyCount);
        Assert.Equal(2, viewModel.MdbxRemoteDatabaseCount);
        Assert.Equal(0, viewModel.MdbxPendingSyncCount);
        Assert.Contains(viewModel.L.Get("MdbxWorkingCopies"), viewModel.MdbxHealthItems.Select(item => item.Label));
        Assert.Contains(viewModel.L.Get("MdbxRemoteSources"), viewModel.MdbxHealthItems.Select(item => item.Label));
        Assert.All(viewModel.MdbxDatabaseItems, item => Assert.Equal(viewModel.L.Get("MdbxWorkingCopyReady"), item.WorkingCopyStatus));
        Assert.Equal(viewModel.L.Get("MdbxNoSyncErrors"), viewModel.MdbxSyncDiagnosticsSummaryText);
    }

    [Fact]
    public async Task ViewModel_opening_remote_mdbx_preserves_sync_status()
    {
        var webDav = new CapturingWebDavBackupService([]);
        var viewModel = CreateViewModel(GetTempPath(), webDavBackupService: webDav);
        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com";
        viewModel.WebDavUsername = "user";
        viewModel.WebDavPassword = "secret";
        viewModel.WebDavRemotePath = "/Monica";
        await viewModel.CreateWebDavMdbxVaultCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.MdbxDatabaseItems);
        item.Database.LastSyncStatus = SyncStatus.Synced;
        var previousAccessedAt = item.Database.LastAccessedAt;

        await viewModel.OpenMdbxDatabaseCommand.ExecuteAsync(item);

        Assert.Equal(SyncStatus.Synced, viewModel.MdbxDatabases.Single().LastSyncStatus);
        Assert.True(viewModel.MdbxDatabases.Single().LastAccessedAt >= previousAccessedAt);
        Assert.Equal(viewModel.L.Get("Synced"), Assert.Single(viewModel.VaultSources, source => source.DisplayName == item.Name).SyncStatus);
    }

    [Fact]
    public async Task ViewModel_tests_webdav_connection_without_changing_backup_history()
    {
        var webDav = new CapturingWebDavBackupService(
        [
            new RemoteFileEntry("/Monica/monica_backup_20260607.monica.json", false, 120, DateTimeOffset.UtcNow)
        ]);
        var viewModel = CreateViewModel(GetTempPath(), webDavBackupService: webDav);
        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com";
        viewModel.WebDavUsername = "user";
        viewModel.WebDavPassword = "secret";
        viewModel.WebDavRemotePath = "/Monica";

        await viewModel.TestWebDavConnectionCommand.ExecuteAsync(null);

        Assert.Equal(1, webDav.ListCallCount);
        Assert.Equal("/Monica", webDav.LastProfile?.RootPath);
        Assert.Empty(viewModel.WebDavBackupHistory);
        Assert.Contains("1", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(viewModel.SyncHealthItems, item => item.Label == viewModel.L.WebDav);
    }

    [Fact]
    public async Task ViewModel_serializes_webdav_operations_across_commands()
    {
        var webDav = new BlockingWebDavBackupService();
        var viewModel = CreateViewModel(GetTempPath(), webDavBackupService: webDav);
        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com";

        var first = viewModel.LoadWebDavBackupsCommand.ExecuteAsync(null);
        await webDav.ListStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(viewModel.IsWebDavBusy);

        var second = viewModel.TestWebDavConnectionCommand.ExecuteAsync(null);
        await Task.Delay(50);

        Assert.Equal(1, webDav.ListCallCount);
        Assert.Equal(viewModel.L.Get("WebDavOperationInProgress"), viewModel.StatusMessage);

        webDav.ReleaseList.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.False(viewModel.IsWebDavBusy);
    }

    [Fact]
    public async Task ViewModel_contains_mdbx_failures_and_recovers_busy_state()
    {
        var viewModel = CreateViewModel(GetTempPath(), mdbxVaultService: new FailingMdbxVaultService());

        await viewModel.CreateMdbxVaultCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsMdbxBusy);
        Assert.Empty(viewModel.MdbxDatabases);
        Assert.Equal(viewModel.L.Format("MdbxOperationFailedFormat", viewModel.L.Get("MdbxOperationCreate"), "fixture failure"), viewModel.StatusMessage);
    }

    [Fact]
    public void ViewModel_opens_database_import_and_export_in_sync_workspace()
    {
        var viewModel = CreateViewModel(GetTempPath());

        viewModel.OpenSyncWorkspacePageCommand.Execute("Export");

        Assert.Equal("Sync", viewModel.SelectedSection);
        Assert.Equal("Export", viewModel.SelectedSyncPage);
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

    [Fact]
    public async Task ViewModel_opens_about_repository_through_platform_service()
    {
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.ExternalLinks, "External links work.")
        ]);
        var externalLinks = new CapturingExternalLinkService(integration);
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            externalLinkService: externalLinks);

        await viewModel.InitializeAsync();
        await viewModel.OpenGitHubRepositoryCommand.ExecuteAsync(null);

        Assert.True(viewModel.CanOpenExternalLinks);
        Assert.Equal(MainWindowViewModel.GitHubRepositoryUrl, externalLinks.OpenedUri?.AbsoluteUri);
        Assert.Equal(viewModel.L.Get("GitHubRepositoryOpened"), viewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_imports_monica_json_from_file_picker()
    {
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "File picking works.")
        ]);
        var json = new ImportExportService().ExportJson(
            [new PasswordEntry { Title = "File imported login", Username = "dev", Password = "secret" }],
            []);
        var filePicker = new CapturingFileSystemPickerService(
            integration,
            new PickedTextFile("monica.json", json));
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            fileSystemPickerService: filePicker);

        await viewModel.InitializeAsync();
        await viewModel.ImportMonicaJsonFileCommand.ExecuteAsync(null);

        var imported = Assert.Single(viewModel.Passwords);
        Assert.Equal("File imported login", imported.Title);
        Assert.Equal("", viewModel.ImportJsonText);
        Assert.Equal(viewModel.L.Format("ImportedMonicaJsonFormat", 1, 0), viewModel.StatusMessage);
        Assert.Equal("Monica JSON", Assert.Single(filePicker.OpenFileTypes).Name);
        Assert.Equal("*.json", Assert.Single(Assert.Single(filePicker.OpenFileTypes).Patterns));
    }

    [Fact]
    public async Task ViewModel_imports_aegis_json_from_file_picker()
    {
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "File picking works.")
        ]);
        var json = new ImportExportService().ExportAegisJson(
        [
            new SecureItem
            {
                ItemType = VaultItemType.Totp,
                Title = "GitHub",
                Notes = "work account",
                ItemData = TotpDataResolver.ToItemData(new TotpData("JBSWY3DPEHPK3PXP", "GitHub", "dev@example.com"))
            }
        ]);
        var filePicker = new CapturingFileSystemPickerService(
            integration,
            new PickedTextFile("aegis.json", json));
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            fileSystemPickerService: filePicker);

        await viewModel.ImportAegisJsonFileCommand.ExecuteAsync(null);

        var imported = Assert.Single(viewModel.TotpItems);
        Assert.Equal("dev@example.com", imported.Title);
        Assert.Equal("", viewModel.ImportAegisJsonText);
        Assert.Equal(viewModel.L.Format("ImportedAegisJsonFormat", 1, 0), viewModel.StatusMessage);
        Assert.Equal("Aegis JSON", Assert.Single(filePicker.OpenFileTypes).Name);
        Assert.Equal("*.json", Assert.Single(Assert.Single(filePicker.OpenFileTypes).Patterns));
    }

    [Fact]
    public async Task ViewModel_imports_totp_csv_from_file_picker()
    {
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "File picking works.")
        ]);
        var csv = new ImportExportService().ExportTotpCsv(
        [
            new SecureItem
            {
                ItemType = VaultItemType.Totp,
                Title = "GitHub",
                Notes = "work account",
                ItemData = TotpDataResolver.ToItemData(new TotpData("JBSWY3DPEHPK3PXP", "GitHub", "dev@example.com"))
            }
        ]);
        var filePicker = new CapturingFileSystemPickerService(
            integration,
            new PickedTextFile("totp.csv", csv));
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            fileSystemPickerService: filePicker);

        await viewModel.ImportTotpCsvFileCommand.ExecuteAsync(null);

        var imported = Assert.Single(viewModel.TotpItems);
        Assert.Equal("GitHub", imported.Title);
        Assert.Equal("", viewModel.ImportTotpCsvText);
        Assert.Equal(viewModel.L.Format("ImportedTotpCsvFormat", 1, 0), viewModel.StatusMessage);
        Assert.Equal("TOTP CSV", Assert.Single(filePicker.OpenFileTypes).Name);
        Assert.Equal("*.csv", Assert.Single(Assert.Single(filePicker.OpenFileTypes).Patterns));
    }

    [Fact]
    public async Task ViewModel_imports_note_csv_from_file_picker()
    {
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "File picking works.")
        ]);
        var payload = NoteContentCodec.BuildSavePayload("Recovery", "# backup codes\nalpha", "ops", true, ["inline.png"]);
        var csv = new ImportExportService().ExportNoteCsv(
        [
            new SecureItem
            {
                ItemType = VaultItemType.Note,
                Title = payload.Title,
                Notes = payload.NotesCache,
                ImagePaths = payload.ImagePaths,
                ItemData = payload.ItemData
            }
        ]);
        var filePicker = new CapturingFileSystemPickerService(
            integration,
            new PickedTextFile("notes.csv", csv));
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            fileSystemPickerService: filePicker);

        await viewModel.ImportNoteCsvFileCommand.ExecuteAsync(null);

        var imported = Assert.Single(viewModel.NoteItems);
        Assert.Equal("Recovery", imported.Title);
        Assert.Equal("", viewModel.ImportNoteCsvText);
        Assert.Equal(viewModel.L.Format("ImportedNoteCsvFormat", 1, 0), viewModel.StatusMessage);
        Assert.Equal("Notes CSV", Assert.Single(filePicker.OpenFileTypes).Name);
        Assert.Equal("*.csv", Assert.Single(Assert.Single(filePicker.OpenFileTypes).Patterns));
    }

    [Fact]
    public async Task ViewModel_saves_password_csv_export_through_file_picker()
    {
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "File picking works.")
        ]);
        var filePicker = new CapturingFileSystemPickerService(integration, null, "passwords.csv");
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            fileSystemPickerService: filePicker);
        viewModel.ImportCsvText = "title,website,username,password\r\nCSV export login,https://example.com,dev,plain-secret";
        await viewModel.ImportPasswordCsvCommand.ExecuteAsync(null);

        await viewModel.SavePasswordCsvExportCommand.ExecuteAsync(null);

        Assert.Contains("CSV export login", filePicker.SavedContent);
        Assert.Contains("plain-secret", filePicker.SavedContent);
        Assert.EndsWith(".csv", filePicker.SuggestedFileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Password CSV", Assert.Single(filePicker.SaveFileTypes).Name);
        Assert.Equal(viewModel.L.Format("SavedExportFileFormat", "passwords.csv"), viewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_saves_totp_csv_export_through_file_picker()
    {
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "File picking works.")
        ]);
        var filePicker = new CapturingFileSystemPickerService(integration, null, "totp.csv");
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            fileSystemPickerService: filePicker);
        viewModel.ImportTotpCsvText = new ImportExportService().ExportTotpCsv(
        [
            new SecureItem
            {
                Title = "GitHub",
                Notes = "work account",
                ItemType = VaultItemType.Totp,
                ItemData = TotpDataResolver.ToItemData(new TotpData("JBSWY3DPEHPK3PXP", "GitHub", "dev@example.com"))
            }
        ]);
        await viewModel.ImportTotpCsvCommand.ExecuteAsync(null);

        await viewModel.SaveTotpCsvExportCommand.ExecuteAsync(null);

        Assert.Contains("ID,Type,Title,Data,Notes,IsFavorite,ImagePaths,CreatedAt,UpdatedAt", filePicker.SavedContent);
        Assert.Contains("TOTP", filePicker.SavedContent);
        Assert.Contains("JBSWY3DPEHPK3PXP", filePicker.SavedContent);
        Assert.EndsWith(".csv", filePicker.SuggestedFileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("TOTP CSV", Assert.Single(filePicker.SaveFileTypes).Name);
        Assert.Equal(viewModel.L.Format("SavedExportFileFormat", "totp.csv"), viewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_saves_note_csv_export_through_file_picker()
    {
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "File picking works.")
        ]);
        var filePicker = new CapturingFileSystemPickerService(integration, null, "notes.csv");
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            fileSystemPickerService: filePicker);
        var payload = NoteContentCodec.BuildSavePayload("Recovery", "# backup codes\nalpha", "ops", true, ["inline.png"]);
        viewModel.ImportNoteCsvText = new ImportExportService().ExportNoteCsv(
        [
            new SecureItem
            {
                Title = payload.Title,
                Notes = payload.NotesCache,
                ImagePaths = payload.ImagePaths,
                ItemType = VaultItemType.Note,
                ItemData = payload.ItemData
            }
        ]);
        await viewModel.ImportNoteCsvCommand.ExecuteAsync(null);

        await viewModel.SaveNoteCsvExportCommand.ExecuteAsync(null);

        Assert.Contains("ID,Type,Title,Data,Notes,IsFavorite,ImagePaths,CreatedAt,UpdatedAt", filePicker.SavedContent);
        Assert.Contains("NOTE", filePicker.SavedContent);
        Assert.Contains("backup codes", filePicker.SavedContent);
        Assert.Contains("inline.png", filePicker.SavedContent);
        Assert.EndsWith(".csv", filePicker.SuggestedFileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Notes CSV", Assert.Single(filePicker.SaveFileTypes).Name);
        Assert.Equal(viewModel.L.Format("SavedExportFileFormat", "notes.csv"), viewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_saves_aegis_json_export_through_file_picker()
    {
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "File picking works.")
        ]);
        var filePicker = new CapturingFileSystemPickerService(integration, null, "totp-aegis.json");
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            fileSystemPickerService: filePicker);
        viewModel.ImportAegisJsonText = new ImportExportService().ExportAegisJson(
        [
            new SecureItem
            {
                Title = "GitHub",
                Notes = "work account",
                ItemType = VaultItemType.Totp,
                ItemData = TotpDataResolver.ToItemData(new TotpData("JBSWY3DPEHPK3PXP", "GitHub", "dev@example.com"))
            }
        ]);
        await viewModel.ImportAegisJsonCommand.ExecuteAsync(null);

        await viewModel.SaveAegisJsonExportCommand.ExecuteAsync(null);

        Assert.Contains("\"db\"", filePicker.SavedContent);
        Assert.Contains("JBSWY3DPEHPK3PXP", filePicker.SavedContent);
        Assert.EndsWith(".json", filePicker.SuggestedFileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Aegis JSON", Assert.Single(filePicker.SaveFileTypes).Name);
        Assert.Equal(viewModel.L.Format("SavedExportFileFormat", "totp-aegis.json"), viewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_disables_platform_limited_desktop_settings()
    {
        var settingsPath = GetTempPath();
        var settings = new AppSettingsService(settingsPath);
        await settings.LoadAsync();
        settings.Current.MinimizeToTray = true;
        settings.Current.BrowserIntegrationEnabled = true;
        await settings.SaveAsync();

        var integration = new PlatformIntegrationService("LimitedOS",
        [
            PlatformIntegrationService.Unsupported(PlatformFeatureKeys.Tray, "Tray is not available here."),
            PlatformIntegrationService.PlatformLimited(PlatformFeatureKeys.GlobalHotkey, "Global hotkeys need compositor support."),
            PlatformIntegrationService.Unsupported(PlatformFeatureKeys.BrowserBridge, "Browser bridge is not available here.")
        ]);
        var viewModel = CreateViewModel(settingsPath, platformIntegrationService: integration);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.CanUseTrayIntegration);
        Assert.False(viewModel.MinimizeToTray);
        Assert.Contains("Tray is not available here.", viewModel.TrayIntegrationStatusText);
        Assert.False(viewModel.CanUseGlobalHotkeyIntegration);
        Assert.Contains("Global hotkeys need compositor support.", viewModel.GlobalHotkeyIntegrationStatusText);
        Assert.False(viewModel.CanUseBrowserBridgeIntegration);
        Assert.False(viewModel.BrowserIntegrationEnabled);
        Assert.Contains("Browser bridge is not available here.", viewModel.BrowserBridgeIntegrationStatusText);

        viewModel.MinimizeToTray = true;
        viewModel.BrowserIntegrationEnabled = true;
        await Task.Delay(250);

        var reloaded = new AppSettingsService(settingsPath);
        await reloaded.LoadAsync();
        Assert.False(viewModel.MinimizeToTray);
        Assert.False(viewModel.BrowserIntegrationEnabled);
        Assert.False(reloaded.Current.MinimizeToTray);
        Assert.False(reloaded.Current.BrowserIntegrationEnabled);
    }

    [Fact]
    public async Task ViewModel_saves_security_questions_as_hashed_recovery_settings()
    {
        var settingsPath = GetTempPath();
        var viewModel = CreateViewModel(settingsPath);
        await viewModel.InitializeAsync();

        viewModel.SecurityRecoveryEnabled = true;
        viewModel.SecurityQuestion1Id = 11;
        viewModel.SecurityQuestion1Answer = "  Tiga ";
        viewModel.SecurityQuestion2Id = SecurityQuestionService.CustomQuestionId;
        viewModel.SecurityQuestion2CustomText = "Favorite shell?";
        viewModel.SecurityQuestion2Answer = "PowerShell";
        await viewModel.SaveSecurityQuestionsCommand.ExecuteAsync(null);
        await Task.Delay(250);

        var reloaded = new AppSettingsService(settingsPath);
        await reloaded.LoadAsync();
        var recovery = reloaded.Current.SecurityRecovery;
        var securityQuestions = new SecurityQuestionService();

        Assert.True(recovery.HasCompleteSetup);
        Assert.Equal(11, recovery.Question1Id);
        Assert.Equal(SecurityQuestionService.CustomQuestionId, recovery.Question2Id);
        Assert.Equal("Favorite shell?", recovery.Question2Text);
        Assert.DoesNotContain("Tiga", recovery.Question1AnswerHash, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PowerShell", recovery.Question2AnswerHash, StringComparison.OrdinalIgnoreCase);
        Assert.True(securityQuestions.VerifyAnswer("tiga", recovery.Question1AnswerHash, recovery.Question1AnswerSalt));
        Assert.True(securityQuestions.VerifyAnswer("powershell", recovery.Question2AnswerHash, recovery.Question2AnswerSalt));
        Assert.Equal("", viewModel.SecurityQuestion1Answer);
        Assert.Equal("", viewModel.SecurityQuestion2Answer);
        Assert.Equal(viewModel.L.Get("SecurityQuestionsSaved"), viewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_changes_master_password_through_settings_command()
    {
        var maintenance = new CapturingMasterPasswordMaintenanceService(
            new MasterPasswordMaintenanceResult(true, "ok", PasswordsReencrypted: 2, PasswordHistoryEntriesReencrypted: 1, MdbxSecretsReencrypted: 1));
        var viewModel = CreateViewModel(GetTempPath(), masterPasswordMaintenanceService: maintenance);
        await viewModel.InitializeAsync();
        viewModel.IsUnlocked = true;
        viewModel.CurrentMasterPassword = "old password";
        viewModel.NewMasterPassword = "new password";
        viewModel.ConfirmNewMasterPassword = "new password";

        await viewModel.ChangeMasterPasswordCommand.ExecuteAsync(null);

        Assert.Equal("old password", maintenance.CurrentPassword);
        Assert.Equal("new password", maintenance.NewPassword);
        Assert.Equal("", viewModel.CurrentMasterPassword);
        Assert.Equal("", viewModel.NewMasterPassword);
        Assert.Equal("", viewModel.ConfirmNewMasterPassword);
        Assert.Equal(viewModel.L.Format("MasterPasswordChangedFormat", 4), viewModel.StatusMessage);
        Assert.False(viewModel.IsChangingMasterPassword);
    }

    [Fact]
    public async Task ViewModel_resets_master_password_after_security_answers()
    {
        var maintenance = new CapturingMasterPasswordMaintenanceService(
            new MasterPasswordMaintenanceResult(true, "ok", PasswordsReencrypted: 2, PasswordHistoryEntriesReencrypted: 1, MdbxSecretsReencrypted: 1));
        var viewModel = CreateViewModel(GetTempPath(), masterPasswordMaintenanceService: maintenance);
        await viewModel.InitializeAsync();
        viewModel.IsUnlocked = true;
        viewModel.SecurityRecoveryEnabled = true;
        viewModel.SecurityQuestion1Id = 11;
        viewModel.SecurityQuestion1Answer = "Tiga";
        viewModel.SecurityQuestion2Id = SecurityQuestionService.CustomQuestionId;
        viewModel.SecurityQuestion2CustomText = "Favorite shell?";
        viewModel.SecurityQuestion2Answer = "PowerShell";
        await viewModel.SaveSecurityQuestionsCommand.ExecuteAsync(null);

        viewModel.SecurityRecoveryAnswer1 = "tiga";
        viewModel.SecurityRecoveryAnswer2 = "powershell";
        viewModel.RecoveryNewMasterPassword = "new password";
        viewModel.RecoveryConfirmNewMasterPassword = "new password";
        await viewModel.ResetMasterPasswordWithSecurityQuestionsCommand.ExecuteAsync(null);

        Assert.True(maintenance.WasReset);
        Assert.Equal("new password", maintenance.NewPassword);
        Assert.Equal("", viewModel.SecurityRecoveryAnswer1);
        Assert.Equal("", viewModel.SecurityRecoveryAnswer2);
        Assert.Equal("", viewModel.RecoveryNewMasterPassword);
        Assert.Equal("", viewModel.RecoveryConfirmNewMasterPassword);
        Assert.Equal(viewModel.L.Format("ResetMasterPasswordChangedFormat", 4), viewModel.StatusMessage);
        Assert.False(viewModel.IsResettingMasterPassword);
    }

    [Fact]
    public async Task ViewModel_blocks_security_question_reset_when_recovery_is_disabled()
    {
        var maintenance = new CapturingMasterPasswordMaintenanceService(
            new MasterPasswordMaintenanceResult(true, "ok", PasswordsReencrypted: 1));
        var viewModel = CreateViewModel(GetTempPath(), masterPasswordMaintenanceService: maintenance);
        await viewModel.InitializeAsync();
        viewModel.IsUnlocked = true;
        viewModel.SecurityRecoveryEnabled = true;
        viewModel.SecurityQuestion1Id = 11;
        viewModel.SecurityQuestion1Answer = "Tiga";
        viewModel.SecurityQuestion2Id = 1;
        viewModel.SecurityQuestion2Answer = "Monica";
        await viewModel.SaveSecurityQuestionsCommand.ExecuteAsync(null);

        viewModel.SecurityRecoveryEnabled = false;
        viewModel.SecurityRecoveryAnswer1 = "tiga";
        viewModel.SecurityRecoveryAnswer2 = "monica";
        viewModel.RecoveryNewMasterPassword = "new password";
        viewModel.RecoveryConfirmNewMasterPassword = "new password";
        await viewModel.ResetMasterPasswordWithSecurityQuestionsCommand.ExecuteAsync(null);

        Assert.False(maintenance.WasReset);
        Assert.False(viewModel.CanResetMasterPasswordWithSecurityQuestions);
        Assert.Equal(viewModel.L.Get("SecurityRecoveryDisabled"), viewModel.StatusMessage);
    }

    [Fact]
    public void ViewModel_clears_transient_security_inputs_when_leaving_settings_pages()
    {
        var viewModel = CreateViewModel(GetTempPath());
        viewModel.SelectedSettingsPage = "Security";
        viewModel.CurrentMasterPassword = "old";
        viewModel.NewMasterPassword = "new password";
        viewModel.ConfirmNewMasterPassword = "new password";

        viewModel.SelectedSettingsPage = "SecurityRecovery";

        Assert.Empty(viewModel.CurrentMasterPassword);
        Assert.Empty(viewModel.NewMasterPassword);
        Assert.Empty(viewModel.ConfirmNewMasterPassword);

        viewModel.SecurityQuestion1Answer = "one";
        viewModel.SecurityQuestion2Answer = "two";
        viewModel.SecurityRecoveryAnswer1 = "one";
        viewModel.SecurityRecoveryAnswer2 = "two";
        viewModel.RecoveryNewMasterPassword = "new password";
        viewModel.RecoveryConfirmNewMasterPassword = "new password";

        viewModel.SelectedSettingsPage = "General";

        Assert.Empty(viewModel.SecurityQuestion1Answer);
        Assert.Empty(viewModel.SecurityQuestion2Answer);
        Assert.Empty(viewModel.SecurityRecoveryAnswer1);
        Assert.Empty(viewModel.SecurityRecoveryAnswer2);
        Assert.Empty(viewModel.RecoveryNewMasterPassword);
        Assert.Empty(viewModel.RecoveryConfirmNewMasterPassword);

        viewModel.SelectedSection = "Settings";
        viewModel.SelectedSettingsPage = "Security";
        viewModel.CurrentMasterPassword = "old";
        viewModel.NewMasterPassword = "new password";
        viewModel.ConfirmNewMasterPassword = "new password";
        viewModel.SelectedSettingsPage = "SecurityRecovery";
        viewModel.SecurityQuestion1Answer = "one";
        viewModel.SecurityQuestion2Answer = "two";
        viewModel.SecurityRecoveryAnswer1 = "one";
        viewModel.SecurityRecoveryAnswer2 = "two";
        viewModel.RecoveryNewMasterPassword = "new password";
        viewModel.RecoveryConfirmNewMasterPassword = "new password";

        viewModel.SelectedSection = "Passwords";

        Assert.Empty(viewModel.CurrentMasterPassword);
        Assert.Empty(viewModel.NewMasterPassword);
        Assert.Empty(viewModel.ConfirmNewMasterPassword);
        Assert.Empty(viewModel.SecurityQuestion1Answer);
        Assert.Empty(viewModel.SecurityQuestion2Answer);
        Assert.Empty(viewModel.SecurityRecoveryAnswer1);
        Assert.Empty(viewModel.SecurityRecoveryAnswer2);
        Assert.Empty(viewModel.RecoveryNewMasterPassword);
        Assert.Empty(viewModel.RecoveryConfirmNewMasterPassword);
    }

    private static MainWindowViewModel CreateViewModel(
        string settingsPath,
        IWebDavBackupService? webDavBackupService = null,
        IPlatformIntegrationService? platformIntegrationService = null,
        IMasterPasswordMaintenanceService? masterPasswordMaintenanceService = null,
        IExternalLinkService? externalLinkService = null,
        IFileSystemPickerService? fileSystemPickerService = null,
        IMdbxVaultService? mdbxVaultService = null,
        IMonicaRepository? repository = null,
        IConfirmationDialogService? confirmationDialogService = null,
        IOneDriveBackupService? oneDriveBackupService = null,
        IKeePassVaultService? keePassVaultService = null,
        IImportExportService? importExportService = null,
        IAppSettingsService? settingsService = null,
        IClipboardService? clipboardService = null)
    {
        var databasePath = TestTempPaths.CreateFilePath(".db");
        var factory = new SqliteConnectionFactory(databasePath);
        var migrator = new DatabaseMigrator(factory);
        var crypto = new CryptoService();
        crypto.InitializeSession("test password", new byte[16]);
        platformIntegrationService ??= new PlatformIntegrationService();
        return new MainWindowViewModel(
            repository ?? new MonicaRepository(factory, migrator),
            new VaultCredentialStore(factory, migrator),
            crypto,
            new TotpService(),
            new PasswordGeneratorService(),
            importExportService ?? new ImportExportService(),
            new PlatformCapabilityService(platformIntegrationService),
            platformIntegrationService,
            clipboardService ?? new NoopClipboardService(),
            webDavBackupService ?? new NoopWebDavBackupService(),
            mdbxVaultService ?? new MdbxVaultService(new MdbxTestVaultEngine()),
            new NoopPasswordAttachmentFileService(),
            new NoopPasswordEditorDialogService(),
            new NoopPasswordDetailDialogService(),
            new NoopCategoryPickerDialogService(),
            new LegacyVaultDetector(factory),
            settingsService ?? new AppSettingsService(settingsPath),
            new LocalizationService(),
            masterPasswordMaintenanceService: masterPasswordMaintenanceService,
            externalLinkService: externalLinkService,
            fileSystemPickerService: fileSystemPickerService,
            confirmationDialogService: confirmationDialogService,
            exportAuthorizationService: new ApprovingExportAuthorizationService(),
            oneDriveBackupService: oneDriveBackupService,
            keePassVaultService: keePassVaultService);
    }

    private static string GetTempPath()
    {
        return TestTempPaths.CreateFilePath(".settings.json");
    }

    private static void ConfigureWebDav(MainWindowViewModel viewModel, string remotePath = "/Monica")
    {
        viewModel.WebDavEnabled = true;
        viewModel.WebDavServerUrl = "https://dav.example.com";
        viewModel.WebDavUsername = "user";
        viewModel.WebDavPassword = "secret";
        viewModel.WebDavRemotePath = remotePath;
    }

    private static async Task<string> ReadMdbxFormatVersionAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT format_version FROM vault_meta LIMIT 1";
        return (string)(await command.ExecuteScalarAsync() ?? "");
    }

    private static async Task<AppSettingsService> LoadSettingsUntilAsync(string path, Func<DesktopAppSettings, bool> predicate)
    {
        AppSettingsService settings = new(path);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            settings = new AppSettingsService(path);
            try
            {
                await settings.LoadAsync();
                if (predicate(settings.Current))
                {
                    return settings;
                }
            }
            catch (IOException)
            {
                // The view model saves settings asynchronously; retry if the file is momentarily locked.
            }

            await Task.Delay(100);
        }

        settings = new AppSettingsService(path);
        await settings.LoadAsync();
        Assert.True(predicate(settings.Current));
        return settings;
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

    private sealed class CapturingExternalLinkService(IPlatformIntegrationService platformIntegrationService) : IExternalLinkService
    {
        public Uri? OpenedUri { get; private set; }
        public PlatformIntegrationCapability Capability => platformIntegrationService.GetCapability(PlatformFeatureKeys.ExternalLinks);

        public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            OpenedUri = uri;
            return Task.CompletedTask;
        }
    }

    private sealed class ReversingSecretProtector : ISecretProtector
    {
        public PlatformIntegrationCapability Capability { get; } = PlatformIntegrationService.Available(
            PlatformFeatureKeys.SecretProtection,
            "Test secret protection is available.");

        public Task<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default) =>
            Task.FromResult(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(new string(plainText.Reverse().ToArray()))));

        public Task<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default)
        {
            var reversed = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedText));
            return Task.FromResult(new string(reversed.Reverse().ToArray()));
        }
    }

    private sealed class BlockingWebDavBackupService : IWebDavBackupService
    {
        public TaskCompletionSource ListStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseList { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ListCallCount { get; private set; }

        public string NormalizeRemotePath(string rootPath, string relativePath) => relativePath;

        public async Task<IReadOnlyList<RemoteFileEntry>> ListAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default)
        {
            ListCallCount++;
            ListStarted.TrySetResult();
            await ReleaseList.Task.WaitAsync(cancellationToken);
            return [];
        }

        public Task UploadTextAsync(WebDavProfile profile, string relativePath, string content, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> DownloadTextAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task DeleteAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FailingMdbxVaultService : IMdbxVaultService
    {
        public Task<LocalMdbxDatabase> CreateLocalMetadataAsync(string name, string filePath, MdbxTigaMode mode = MdbxTigaMode.Multi, CancellationToken cancellationToken = default) =>
            Task.FromException<LocalMdbxDatabase>(new InvalidOperationException("fixture failure"));

        public Task<Stream> OpenLocalStreamAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default) =>
            Task.FromException<Stream>(new InvalidOperationException("fixture failure"));
    }

    private sealed class ApprovingExportAuthorizationService : IExportAuthorizationService
    {
        public Task<bool> AuthorizeAsync(bool requireMasterPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class ThrowingSecretProtector : ISecretProtector
    {
        public PlatformIntegrationCapability Capability { get; } = PlatformIntegrationService.Unsupported(
            PlatformFeatureKeys.SecretProtection,
            "Protection unavailable.");

        public Task<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Protection unavailable.");

        public Task<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Protection unavailable.");
    }

    private sealed class CapturingFileSystemPickerService(
        IPlatformIntegrationService platformIntegrationService,
        PickedTextFile? openFile,
        string? savedFileName = null) : IFileSystemPickerService
    {
        public IReadOnlyList<PlatformFilePickerFileType> OpenFileTypes { get; private set; } = [];
        public IReadOnlyList<PlatformFilePickerFileType> SaveFileTypes { get; private set; } = [];
        public string SuggestedFileName { get; private set; } = "";
        public string SavedContent { get; private set; } = "";
        public PlatformIntegrationCapability Capability => platformIntegrationService.GetCapability(PlatformFeatureKeys.FilePicker);

        public Task<PickedTextFile?> OpenTextFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default)
        {
            OpenFileTypes = fileTypes;
            return Task.FromResult(openFile);
        }

        public Task<PickedBinaryFile?> OpenBinaryFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default)
        {
            OpenFileTypes = fileTypes;
            return Task.FromResult<PickedBinaryFile?>(null);
        }

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default)
        {
            SuggestedFileName = suggestedFileName;
            SavedContent = content;
            SaveFileTypes = fileTypes;
            return Task.FromResult(savedFileName);
        }

        public Task<string?> SaveBinaryFileAsync(string title, string suggestedFileName, ReadOnlyMemory<byte> content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default)
        {
            SuggestedFileName = suggestedFileName;
            SavedContent = Convert.ToBase64String(content.Span);
            SaveFileTypes = fileTypes;
            return Task.FromResult(savedFileName);
        }
    }

    private sealed class CapturingWebDavBackupService(IReadOnlyList<RemoteFileEntry> entries) : IWebDavBackupService
    {
        public WebDavProfile? LastProfile { get; private set; }
        public string DeletedPath { get; private set; } = "";
        public string UploadedBinaryPath { get; private set; } = "";
        public string DownloadedBinaryPath { get; private set; } = "";
        public byte[] UploadedBytes { get; private set; } = [];
        public byte[] DownloadBytes { get; set; } = [];
        public Exception? UploadBinaryFailure { get; set; }
        public Exception? DownloadBinaryFailure { get; set; }
        public RemoteFileVersion RemoteVersion { get; set; } = new(
            "\"fixture-v1\"",
            DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000),
            null);
        public RemoteWriteCondition? LastWriteCondition { get; private set; }
        public int UploadBinaryCallCount { get; private set; }
        public int ListCallCount { get; private set; }

        public string NormalizeRemotePath(string rootPath, string relativePath) => relativePath;

        public Task<IReadOnlyList<RemoteFileEntry>> ListAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default)
        {
            LastProfile = profile;
            ListCallCount++;
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

        public async Task UploadBinaryAsync(WebDavProfile profile, string relativePath, Stream content, CancellationToken cancellationToken = default)
        {
            _ = await UploadBinaryConditionallyAsync(
                profile,
                relativePath,
                content,
                RemoteWriteCondition.CreateOnly,
                cancellationToken);
        }

        public Task<RemoteFileVersion?> GetFileVersionAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<RemoteFileVersion?>(RemoteVersion);

        public async Task<RemoteFileVersion> UploadBinaryConditionallyAsync(
            WebDavProfile profile,
            string relativePath,
            Stream content,
            RemoteWriteCondition condition,
            CancellationToken cancellationToken = default)
        {
            UploadBinaryCallCount++;
            LastWriteCondition = condition;
            if (UploadBinaryFailure is not null)
            {
                throw UploadBinaryFailure;
            }

            LastProfile = profile;
            UploadedBinaryPath = relativePath;
            await using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            UploadedBytes = copy.ToArray();
            return RemoteVersion;
        }

        public async Task DownloadBinaryAsync(WebDavProfile profile, string relativePath, Stream destination, CancellationToken cancellationToken = default)
        {
            _ = await DownloadBinaryVersionedAsync(profile, relativePath, destination, cancellationToken);
        }

        public async Task<RemoteFileVersion> DownloadBinaryVersionedAsync(
            WebDavProfile profile,
            string relativePath,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            if (DownloadBinaryFailure is not null)
            {
                throw DownloadBinaryFailure;
            }

            LastProfile = profile;
            DownloadedBinaryPath = relativePath;
            await destination.WriteAsync(DownloadBytes, cancellationToken);
            return RemoteVersion;
        }

        public Task DeleteAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default)
        {
            LastProfile = profile;
            DeletedPath = relativePath;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingMasterPasswordMaintenanceService(MasterPasswordMaintenanceResult result) : IMasterPasswordMaintenanceService
    {
        public string CurrentPassword { get; private set; } = "";
        public string NewPassword { get; private set; } = "";
        public bool WasReset { get; private set; }

        public Task<MasterPasswordMaintenanceResult> ChangeMasterPasswordAsync(
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken = default)
        {
            CurrentPassword = currentPassword;
            NewPassword = newPassword;
            return Task.FromResult(result);
        }

        public Task<MasterPasswordMaintenanceResult> ResetMasterPasswordFromUnlockedVaultAsync(
            string newPassword,
            CancellationToken cancellationToken = default)
        {
            WasReset = true;
            NewPassword = newPassword;
            return Task.FromResult(result);
        }
    }

    private sealed class ApprovingConfirmationDialogService : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText,
            string? closeButtonText = null,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> ConfirmTypedAsync(
            string title,
            string message,
            string requiredPhrase,
            string instruction,
            string primaryButtonText,
            string? closeButtonText = null,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
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
            Func<PasswordEntry, Task<PasswordAttachmentAddResult>>? addAttachment,
            Func<Attachment, Task<PasswordAttachmentSaveResult>>? saveAttachment,
            Func<Attachment, Task<bool>>? deleteAttachment,
            Func<PasswordHistoryEntry, Task<bool>>? deletePasswordHistory,
            Func<long, Task<bool>>? clearPasswordHistory,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopPasswordAttachmentFileService : IPasswordAttachmentFileService
    {
        public Task<PasswordAttachmentFileDraft?> PickAttachmentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<PasswordAttachmentFileDraft?>(null);

        public Task<PasswordAttachmentFileDraft> StoreAttachmentAsync(string fileName, byte[] content, string contentType = "", CancellationToken cancellationToken = default) =>
            Task.FromResult(new PasswordAttachmentFileDraft(fileName, "", content.LongLength, contentType, content));

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
