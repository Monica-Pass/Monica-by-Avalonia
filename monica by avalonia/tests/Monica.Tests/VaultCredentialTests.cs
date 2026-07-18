using Monica.Core.ImportExport;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.App.ViewModels;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class VaultCredentialTestCollection
{
    public const string Name = "Vault credentials";
}

[Collection(VaultCredentialTestCollection.Name)]
public sealed partial class VaultCredentialTests
{
    [Fact]
    public async Task Security_baseline_lock_clears_sensitive_view_state()
    {
        var crypto = new CryptoService();
        crypto.InitializeSession("correct horse battery staple", crypto.CreateSalt());
        var clipboard = new RecordingClipboardService();
        var settings = new AppSettingsService(GetTempSettingsPath());
        var viewModel = CreateViewModel(GetTempDatabasePath(), crypto, clipboard, settingsService: settings);
        viewModel.IsUnlocked = true;
        viewModel.Passwords.Add(new PasswordEntry { Title = "private account query", Password = "plain-secret" });
        viewModel.NoteItems.Add(new SecureItem { Title = "Recovery", ItemData = "backup-code" });
        viewModel.WalletItems.Add(new SecureItem { Title = "Card", ItemData = "4111111111111111" });
        viewModel.TotpItems.Add(new SecureItem { Title = "TOTP", ItemData = "totp-seed" });
        Assert.Single(viewModel.FilteredPasswords);
        Assert.Single(viewModel.FilteredTotpItems);
        Assert.Single(viewModel.FilteredWalletItems);
        var filteredNotesBeforeLock = viewModel.FilteredNoteItems;
        var noteGroupsBeforeLock = viewModel.NoteTreeGroups;
        Assert.Single(filteredNotesBeforeLock);
        Assert.Single(noteGroupsBeforeLock);
        viewModel.NoteContent = "# private note\n[secret](https://private.example)";
        var noteOutlineBeforeLock = viewModel.NoteOutlineItems;
        var noteReferencesBeforeLock = viewModel.NoteReferenceItems;
        Assert.Single(noteOutlineBeforeLock);
        Assert.Single(noteReferencesBeforeLock);
        viewModel.ImportCsvText = "username,password";
        viewModel.ImportAegisJsonText = AegisEncryptedTestData.Json;
        viewModel.AegisImportPassword = AegisEncryptedTestData.Password;
        viewModel.IsAegisImportPasswordRequired = true;
        viewModel.ExportPreview = "plain export";
        viewModel.GeneratedPassword = "generated-secret";
        viewModel.PasswordSearchText = "private account query";
        viewModel.PasswordSearchQuery = "private account query";
        viewModel.WebDavPassword = "webdav-secret";
        viewModel.CurrentMasterPassword = "old-master-password";
        viewModel.ToggleMasterPasswordVisibilityCommand.Execute(null);
        viewModel.ToggleConfirmMasterPasswordVisibilityCommand.Execute(null);
        await viewModel.LockCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsUnlocked);
        Assert.False(crypto.IsUnlocked);
        Assert.Empty(viewModel.Passwords);
        Assert.Empty(viewModel.NoteItems);
        Assert.Empty(viewModel.WalletItems);
        Assert.Empty(viewModel.TotpItems);
        Assert.Empty(viewModel.FilteredPasswords);
        Assert.Empty(viewModel.FilteredTotpItems);
        Assert.Empty(viewModel.FilteredWalletItems);
        Assert.Empty(viewModel.FilteredNoteItems);
        Assert.Empty(viewModel.NoteTreeGroups);
        Assert.NotSame(filteredNotesBeforeLock, viewModel.FilteredNoteItems);
        Assert.NotSame(noteGroupsBeforeLock, viewModel.NoteTreeGroups);
        Assert.Empty(viewModel.NoteOutlineItems);
        Assert.Empty(viewModel.NoteReferenceItems);
        Assert.NotSame(noteOutlineBeforeLock, viewModel.NoteOutlineItems);
        Assert.NotSame(noteReferencesBeforeLock, viewModel.NoteReferenceItems);
        Assert.Equal("", viewModel.NoteContent);
        Assert.Equal("", viewModel.ImportCsvText);
        Assert.Equal("", viewModel.ImportAegisJsonText);
        Assert.Equal("", viewModel.AegisImportPassword);
        Assert.False(viewModel.IsAegisImportPasswordRequired);
        Assert.Equal("", viewModel.ExportPreview);
        Assert.Equal("", viewModel.GeneratedPassword);
        Assert.Equal("", viewModel.PasswordSearchText);
        Assert.Equal("", viewModel.PasswordSearchQuery);
        Assert.Equal("", viewModel.WebDavPassword);
        Assert.Equal("", settings.Current.WebDavPassword);
        Assert.Equal("", viewModel.CurrentMasterPassword);
        Assert.False(viewModel.IsMasterPasswordVisible);
        Assert.False(viewModel.IsConfirmMasterPasswordVisible);
        Assert.True(clipboard.ClearOwnedContentCalled);
    }

    [Fact]
    public async Task Security_baseline_export_requires_authorization()
    {
        var crypto = new CryptoService();
        crypto.InitializeSession("correct horse battery staple", crypto.CreateSalt());
        var viewModel = CreateViewModel(
            GetTempDatabasePath(),
            crypto,
            exportAuthorizationService: new RejectingExportAuthorizationService());
        viewModel.IsUnlocked = true;

        await viewModel.ExportDataCommand.ExecuteAsync(null);

        Assert.Equal("", viewModel.ExportPreview);
        Assert.Equal(viewModel.L.Get("ExportAuthorizationFailed"), viewModel.StatusMessage);
    }

    [Fact]
    public void Default_database_path_uses_avalonia_specific_storage()
    {
        var factory = new SqliteConnectionFactory();
        var legacyWindowsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Monica",
            "monica.db");

        Assert.Equal("monica.db", Path.GetFileName(factory.DatabasePath));
        Assert.Equal("Monica by Avalonia", Path.GetFileName(Path.GetDirectoryName(factory.DatabasePath)));
        Assert.NotEqual(legacyWindowsPath, factory.DatabasePath);
    }

    [Fact]
    public async Task Credential_store_roundtrips_master_password_hash()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var store = new VaultCredentialStore(factory, new DatabaseMigrator(factory));
        var crypto = new CryptoService();
        var hash = crypto.HashMasterPassword("correct password");

        await store.SaveAsync(hash);
        var loaded = await store.GetAsync();

        Assert.NotNull(loaded);
        Assert.True(new CryptoService().VerifyMasterPassword("correct password", loaded));
        Assert.False(new CryptoService().VerifyMasterPassword("wrong password", loaded));
    }

    [Fact]
    public async Task Existing_database_can_be_written_after_legacy_readonly_detection()
    {
        var path = GetTempDatabasePath();
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE bootstrap_probe (id INTEGER PRIMARY KEY);";
            await command.ExecuteNonQueryAsync();
        }

        var factory = new SqliteConnectionFactory(path);
        var detection = await new LegacyVaultDetector(factory).DetectAsync();

        Assert.False(detection.RequiresImport);

        var store = new VaultCredentialStore(factory, new DatabaseMigrator(factory));
        var crypto = new CryptoService();
        await store.SaveAsync(crypto.HashMasterPassword("correct password"));

        var loaded = await store.GetAsync();

        Assert.NotNull(loaded);
        Assert.True(new CryptoService().VerifyMasterPassword("correct password", loaded));
    }

    [Fact]
    public async Task Unlock_coordinator_creates_and_initializes_a_new_vault()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var migrator = new DatabaseMigrator(factory);
        var coordinator = new VaultUnlockCoordinator(
            new VaultCredentialStore(factory, migrator),
            new CryptoService(),
            new LegacyVaultDetector(factory));

        var initialState = await coordinator.InitializeAsync();
        var missingPassword = await coordinator.UnlockOrCreateAsync("", "", LegacyVaultDetection.Empty);
        var mismatchedConfirmation = await coordinator.UnlockOrCreateAsync(
            "correct password",
            "different password",
            LegacyVaultDetection.Empty);
        var created = await coordinator.UnlockOrCreateAsync(
            "correct password",
            "correct password",
            LegacyVaultDetection.Empty);
        var initializedState = await coordinator.InitializeAsync();

        Assert.False(initialState.IsVaultInitialized);
        Assert.Equal(VaultUnlockStatus.MissingPassword, missingPassword.Status);
        Assert.Equal(VaultUnlockStatus.ConfirmationMismatch, mismatchedConfirmation.Status);
        Assert.Equal(VaultUnlockStatus.CreatedAndUnlocked, created.Status);
        Assert.True(initializedState.IsVaultInitialized);
    }

    [Fact]
    public async Task Unlock_coordinator_rejects_wrong_password_and_accepts_existing_password()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var migrator = new DatabaseMigrator(factory);
        var store = new VaultCredentialStore(factory, migrator);
        await store.SaveAsync(new CryptoService().HashMasterPassword("correct password"));
        var coordinator = new VaultUnlockCoordinator(
            store,
            new CryptoService(),
            new LegacyVaultDetector(factory));

        var wrong = await coordinator.UnlockOrCreateAsync("wrong password", "", LegacyVaultDetection.Empty);
        var unlocked = await coordinator.UnlockOrCreateAsync("correct password", "", LegacyVaultDetection.Empty);

        Assert.Equal(VaultUnlockStatus.WrongPassword, wrong.Status);
        Assert.Equal(VaultUnlockStatus.Unlocked, unlocked.Status);
    }

    [Fact]
    public async Task ViewModel_requires_first_run_password_confirmation()
    {
        var viewModel = CreateViewModel(GetTempDatabasePath());
        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsVaultInitialized);

        viewModel.MasterPassword = "password-one";
        viewModel.ConfirmMasterPassword = "password-two";

        Assert.False(viewModel.IsUnlocked);
        Assert.False(viewModel.UnlockCommand.CanExecute(null));
        Assert.Equal(viewModel.L.Get("ConfirmationMismatch"), viewModel.CreateVaultPasswordConfirmationStatusText);

        viewModel.ConfirmMasterPassword = "password-one";

        Assert.True(viewModel.UnlockCommand.CanExecute(null));
    }

    [Fact]
    public async Task Vault_access_sanitizes_secret_input_and_exposes_reveal_commands()
    {
        var viewModel = CreateViewModel(GetTempDatabasePath());
        await viewModel.InitializeAsync();

        Assert.False(viewModel.UnlockCommand.CanExecute(null));

        viewModel.MasterPassword = "pass\u0001word";
        Assert.Equal("password", viewModel.MasterPassword);
        Assert.False(viewModel.UnlockCommand.CanExecute(null));

        viewModel.ConfirmMasterPassword = "password";
        Assert.True(viewModel.UnlockCommand.CanExecute(null));
        Assert.Equal('*', viewModel.MasterPasswordMaskChar);
        Assert.Equal('*', viewModel.ConfirmMasterPasswordMaskChar);

        viewModel.ToggleMasterPasswordVisibilityCommand.Execute(null);
        viewModel.ToggleConfirmMasterPasswordVisibilityCommand.Execute(null);

        Assert.Equal('\0', viewModel.MasterPasswordMaskChar);
        Assert.Equal('\0', viewModel.ConfirmMasterPasswordMaskChar);
        Assert.Equal(viewModel.L.HidePassword, viewModel.ToggleMasterPasswordVisibilityLabel);
        Assert.Equal(viewModel.L.HidePassword, viewModel.ToggleConfirmMasterPasswordVisibilityLabel);
    }

    [Fact]
    public async Task Vault_access_disables_duplicate_submission_while_unlocking()
    {
        var coordinator = new BlockingVaultUnlockCoordinator();
        var viewModel = CreateViewModel(
            GetTempDatabasePath(),
            vaultUnlockCoordinator: coordinator);
        await viewModel.InitializeAsync();
        viewModel.MasterPassword = "correct password";
        viewModel.ToggleMasterPasswordVisibilityCommand.Execute(null);

        var operation = viewModel.UnlockCommand.ExecuteAsync(null);
        await coordinator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsUnlocking);
        Assert.False(viewModel.UnlockCommand.CanExecute(null));
        viewModel.UnlockCommand.Execute(null);
        Assert.Equal(1, coordinator.UnlockCallCount);

        coordinator.Complete(new VaultUnlockResult(
            VaultUnlockStatus.WrongPassword,
            true,
            "WrongMasterPassword"));
        await operation;

        Assert.False(viewModel.IsUnlocking);
        Assert.True(viewModel.HasUnlockError);
        Assert.False(viewModel.IsMasterPasswordVisible);
        Assert.Equal(1, coordinator.UnlockCallCount);
    }

    [Fact]
    public async Task Startup_initialization_begins_settings_and_vault_metadata_reads_together()
    {
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new BlockingStartupSettingsService(release.Task);
        var coordinator = new BlockingStartupVaultCoordinator(release.Task);
        var viewModel = CreateViewModel(
            GetTempDatabasePath(),
            settingsService: settings,
            vaultUnlockCoordinator: coordinator);
        viewModel.MasterPassword = "correct password";
        viewModel.ConfirmMasterPassword = "correct password";

        Assert.True(viewModel.IsVaultAccessInitializing);
        Assert.False(viewModel.IsVaultAccessReady);
        Assert.False(viewModel.UnlockCommand.CanExecute(null));
        Assert.Equal(viewModel.L.Get("PreparingVaultAccess"), viewModel.LoginTitle);

        var initialization = viewModel.InitializeAsync();
        var bothEntered = Task.WhenAll(settings.LoadEntered.Task, coordinator.InitializeEntered.Task);
        var observed = await Task.WhenAny(bothEntered, Task.Delay(250));

        Assert.True(viewModel.IsVaultAccessInitializing);
        Assert.False(viewModel.UnlockCommand.CanExecute(null));

        release.TrySetResult(null);
        await initialization;

        Assert.Same(bothEntered, observed);
        Assert.False(viewModel.IsVaultAccessInitializing);
        Assert.True(viewModel.IsVaultAccessReady);
        Assert.True(viewModel.IsVaultInitialized);
        Assert.True(viewModel.UnlockCommand.CanExecute(null));
    }

    [Fact]
    public async Task Startup_initialization_failure_releases_preparation_state_and_surfaces_error()
    {
        var viewModel = CreateViewModel(
            GetTempDatabasePath(),
            vaultUnlockCoordinator: new FailingStartupVaultCoordinator());
        viewModel.MasterPassword = "initialization secret";
        viewModel.ConfirmMasterPassword = "initialization secret";
        viewModel.ToggleMasterPasswordVisibilityCommand.Execute(null);
        viewModel.ToggleConfirmMasterPasswordVisibilityCommand.Execute(null);

        Assert.True(viewModel.IsVaultAccessInitializing);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsVaultAccessInitializing);
        Assert.True(viewModel.IsVaultAccessReady);
        Assert.True(viewModel.HasUnlockError);
        Assert.Equal(viewModel.L.Get("VaultAccessInitializationFailed"), viewModel.StatusMessage);
        Assert.NotEqual("VaultAccessInitializationFailed", viewModel.StatusMessage);
        Assert.DoesNotContain("metadata unavailable", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(viewModel.MasterPassword);
        Assert.Empty(viewModel.ConfirmMasterPassword);
        Assert.False(viewModel.IsMasterPasswordVisible);
        Assert.False(viewModel.IsConfirmMasterPasswordVisible);
    }

    [Fact]
    public async Task Vault_access_failed_result_hides_details_and_clears_secret_state()
    {
        const string sensitiveDetail = @"C:\Users\private\vault.db could not be opened";
        var viewModel = CreateViewModel(
            GetTempDatabasePath(),
            vaultUnlockCoordinator: new FailedVaultUnlockCoordinator(sensitiveDetail));
        await viewModel.InitializeAsync();
        viewModel.MasterPassword = "correct password";
        viewModel.ConfirmMasterPassword = "correct password";
        viewModel.ToggleMasterPasswordVisibilityCommand.Execute(null);
        viewModel.ToggleConfirmMasterPasswordVisibilityCommand.Execute(null);

        await viewModel.UnlockCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsUnlocked);
        Assert.True(viewModel.HasUnlockError);
        Assert.Equal(viewModel.L.Get("VaultAccessUnlockFailed"), viewModel.StatusMessage);
        Assert.NotEqual("VaultAccessUnlockFailed", viewModel.StatusMessage);
        Assert.DoesNotContain(sensitiveDetail, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Empty(viewModel.MasterPassword);
        Assert.Empty(viewModel.ConfirmMasterPassword);
        Assert.False(viewModel.IsMasterPasswordVisible);
        Assert.False(viewModel.IsConfirmMasterPasswordVisible);
    }

    [Fact]
    public async Task Vault_access_thrown_failure_hides_details_and_clears_secret_state()
    {
        const string sensitiveDetail = @"C:\Users\private\vault.key is unavailable";
        var viewModel = CreateViewModel(
            GetTempDatabasePath(),
            vaultUnlockCoordinator: new ThrowingVaultUnlockCoordinator(sensitiveDetail));
        await viewModel.InitializeAsync();
        viewModel.MasterPassword = "correct password";
        viewModel.ToggleMasterPasswordVisibilityCommand.Execute(null);

        await viewModel.UnlockCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsUnlocked);
        Assert.True(viewModel.HasUnlockError);
        Assert.Equal(viewModel.L.Get("VaultAccessUnlockFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(sensitiveDetail, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Empty(viewModel.MasterPassword);
        Assert.False(viewModel.IsMasterPasswordVisible);
    }

    [Fact]
    public async Task Legacy_business_data_notice_respects_acknowledged_state_signature()
    {
        const string acknowledgedSignature = "legacy-sqlite-v1:1:0:0:0:0:0";
        const string changedSignature = "legacy-sqlite-v1:2:0:0:0:0:0";
        var settingsPath = GetTempSettingsPath();
        var settings = new AppSettingsService(settingsPath);
        await settings.LoadAsync();
        settings.Current.LegacyBusinessDataNoticeAcknowledgedSignature = acknowledgedSignature;
        await settings.SaveAsync();

        var acknowledged = CreateViewModel(
            GetTempDatabasePath(),
            settingsService: settings,
            vaultUnlockCoordinator: new FixedVaultUnlockCoordinator(acknowledgedSignature));
        await acknowledged.InitializeAsync();
        acknowledged.MasterPassword = "correct password";

        await acknowledged.UnlockCommand.ExecuteAsync(null);

        Assert.False(acknowledged.HasPendingLegacyBusinessData);

        var changedSettings = new AppSettingsService(settingsPath);
        var changed = CreateViewModel(
            GetTempDatabasePath(),
            settingsService: changedSettings,
            vaultUnlockCoordinator: new FixedVaultUnlockCoordinator(changedSignature));
        await changed.InitializeAsync();
        changed.MasterPassword = "correct password";

        await changed.UnlockCommand.ExecuteAsync(null);

        Assert.True(changed.HasPendingLegacyBusinessData);
        changed.DismissLegacyBusinessDataNoticeCommand.Execute(null);
        Assert.False(changed.HasPendingLegacyBusinessData);
        Assert.Equal(
            changedSignature,
            changedSettings.Current.LegacyBusinessDataNoticeAcknowledgedSignature);
    }

    [Fact]
    public async Task ViewModel_creates_vault_then_rejects_wrong_password()
    {
        var path = GetTempDatabasePath();
        var first = CreateViewModel(path);
        await first.InitializeAsync();
        first.MasterPassword = "correct password";
        first.ConfirmMasterPassword = "correct password";
        await first.UnlockCommand.ExecuteAsync(null);

        Assert.True(first.IsUnlocked);
        Assert.True(first.IsVaultInitialized);

        var second = CreateViewModel(path);
        await second.InitializeAsync();
        second.MasterPassword = "wrong password";
        await second.UnlockCommand.ExecuteAsync(null);

        Assert.False(second.IsUnlocked);
        Assert.Equal(second.L.Get("WrongMasterPassword"), second.StatusMessage);

        second.MasterPassword = "correct password";
        await second.UnlockCommand.ExecuteAsync(null);

        Assert.True(second.IsUnlocked);
    }

    [Fact]
    public async Task Repository_encrypts_sensitive_vault_fields_at_rest()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var migrator = new DatabaseMigrator(factory);
        var crypto = new CryptoService();
        var hash = crypto.HashMasterPassword("vault password");
        crypto.InitializeSession("vault password", hash.Salt);
        var repository = new MonicaRepository(factory, migrator, new VaultDataProtector(crypto));

        var passwordId = await repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "secret-login-title",
            Website = "https://secret.example",
            Username = "secret-user",
            Password = "secret-password",
            Notes = "secret-password-notes",
            CreditCardNumber = "4111111111111111",
            CreditCardCvv = "123",
            AuthenticatorKey = "secret-totp-seed",
            WifiMetadata = "secret-wifi-json"
        });
        await repository.ReplaceCustomFieldsAsync(passwordId,
        [
            new CustomField { Title = "secret-field-title", Value = "secret-field-value", IsProtected = true }
        ]);
        await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = passwordId,
            Password = "secret-old-password"
        });
        await repository.SaveSecureItemAsync(new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "secret-totp-title",
            Notes = "secret-totp-notes",
            ItemData = """{"secret":"secret-secure-item-payload"}"""
        });
        await repository.SaveMdbxDatabaseAsync(new LocalMdbxDatabase
        {
            Name = "Local",
            FilePath = "local.mdbx",
            StorageLocation = MdbxStorageLocation.Internal,
            EncryptedPassword = "secret-mdbx-password",
            KeyFileUri = "secret-key-file-uri"
        });
        await repository.LogAsync(new OperationLog
        {
            ItemType = "PASSWORD",
            ItemId = passwordId,
            ItemTitle = "secret-log-title",
            OperationType = "CREATE",
            ChangesJson = """{"secret":"secret-log-change"}"""
        });

        var rawVaultText = await ReadRawVaultTextAsync(path);

        foreach (var secret in new[]
        {
            "secret-login-title",
            "secret-user",
            "secret-password",
            "secret-password-notes",
            "4111111111111111",
            "secret-totp-seed",
            "secret-wifi-json",
            "secret-field-title",
            "secret-field-value",
            "secret-old-password",
            "secret-totp-title",
            "secret-secure-item-payload",
            "secret-mdbx-password",
            "secret-key-file-uri",
            "secret-log-title",
            "secret-log-change"
        })
        {
            Assert.DoesNotContain(secret, rawVaultText);
        }

        Assert.Contains("vault:v1:", rawVaultText);

        var reloadedPassword = Assert.Single(await repository.GetPasswordsAsync());
        Assert.Equal("secret-login-title", reloadedPassword.Title);
        Assert.Equal("secret-password", reloadedPassword.Password);
        Assert.Equal("secret-totp-seed", reloadedPassword.AuthenticatorKey);
        Assert.Equal("4111111111111111", reloadedPassword.CreditCardNumber);
        Assert.Equal("secret-field-value", Assert.Single(await repository.GetCustomFieldsAsync(passwordId)).Value);
        Assert.Equal("secret-old-password", Assert.Single(await repository.GetPasswordHistoryAsync(passwordId)).Password);
        Assert.Contains("secret-secure-item-payload", Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Totp)).ItemData);
        Assert.Equal("secret-mdbx-password", Assert.Single(await repository.GetMdbxDatabasesAsync()).EncryptedPassword);
        Assert.Contains("secret-log-change", Assert.Single(await repository.GetOperationLogsAsync()).ChangesJson);
    }

    private static MainWindowViewModel CreateViewModel(
        string databasePath,
        ICryptoService? cryptoService = null,
        IClipboardService? clipboardService = null,
        IAppSettingsService? settingsService = null,
        IExportAuthorizationService? exportAuthorizationService = null,
        IVaultUnlockCoordinator? vaultUnlockCoordinator = null)
    {
        var factory = new SqliteConnectionFactory(databasePath);
        var migrator = new DatabaseMigrator(factory);
        var repository = new MonicaRepository(factory, migrator);
        return new MainWindowViewModel(
            repository,
            new VaultCredentialStore(factory, migrator),
            cryptoService ?? new CryptoService(),
            new TotpService(),
            new PasswordGeneratorService(),
            new ImportExportService(),
            new PlatformCapabilityService(),
            new PlatformIntegrationService(),
            clipboardService ?? new NoopClipboardService(),
            new NoopWebDavBackupService(),
            new MdbxVaultService(),
            new NoopPasswordAttachmentFileService(),
            new NoopPasswordEditorDialogService(),
            new NoopPasswordDetailDialogService(),
            new NoopCategoryPickerDialogService(),
            new LegacyVaultDetector(factory),
            settingsService ?? new AppSettingsService(GetTempSettingsPath()),
            new LocalizationService(),
            vaultUnlockCoordinator: vaultUnlockCoordinator,
            exportAuthorizationService: exportAuthorizationService);
    }

    private sealed class BlockingVaultUnlockCoordinator : IVaultUnlockCoordinator
    {
        private readonly TaskCompletionSource<VaultUnlockResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int UnlockCallCount { get; private set; }

        public Task<VaultInitializationState> InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new VaultInitializationState(LegacyVaultDetection.Empty, true));

        public Task<VaultUnlockResult> UnlockOrCreateAsync(
            string masterPassword,
            string confirmMasterPassword,
            LegacyVaultDetection legacyVaultDetection,
            CancellationToken cancellationToken = default)
        {
            UnlockCallCount++;
            Entered.TrySetResult(null);
            return _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete(VaultUnlockResult result) => _completion.TrySetResult(result);
    }

    private sealed class FixedVaultUnlockCoordinator(string legacyBusinessDataSignature) : IVaultUnlockCoordinator
    {
        public Task<VaultInitializationState> InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new VaultInitializationState(LegacyVaultDetection.Empty, true));

        public Task<VaultUnlockResult> UnlockOrCreateAsync(
            string masterPassword,
            string confirmMasterPassword,
            LegacyVaultDetection legacyVaultDetection,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new VaultUnlockResult(
                VaultUnlockStatus.Unlocked,
                true,
                "VaultUnlockedLegacyBusinessDataPending",
                LegacyBusinessDataPending: true,
                LegacyBusinessDataSignature: legacyBusinessDataSignature));
    }

    private sealed class BlockingStartupSettingsService(Task release) : IAppSettingsService
    {
        public DesktopAppSettings Current { get; } = new();
        public TaskCompletionSource<object?> LoadEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadEntered.TrySetResult(null);
            await release.WaitAsync(cancellationToken);
        }

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearSensitiveCacheAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IReadOnlyDictionary<string, bool> GetFeatureToggles() => Current.FeatureToggles;
        public bool IsFeatureEnabled(string featureKey) => Current.FeatureToggles.GetValueOrDefault(featureKey);
        public void SetFeatureEnabled(string featureKey, bool isEnabled) => Current.FeatureToggles[featureKey] = isEnabled;
    }

    private sealed class BlockingStartupVaultCoordinator(Task release) : IVaultUnlockCoordinator
    {
        public TaskCompletionSource<object?> InitializeEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<VaultInitializationState> InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeEntered.TrySetResult(null);
            await release.WaitAsync(cancellationToken);
            return new VaultInitializationState(LegacyVaultDetection.Empty, true);
        }

        public Task<VaultUnlockResult> UnlockOrCreateAsync(
            string masterPassword,
            string confirmMasterPassword,
            LegacyVaultDetection legacyVaultDetection,
            CancellationToken cancellationToken = default) =>
            Task.FromException<VaultUnlockResult>(new NotSupportedException());
    }

    private sealed class FailingStartupVaultCoordinator : IVaultUnlockCoordinator
    {
        public Task<VaultInitializationState> InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<VaultInitializationState>(new InvalidOperationException("Metadata unavailable"));

        public Task<VaultUnlockResult> UnlockOrCreateAsync(
            string masterPassword,
            string confirmMasterPassword,
            LegacyVaultDetection legacyVaultDetection,
            CancellationToken cancellationToken = default) =>
            Task.FromException<VaultUnlockResult>(new NotSupportedException());
    }

    private sealed class FailedVaultUnlockCoordinator(string detail) : IVaultUnlockCoordinator
    {
        public Task<VaultInitializationState> InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new VaultInitializationState(LegacyVaultDetection.Empty, true));

        public Task<VaultUnlockResult> UnlockOrCreateAsync(
            string masterPassword,
            string confirmMasterPassword,
            LegacyVaultDetection legacyVaultDetection,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new VaultUnlockResult(
                VaultUnlockStatus.Failed,
                true,
                "UnlockFailedFormat",
                new InvalidOperationException(detail)));
    }

    private sealed class ThrowingVaultUnlockCoordinator(string detail) : IVaultUnlockCoordinator
    {
        public Task<VaultInitializationState> InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new VaultInitializationState(LegacyVaultDetection.Empty, true));

        public Task<VaultUnlockResult> UnlockOrCreateAsync(
            string masterPassword,
            string confirmMasterPassword,
            LegacyVaultDetection legacyVaultDetection,
            CancellationToken cancellationToken = default) =>
            Task.FromException<VaultUnlockResult>(new InvalidOperationException(detail));
    }

    private static string GetTempDatabasePath()
    {
        return TestTempPaths.CreateFilePath(".db");
    }

    private static string GetTempSettingsPath()
    {
        return TestTempPaths.CreateFilePath(".settings.json");
    }

    private static async Task<string> ReadRawVaultTextAsync(string databasePath)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        var fragments = new List<string>();
        foreach (var sql in new[]
        {
            "SELECT title, website, username, password, notes, credit_card_number, credit_card_cvv, authenticator_key, wifi_metadata FROM password_entries",
            "SELECT title, value FROM custom_fields",
            "SELECT password FROM password_history_entries",
            "SELECT title, notes, item_data, image_paths FROM secure_items",
            "SELECT encrypted_password, key_file_uri FROM local_mdbx_databases",
            "SELECT item_title, changes_json FROM operation_logs"
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    if (!reader.IsDBNull(index))
                    {
                        fragments.Add(reader.GetString(index));
                    }
                }
            }
        }

        return string.Join("\n", fragments);
    }

    private sealed class NoopClipboardService : IClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingClipboardService : IClipboardService
    {
        public bool ClearOwnedContentCalled { get; private set; }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ClearOwnedContentAsync(CancellationToken cancellationToken = default)
        {
            ClearOwnedContentCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RejectingExportAuthorizationService : IExportAuthorizationService
    {
        public Task<bool> AuthorizeAsync(bool requireMasterPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
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
