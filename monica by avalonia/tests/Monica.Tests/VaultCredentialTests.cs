using Monica.Core.ImportExport;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.App.ViewModels;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class VaultCredentialTests
{
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
    public async Task ViewModel_requires_first_run_password_confirmation()
    {
        var viewModel = CreateViewModel(GetTempDatabasePath());
        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsVaultInitialized);

        viewModel.MasterPassword = "password-one";
        viewModel.ConfirmMasterPassword = "password-two";
        await viewModel.UnlockCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsUnlocked);
        Assert.Equal(viewModel.L.Get("ConfirmationMismatch"), viewModel.StatusMessage);
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

    private static MainWindowViewModel CreateViewModel(string databasePath)
    {
        var factory = new SqliteConnectionFactory(databasePath);
        var migrator = new DatabaseMigrator(factory);
        var repository = new MonicaRepository(factory, migrator);
        return new MainWindowViewModel(
            repository,
            new VaultCredentialStore(factory, migrator),
            new CryptoService(),
            new TotpService(),
            new PasswordGeneratorService(),
            new ImportExportService(),
            new PlatformCapabilityService(),
            new PlatformIntegrationService(),
            new NoopClipboardService(),
            new NoopWebDavBackupService(),
            new MdbxVaultService(),
            new NoopPasswordAttachmentFileService(),
            new NoopPasswordEditorDialogService(),
            new NoopPasswordDetailDialogService(),
            new NoopCategoryPickerDialogService(),
            new LegacyVaultDetector(factory),
            new AppSettingsService(GetTempSettingsPath()),
            new LocalizationService());
    }

    private static string GetTempDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static string GetTempSettingsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
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
            Func<PasswordEntry, Task>? addAttachment,
            Func<Attachment, Task<bool>>? deleteAttachment,
            Func<PasswordHistoryEntry, Task<bool>>? deletePasswordHistory,
            Func<long, Task<bool>>? clearPasswordHistory,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopPasswordAttachmentFileService : IPasswordAttachmentFileService
    {
        public Task<PasswordAttachmentFileDraft?> PickAndStoreAttachmentAsync(PasswordEntry entry, CancellationToken cancellationToken = default) =>
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
