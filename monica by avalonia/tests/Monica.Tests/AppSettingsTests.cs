using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Data.Services;
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
        viewModel.Passwords.Add(new PasswordEntry
        {
            Title = "CSV export login",
            Website = "https://example.com",
            Username = "dev",
            Password = "plain-secret"
        });

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
        viewModel.TotpItems.Add(new SecureItem
        {
            Title = "GitHub",
            Notes = "work account",
            ItemType = VaultItemType.Totp,
            ItemData = TotpDataResolver.ToItemData(new TotpData("JBSWY3DPEHPK3PXP", "GitHub", "dev@example.com"))
        });

        await viewModel.SaveTotpCsvExportCommand.ExecuteAsync(null);

        Assert.Contains("ID,Type,Title,Data,Notes,IsFavorite,ImagePaths,CreatedAt,UpdatedAt", filePicker.SavedContent);
        Assert.Contains("TOTP", filePicker.SavedContent);
        Assert.Contains("JBSWY3DPEHPK3PXP", filePicker.SavedContent);
        Assert.EndsWith(".csv", filePicker.SuggestedFileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("TOTP CSV", Assert.Single(filePicker.SaveFileTypes).Name);
        Assert.Equal(viewModel.L.Format("SavedExportFileFormat", "totp.csv"), viewModel.StatusMessage);
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
        viewModel.TotpItems.Add(new SecureItem
        {
            Title = "GitHub",
            Notes = "work account",
            ItemType = VaultItemType.Totp,
            ItemData = TotpDataResolver.ToItemData(new TotpData("JBSWY3DPEHPK3PXP", "GitHub", "dev@example.com"))
        });

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
        viewModel.SaveSecurityQuestionsCommand.Execute(null);
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
        viewModel.SaveSecurityQuestionsCommand.Execute(null);

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

    private static MainWindowViewModel CreateViewModel(
        string settingsPath,
        IWebDavBackupService? webDavBackupService = null,
        IPlatformIntegrationService? platformIntegrationService = null,
        IMasterPasswordMaintenanceService? masterPasswordMaintenanceService = null,
        IExternalLinkService? externalLinkService = null,
        IFileSystemPickerService? fileSystemPickerService = null)
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
            new LocalizationService(),
            masterPasswordMaintenanceService: masterPasswordMaintenanceService,
            externalLinkService: externalLinkService,
            fileSystemPickerService: fileSystemPickerService);
    }

    private static string GetTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
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

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default)
        {
            SuggestedFileName = suggestedFileName;
            SavedContent = content;
            SaveFileTypes = fileTypes;
            return Task.FromResult(savedFileName);
        }
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
