using System.Text.Json;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Mdbx;
using Monica.Data.Repositories;
using Monica.Data.Services;

namespace Monica.Tests;

public sealed class MdbxRepositoryTests
{
    private static readonly JsonSerializerOptions MdbxPayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task Canonical_repository_keeps_password_and_secure_item_rows_out_of_sqlite()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Canonical login",
            Username = "canonical-user",
            Password = "canonical-secret"
        };
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Canonical note",
            ItemData = "{\"content\":\"canonical\"}"
        };

        await repository.SavePasswordAsync(password);
        await repository.SaveSecureItemAsync(note);

        Assert.True(password.Id > 0);
        Assert.True(note.Id > 0);
        Assert.Empty(await sqliteRepository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.Empty(await sqliteRepository.GetSecureItemsAsync(includeDeleted: true));
        Assert.Equal("Canonical login", Assert.Single(await repository.GetPasswordsAsync()).Title);
        Assert.Equal("Canonical note", Assert.Single(await repository.GetSecureItemsAsync()).Title);
    }

    [Fact]
    public async Task Canonical_repository_keeps_categories_fields_history_and_attachments_out_of_sqlite()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var category = new Category { Name = "Canonical/Work", SortOrder = 3 };
        await repository.SaveCategoryAsync(category);
        var password = new PasswordEntry
        {
            Title = "Canonical extended login",
            Password = "current-secret",
            CategoryId = category.Id
        };
        await repository.SavePasswordAsync(password);
        await repository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { EntryId = password.Id, Title = "Recovery", Value = "fixture", IsProtected = true }
        ]);
        var history = new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "previous-secret",
            LastUsedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        await repository.SavePasswordHistoryAsync(history);
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "canonical.txt",
            ContentType = "text/plain",
            StoragePath = "pending:canonical.txt",
            SizeBytes = 9
        };
        await repository.SaveAttachmentAsync(attachment);

        Assert.True(category.Id > 0);
        Assert.True(history.Id > 0);
        Assert.True(attachment.Id > 0);
        Assert.Empty(await sqliteRepository.GetCategoriesAsync());
        Assert.Empty(await sqliteRepository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.Empty(await sqliteRepository.GetCustomFieldsAsync(password.Id));
        Assert.Empty(await sqliteRepository.GetPasswordHistoryAsync(password.Id));
        Assert.Empty(await sqliteRepository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Equal("Recovery", Assert.Single(await repository.GetCustomFieldsAsync(password.Id)).Title);
        Assert.Equal("previous-secret", Assert.Single(await repository.GetPasswordHistoryAsync(password.Id)).Password);
        Assert.Equal("canonical.txt", Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id)).FileName);
    }

    [Fact]
    public async Task Canonical_repository_does_not_auto_import_sqlite_business_data_into_new_default_mdbx()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var legacy = new PasswordEntry
        {
            Title = "SQLite legacy only",
            Password = "legacy-secret"
        };
        await sqliteRepository.SavePasswordAsync(legacy);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);

        var canonicalPasswords = await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);

        Assert.Empty(canonicalPasswords);
        Assert.Equal("SQLite legacy only", Assert.Single(await sqliteRepository.GetPasswordsAsync()).Title);
        Assert.Equal(0, bridge.CountEntries(database.WorkingCopyPath!));
    }

    [Fact]
    public async Task Canonical_repository_fails_closed_without_default_mdbx_instead_of_using_sqlite()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await sqliteRepository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Hidden legacy row",
            Password = "legacy-secret"
        });

        Assert.Empty(await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Must not fall back",
            Password = "secret"
        }));
        Assert.Single(await sqliteRepository.GetPasswordsAsync());
    }

    [Fact]
    public async Task Canonical_repository_fails_closed_for_all_business_mutations_without_default_mdbx()
    {
        var repository = CreateRepository(out _);
        var password = new PasswordEntry { Id = 11, Title = "Unavailable", Password = "secret" };
        var secureItem = new SecureItem { Id = 12, ItemType = VaultItemType.Note, Title = "Unavailable note" };
        var attachment = new Attachment { Id = 13, OwnerType = "PASSWORD", OwnerId = password.Id, FileName = "unavailable.txt" };
        var history = new PasswordHistoryEntry { Id = 14, EntryId = password.Id, Password = "old" };
        var category = new Category { Id = 15, Name = "Unavailable category" };

        var mutations = new Func<Task>[]
        {
            () => repository.SavePasswordAsync(password),
            () => repository.SoftDeletePasswordAsync(password.Id),
            () => repository.RestorePasswordAsync(password.Id),
            () => repository.DeletePasswordPermanentlyAsync(password.Id),
            () => repository.ReplaceCustomFieldsAsync(password.Id, []),
            () => repository.SaveAttachmentAsync(attachment),
            () => repository.SaveAttachmentAsync(attachment, "content"u8.ToArray()),
            () => repository.DeleteAttachmentAsync(attachment.Id),
            () => repository.DeleteAttachmentAsync(attachment.Id, attachment),
            () => repository.SavePasswordHistoryAsync(history),
            () => repository.TrimPasswordHistoryAsync(password.Id, 1),
            () => repository.DeletePasswordHistoryAsync(history.Id),
            () => repository.ClearPasswordHistoryAsync(password.Id),
            () => repository.SaveSecureItemAsync(secureItem),
            () => repository.SoftDeleteSecureItemAsync(secureItem.Id),
            () => repository.SaveCategoryAsync(category),
            () => repository.DeleteCategoryAsync(category.Id),
            () => repository.ClearVaultDataAsync(VaultClearScope.All)
        };

        foreach (var mutation in mutations)
        {
            await Assert.ThrowsAnyAsync<InvalidOperationException>(mutation);
        }
    }

    [Fact]
    public async Task Canonical_repository_records_quick_access_without_creating_sqlite_password_cache()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Canonical quick access",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);

        var updated = await repository.RecordPasswordQuickAccessAsync(password.Id);

        Assert.Empty(await sqliteRepository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.NotNull(updated);
        Assert.Equal(password.Id, updated.PasswordId);
        Assert.Equal(1, updated.OpenCount);
        var record = Assert.Single(await repository.GetPasswordQuickAccessRecordsAsync());
        Assert.Equal(password.Id, record.PasswordId);
        Assert.Equal(1, record.OpenCount);
    }

    [Fact]
    public async Task Repository_reads_android_flat_password_payload_from_mdbx_record()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        bridge.SeedEntry(
            database.WorkingCopyPath!,
            "Monica",
            "login",
            "Android 门户",
            ReadMdbxFixture("android-login-v1.json"));

        var entry = Assert.Single(await repository.GetPasswordsAsync());
        var fields = await repository.GetCustomFieldsAsync(entry.Id);

        Assert.Equal(42, entry.Id);
        Assert.Equal("Android 门户", entry.Title);
        Assert.Equal("S3cret-密码-🔐", entry.Password);
        Assert.Equal("恢复代码", fields[1].Title);
        Assert.True(fields[1].IsProtected);
    }

    [Fact]
    public async Task Repository_keeps_read_compatibility_with_legacy_avalonia_wrapper_payload()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        bridge.SeedEntry(
            database.WorkingCopyPath!,
            "Monica",
            "login",
            "Legacy wrapper",
            """
            {
              "kind": "password",
              "schemaVersion": 1,
              "data": {
                "entry": {
                  "id": 77,
                  "title": "Legacy wrapper",
                  "username": "legacy-user",
                  "password": "legacy-secret"
                },
                "customFields": [
                  { "entryId": 77, "title": "Legacy field", "value": "legacy-value", "sortOrder": 0 }
                ]
              }
            }
            """);

        var entry = Assert.Single(await repository.GetPasswordsAsync());

        Assert.Equal(77, entry.Id);
        Assert.Equal("legacy-user", entry.Username);
        Assert.Equal("legacy-secret", entry.Password);
        Assert.Equal("Legacy field", Assert.Single(await repository.GetCustomFieldsAsync(entry.Id)).Title);
    }

    [Fact]
    public async Task Repository_roundtrips_passwords_through_mdbx_store_when_default_vault_exists()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "GitHub",
            Website = "https://github.com",
            Username = "dev",
            Password = "secret",
            Notes = "recovery codes",
            IsFavorite = true
        };

        await repository.SavePasswordAsync(password);

        Assert.NotNull(password.MdbxDatabaseId);
        Assert.False(string.IsNullOrWhiteSpace(password.MdbxFolderId));
        Assert.NotEmpty(bridge.OpenedPaths);
        Assert.All(bridge.OpenedPaths, path => Assert.Equal(database.WorkingCopyPath, path));

        password.Password = "rotated";
        await repository.SavePasswordAsync(password);

        var reloaded = Assert.Single(await repository.GetPasswordsAsync());
        Assert.Equal(password.Id, reloaded.Id);
        Assert.Equal("rotated", reloaded.Password);
        Assert.Equal(password.MdbxFolderId, reloaded.MdbxFolderId);

        await repository.SoftDeletePasswordAsync(password.Id);

        Assert.Empty(await repository.GetPasswordsAsync());
        var deleted = Assert.Single(await repository.GetPasswordsAsync(includeDeleted: true));
        Assert.True(deleted.IsDeleted);

        await repository.RestorePasswordAsync(password.Id);

        var restored = Assert.Single(await repository.GetPasswordsAsync());
        Assert.False(restored.IsDeleted);
        Assert.Equal("rotated", restored.Password);
    }

    [Fact]
    public async Task Repository_does_not_persist_local_mdbx_bindings_inside_payloads()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Payload binding check",
            Password = "secret"
        };
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Payload note",
            ItemData = "{}"
        };
        await repository.SavePasswordAsync(password);
        await repository.SaveSecureItemAsync(note);

        password.Title = "Payload binding check updated";
        note.Title = "Payload note updated";
        await repository.SavePasswordAsync(password);
        await repository.SaveSecureItemAsync(note);

        using var passwordPayload = JsonDocument.Parse(bridge.GetEntryPayloadJson(database.WorkingCopyPath!, password.MdbxFolderId!)!);
        var entry = passwordPayload.RootElement;
        AssertMissingOrNull(entry, "mdbxDatabaseId");
        AssertMissingOrNull(entry, "mdbxFolderId");
        Assert.Equal(password.MdbxFolderId, entry.GetProperty("mdbx_folder_id").GetString());
        Assert.False(entry.TryGetProperty("data", out _));

        using var secureItemPayload = JsonDocument.Parse(bridge.GetEntryPayloadJson(database.WorkingCopyPath!, note.MdbxFolderId!)!);
        var item = secureItemPayload.RootElement;
        AssertMissingOrNull(item, "mdbxDatabaseId");
        AssertMissingOrNull(item, "mdbxFolderId");
        Assert.Equal(note.MdbxFolderId, item.GetProperty("mdbx_folder_id").GetString());
        Assert.False(item.TryGetProperty("data", out _));

        Assert.NotNull(Assert.Single(await repository.GetPasswordsAsync()).MdbxDatabaseId);
        Assert.NotNull(Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Note)).MdbxDatabaseId);

        static void AssertMissingOrNull(JsonElement element, string propertyName)
        {
            Assert.True(
                !element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null,
                $"Expected '{propertyName}' to be absent or null.");
        }
    }

    [Fact]
    public async Task Repository_records_quick_access_when_sqlite_password_cache_is_missing()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Quick access from MDBX",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);

        await repository.RecordPasswordQuickAccessAsync(password.Id);
        await repository.RecordPasswordQuickAccessAsync(password.Id);

        var record = Assert.Single(await repository.GetPasswordQuickAccessRecordsAsync());
        Assert.Equal(password.Id, record.PasswordId);
        Assert.Equal(2, record.OpenCount);
    }

    [Fact]
    public async Task Repository_reuses_mdbx_read_caches_across_vault_load_queries()
    {
        var repository = CreateRepository(out var bridge);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Cached load",
            Username = "cache-user",
            Password = "secret"
        };
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Cached note",
            Notes = "note"
        };
        await repository.SavePasswordAsync(password);
        await repository.SaveSecureItemAsync(note);

        bridge.OpenedPaths.Clear();
        var passwords = await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
        var passwordIds = passwords.Select(item => item.Id).ToArray();
        var fields = await repository.GetCustomFieldsByEntryIdsAsync(passwordIds);
        var attachments = await repository.GetAttachmentsByOwnerIdsAsync("PASSWORD", passwordIds);
        var secureItems = await repository.GetSecureItemsAsync();
        var categories = await repository.GetCategoriesAsync();

        Assert.Single(passwords);
        Assert.Empty(fields.Values.SelectMany(item => item));
        Assert.Empty(attachments.Values.SelectMany(item => item));
        Assert.Single(secureItems);
        Assert.Empty(categories);
        Assert.True(
            bridge.OpenedPaths.Count <= 3,
            $"Expected the vault-load read path to reuse MDBX read caches, but MDBX was opened {bridge.OpenedPaths.Count} times.");
    }

    [Fact]
    public async Task Vault_snapshot_loader_fanout_preserves_canonical_mdbx_results()
    {
        var repository = CreateRepository(out _);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var category = new Category { Name = "Work" };
        await repository.SaveCategoryAsync(category);
        var password = new PasswordEntry
        {
            Title = "Canonical login",
            Username = "canonical-user",
            Password = "secret",
            CategoryId = category.Id
        };
        await repository.SavePasswordAsync(password);
        await repository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { EntryId = password.Id, Title = "Environment", Value = "Production" }
        ]);
        await repository.SaveAttachmentAsync(new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "recovery.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/recovery.enc",
            SizeBytes = 8
        });
        await repository.SaveSecureItemAsync(new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Canonical note"
        });
        await repository.RecordPasswordQuickAccessAsync(password.Id);

        var snapshot = await VaultSnapshotLoader.LoadAsync(repository);

        Assert.Equal(password.Id, Assert.Single(snapshot.ActivePasswords).Id);
        Assert.Equal("Environment", Assert.Single(await repository.GetCustomFieldsAsync(password.Id)).Title);
        Assert.Contains(password.Id, snapshot.PasswordAttachmentOwnerIds);
        Assert.Equal("recovery.txt", Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id)).FileName);
        Assert.Equal("Canonical note", Assert.Single(snapshot.NoteItems).Title);
        Assert.Equal(category.Id, Assert.Single(snapshot.Categories).Id);
        Assert.Equal(1, snapshot.PasswordQuickAccessRecords[password.Id].OpenCount);
        Assert.Single(snapshot.MdbxDatabases);
    }

    [Fact]
    public async Task Vault_snapshot_loader_releases_mdbx_item_snapshots_after_detaching_results()
    {
        var repository = CreateRepository(out var bridge);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Detached login",
            Password = "secret"
        };
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Detached note",
            Notes = "note"
        };
        await repository.SavePasswordAsync(password);
        await repository.SaveSecureItemAsync(note);

        bridge.OpenedPaths.Clear();
        var snapshot = await VaultSnapshotLoader.LoadAsync(repository);
        var vaultLoadOpenCount = bridge.OpenedPaths.Count;

        bridge.OpenedPaths.Clear();
        var reloadedPasswords = await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
        var reloadedSecureItems = await repository.GetSecureItemsAsync(includeDeleted: true);

        Assert.Equal(password.Id, Assert.Single(snapshot.ActivePasswords).Id);
        Assert.Equal(note.Id, Assert.Single(snapshot.NoteItems).Id);
        Assert.Equal(password.Id, Assert.Single(reloadedPasswords).Id);
        Assert.Equal(note.Id, Assert.Single(reloadedSecureItems).Id);
        Assert.True(
            vaultLoadOpenCount <= 2,
            $"Expected Vault Access to reuse one password and one secure-item snapshot, but MDBX was opened {vaultLoadOpenCount} times.");
        Assert.Equal(2, bridge.OpenedPaths.Count);
    }

    [Fact]
    public async Task Repository_releases_transient_vault_item_snapshots_on_demand()
    {
        var repository = CreateRepository(out var bridge);
        await SaveDefaultMdbxDatabaseAsync(repository);
        await repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Transient password snapshot",
            Password = "secret"
        });
        await repository.SaveSecureItemAsync(new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Transient secure-item snapshot",
            Notes = "secret note"
        });

        await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
        await repository.GetSecureItemsAsync(includeDeleted: true);
        bridge.OpenedPaths.Clear();

        await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
        await repository.GetSecureItemsAsync(includeDeleted: true);
        Assert.Empty(bridge.OpenedPaths);

        Assert.IsAssignableFrom<ITransientVaultReadCache>(repository)
            .ReleaseVaultItemSnapshots();

        await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
        await repository.GetSecureItemsAsync(includeDeleted: true);
        Assert.Equal(2, bridge.OpenedPaths.Count);
    }

    [Fact]
    public async Task Repository_cascades_password_delete_restore_to_mdbx_bound_totps_when_sqlite_cache_is_missing()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Portal",
            Username = "dev",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var totp = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Portal OTP",
            ItemData = """{"secret":"JBSWY3DPEHPK3PXP"}""",
            BoundPasswordId = password.Id
        };
        await repository.SaveSecureItemAsync(totp);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.SecureItems);

        Assert.Equal("Portal OTP", Assert.Single(await repository.GetSecureItemsByBoundPasswordIdAsync(password.Id)).Title);

        await repository.SoftDeletePasswordAsync(password.Id);

        Assert.Empty(await repository.GetPasswordsAsync());
        Assert.Empty(await repository.GetSecureItemsByBoundPasswordIdAsync(password.Id));
        Assert.Equal(0, bridge.CountActiveEntries(database.WorkingCopyPath!));
        Assert.Equal(2, bridge.CountDeletedEntries(database.WorkingCopyPath!));

        await repository.RestorePasswordAsync(password.Id);

        Assert.Equal("Portal", Assert.Single(await repository.GetPasswordsAsync()).Title);
        var restoredTotp = Assert.Single(await repository.GetSecureItemsByBoundPasswordIdAsync(password.Id));
        Assert.Equal(totp.Id, restoredTotp.Id);
        Assert.Equal("Portal OTP", restoredTotp.Title);
        Assert.Equal(2, bridge.CountActiveEntries(database.WorkingCopyPath!));
        Assert.Equal(0, bridge.CountDeletedEntries(database.WorkingCopyPath!));
    }

    [Fact]
    public async Task Repository_uses_remote_mdbx_working_copy_as_default_store()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(
            repository,
            MdbxStorageLocation.RemoteWebDav,
            "REMOTE_WEBDAV");
        database.FilePath = "/Monica/local.mdbx";
        await repository.SaveMdbxDatabaseAsync(database);
        var password = new PasswordEntry
        {
            Title = "Remote-backed",
            Password = "secret"
        };

        await repository.SavePasswordAsync(password);

        var reloaded = Assert.Single(await repository.GetPasswordsAsync());
        Assert.Equal(database.Id, reloaded.MdbxDatabaseId);
        Assert.False(string.IsNullOrWhiteSpace(reloaded.MdbxFolderId));
        Assert.Equal("Remote-backed", reloaded.Title);
        Assert.Contains(database.WorkingCopyPath!, bridge.OpenedPaths);
    }

    [Fact]
    public async Task Repository_marks_synced_webdav_working_copy_pending_before_business_write()
    {
        var repository = CreateRepository(out _);
        var database = await SaveDefaultMdbxDatabaseAsync(
            repository,
            MdbxStorageLocation.RemoteWebDav,
            "REMOTE_WEBDAV");
        database.FilePath = "/Monica/team-vault.mdbx";
        database.LastSyncStatus = SyncStatus.Synced;
        database.LastSyncError = "old diagnostic";
        database.LastSyncedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await repository.SaveMdbxDatabaseAsync(database);
        var previousSyncedAt = Assert.Single(await repository.GetMdbxDatabasesAsync()).LastSyncedAt;

        await repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Local change",
            Password = "secret"
        });

        var persisted = Assert.Single(await repository.GetMdbxDatabasesAsync());
        Assert.Equal(SyncStatus.PendingUpload, persisted.LastSyncStatus);
        Assert.Null(persisted.LastSyncError);
        Assert.Equal(previousSyncedAt, persisted.LastSyncedAt);
    }

    [Fact]
    public async Task Repository_keeps_synced_webdav_status_for_quick_access_metadata()
    {
        var repository = CreateRepository(out _);
        var database = await SaveDefaultMdbxDatabaseAsync(
            repository,
            MdbxStorageLocation.RemoteWebDav,
            "REMOTE_WEBDAV");
        var password = new PasswordEntry
        {
            Title = "Quick access",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        database.LastSyncStatus = SyncStatus.Synced;
        database.LastSyncError = null;
        await repository.SaveMdbxDatabaseAsync(database);

        await repository.RecordPasswordQuickAccessAsync(password.Id);

        var persisted = Assert.Single(await repository.GetMdbxDatabasesAsync());
        Assert.Equal(SyncStatus.Synced, persisted.LastSyncStatus);
    }

    [Fact]
    public async Task Repository_fails_closed_for_remote_mdbx_metadata_without_working_copy()
    {
        var repository = CreateRepository(out var bridge);
        await repository.SaveMdbxDatabaseAsync(new LocalMdbxDatabase
        {
            Name = "Remote metadata only",
            FilePath = "/Monica/local.mdbx",
            StorageLocation = MdbxStorageLocation.RemoteWebDav,
            SourceType = "REMOTE_WEBDAV",
            EncryptedPassword = "test-mdbx-password",
            IsDefault = true
        });
        var password = new PasswordEntry
        {
            Title = "Unavailable canonical vault",
            Password = "secret"
        };

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => repository.SavePasswordAsync(password));

        Assert.Empty(await repository.GetPasswordsAsync());
        Assert.Empty(bridge.OpenedPaths);
    }

    [Fact]
    public async Task Repository_roundtrips_password_custom_fields_through_mdbx_payload()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "With fields",
            Username = "dev",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        await repository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { Title = "Security question", Value = "First school" },
            new CustomField { Title = "Backup code", Value = "123456", IsProtected = true }
        ]);

        await sqliteRepository.SavePasswordAsync(CreateSqlitePasswordStub(password));
        await sqliteRepository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { Title = "SQLite stale", Value = "stale-only" }
        ]);

        var fields = await repository.GetCustomFieldsAsync(password.Id);
        var fieldsByEntryId = await repository.GetCustomFieldsByEntryIdsAsync([password.Id]);

        Assert.Equal(["Security question", "Backup code"], fields.Select(field => field.Title).ToArray());
        Assert.All(fields, field => Assert.Equal(password.Id, field.EntryId));
        Assert.True(fields[1].IsProtected);
        Assert.Equal(fields.Select(field => field.Title), fieldsByEntryId[password.Id].Select(field => field.Title));
        Assert.Equal([password.Id], await repository.SearchEntryIdsByCustomFieldContentAsync("school"));
        Assert.Empty(await repository.SearchEntryIdsByCustomFieldContentAsync("stale-only"));
    }

    [Fact]
    public async Task Repository_updates_and_clears_password_custom_fields_in_mdbx_payload()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Mutable fields",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        await repository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { Title = "Old", Value = "remove-me" }
        ]);

        await repository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { Title = "New", Value = "keep-me" }
        ]);

        Assert.Equal("New", Assert.Single(await repository.GetCustomFieldsAsync(password.Id)).Title);
        Assert.Empty(await repository.SearchEntryIdsByCustomFieldContentAsync("remove-me"));
        Assert.Equal([password.Id], await repository.SearchEntryIdsByCustomFieldContentAsync("keep-me"));

        await repository.ReplaceCustomFieldsAsync(password.Id, []);
        await sqliteRepository.SavePasswordAsync(CreateSqlitePasswordStub(password));
        await sqliteRepository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { Title = "Old", Value = "stale-after-clear" }
        ]);

        Assert.Empty(await repository.GetCustomFieldsAsync(password.Id));
        Assert.Empty(await repository.SearchEntryIdsByCustomFieldContentAsync("stale-after-clear"));
    }

    [Fact]
    public async Task Repository_updates_password_custom_fields_when_sqlite_password_cache_is_missing()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "MDBX-only fields",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);

        await repository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { EntryId = password.Id, Title = "owner", Value = "mdbx-only" }
        ]);

        var field = Assert.Single(await repository.GetCustomFieldsAsync(password.Id));
        Assert.Equal("owner", field.Title);
        Assert.Equal("mdbx-only", field.Value);
        Assert.Equal([password.Id], await repository.SearchEntryIdsByCustomFieldContentAsync("mdbx-only"));
    }

    [Fact]
    public async Task Repository_does_not_return_mdbx_custom_fields_after_password_is_permanently_deleted()
    {
        var repository = CreateRepository(out _);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Delete fields",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        await repository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { Title = "Recovery", Value = "delete-me" }
        ]);

        await repository.DeletePasswordPermanentlyAsync(password.Id);

        Assert.Empty(await repository.GetCustomFieldsAsync(password.Id));
        Assert.Empty(await repository.GetCustomFieldsByEntryIdsAsync([password.Id]));
        Assert.Empty(await repository.SearchEntryIdsByCustomFieldContentAsync("delete-me"));
    }

    [Fact]
    public async Task Repository_does_not_import_legacy_sqlite_custom_fields_when_default_mdbx_vault_is_added()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        var password = new PasswordEntry
        {
            Title = "Legacy fields",
            Password = "legacy-secret"
        };
        await sqliteRepository.SavePasswordAsync(password);
        await sqliteRepository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { Title = "Legacy hint", Value = "mother maiden" }
        ]);

        await SaveDefaultMdbxDatabaseAsync(repository);
        Assert.Empty(await repository.GetPasswordsAsync());
        Assert.Empty(await repository.GetCustomFieldsAsync(password.Id));
        Assert.Empty(await repository.SearchEntryIdsByCustomFieldContentAsync("maiden"));
        Assert.Equal("Legacy hint", Assert.Single(await sqliteRepository.GetCustomFieldsAsync(password.Id)).Title);
    }

    [Fact]
    public async Task Repository_roundtrips_password_history_through_mdbx_payload()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "With history",
            Password = "current"
        };
        await repository.SavePasswordAsync(password);
        var older = new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "older-secret",
            LastUsedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        var latest = new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "latest-secret",
            LastUsedAt = DateTimeOffset.UtcNow
        };

        await repository.SavePasswordHistoryAsync(older);
        await repository.SavePasswordHistoryAsync(latest);
        await sqliteRepository.SavePasswordAsync(CreateSqlitePasswordStub(password));
        await sqliteRepository.ClearPasswordHistoryAsync(password.Id);
        await sqliteRepository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "sqlite-stale",
            LastUsedAt = DateTimeOffset.UtcNow.AddMinutes(5)
        });

        var history = await repository.GetPasswordHistoryAsync(password.Id);

        Assert.Equal(["latest-secret", "older-secret"], history.Select(item => item.Password).ToArray());
        Assert.All(history, item => Assert.Equal(password.Id, item.EntryId));
        Assert.DoesNotContain(history, item => item.Password == "sqlite-stale");
    }

    [Fact]
    public async Task Repository_saves_password_history_when_sqlite_password_cache_is_missing()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "MDBX-only history",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);

        await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "history-secret",
            LastUsedAt = DateTimeOffset.UtcNow
        });

        var history = Assert.Single(await repository.GetPasswordHistoryAsync(password.Id));
        Assert.Equal("history-secret", history.Password);
        Assert.Equal(password.Id, history.EntryId);
    }

    [Fact]
    public async Task Repository_preserves_mdbx_only_fields_and_history_when_password_is_resaved()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "MDBX-only payload",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        await repository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { EntryId = password.Id, Title = "owner", Value = "preserve-field" }
        ]);
        await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "preserve-history",
            LastUsedAt = DateTimeOffset.UtcNow
        });
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);

        var mdbxOnlyPassword = Assert.Single(await repository.GetPasswordsAsync());
        mdbxOnlyPassword.Title = "MDBX-only payload edited";
        await repository.SavePasswordAsync(mdbxOnlyPassword);

        Assert.Equal("preserve-field", Assert.Single(await repository.GetCustomFieldsAsync(password.Id)).Value);
        Assert.Equal("preserve-history", Assert.Single(await repository.GetPasswordHistoryAsync(password.Id)).Password);
    }

    [Fact]
    public async Task Repository_preserves_mdbx_only_history_when_custom_fields_change()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "MDBX-only history with fields",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "history-before-fields",
            LastUsedAt = DateTimeOffset.UtcNow
        });
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);

        await repository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { EntryId = password.Id, Title = "owner", Value = "new-field" }
        ]);

        Assert.Equal("new-field", Assert.Single(await repository.GetCustomFieldsAsync(password.Id)).Value);
        Assert.Equal("history-before-fields", Assert.Single(await repository.GetPasswordHistoryAsync(password.Id)).Password);
    }

    [Fact]
    public async Task Repository_preserves_mdbx_only_custom_fields_when_password_history_changes()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "MDBX-only fields with history",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        await repository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { EntryId = password.Id, Title = "owner", Value = "field-before-history" }
        ]);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);

        await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "new-history",
            LastUsedAt = DateTimeOffset.UtcNow
        });

        Assert.Equal("field-before-history", Assert.Single(await repository.GetCustomFieldsAsync(password.Id)).Value);
        Assert.Equal("new-history", Assert.Single(await repository.GetPasswordHistoryAsync(password.Id)).Password);
    }

    [Fact]
    public async Task Repository_updates_existing_mdbx_history_when_sqlite_cache_is_missing()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "MDBX-only mutable history",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        for (var index = 0; index < 3; index++)
        {
            await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
            {
                EntryId = password.Id,
                Password = $"mdbx-history-{index}",
                LastUsedAt = DateTimeOffset.UtcNow.AddMinutes(index)
            });
        }

        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);

        await repository.TrimPasswordHistoryAsync(password.Id, 2);

        var trimmed = await repository.GetPasswordHistoryAsync(password.Id);
        Assert.Equal(["mdbx-history-2", "mdbx-history-1"], trimmed.Select(item => item.Password).ToArray());

        await repository.DeletePasswordHistoryAsync(trimmed[0].Id);

        Assert.Equal("mdbx-history-1", Assert.Single(await repository.GetPasswordHistoryAsync(password.Id)).Password);

        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);
        await repository.ClearPasswordHistoryAsync(password.Id);

        Assert.Empty(await repository.GetPasswordHistoryAsync(password.Id));
    }

    [Fact]
    public async Task Repository_trims_deletes_and_clears_password_history_in_mdbx_payload()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Mutable history",
            Password = "current"
        };
        await repository.SavePasswordAsync(password);
        for (var index = 0; index < 4; index++)
        {
            await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
            {
                EntryId = password.Id,
                Password = $"old-{index}",
                LastUsedAt = DateTimeOffset.UtcNow.AddMinutes(index)
            });
        }

        await repository.TrimPasswordHistoryAsync(password.Id, 2);

        var trimmed = await repository.GetPasswordHistoryAsync(password.Id);
        Assert.Equal(["old-3", "old-2"], trimmed.Select(item => item.Password).ToArray());

        await repository.DeletePasswordHistoryAsync(trimmed[0].Id);

        Assert.Equal("old-2", Assert.Single(await repository.GetPasswordHistoryAsync(password.Id)).Password);

        await repository.ClearPasswordHistoryAsync(password.Id);
        await sqliteRepository.SavePasswordAsync(CreateSqlitePasswordStub(password));
        await sqliteRepository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "sqlite-stale-after-clear",
            LastUsedAt = DateTimeOffset.UtcNow
        });

        Assert.Empty(await repository.GetPasswordHistoryAsync(password.Id));
    }

    [Fact]
    public async Task Repository_does_not_import_legacy_sqlite_password_history_when_default_mdbx_vault_is_added()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        var password = new PasswordEntry
        {
            Title = "Legacy history",
            Password = "current"
        };
        await sqliteRepository.SavePasswordAsync(password);
        await sqliteRepository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "legacy-secret",
            LastUsedAt = DateTimeOffset.UtcNow
        });

        await SaveDefaultMdbxDatabaseAsync(repository);
        Assert.Empty(await repository.GetPasswordsAsync());
        Assert.Empty(await repository.GetPasswordHistoryAsync(password.Id));
        Assert.Equal("legacy-secret", Assert.Single(await sqliteRepository.GetPasswordHistoryAsync(password.Id)).Password);
    }

    [Fact]
    public async Task Repository_roundtrips_secure_items_through_mdbx_store()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Recovery note",
            Notes = "keep this",
            ItemData = """{"body":"codes"}"""
        };
        var totp = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "GitHub OTP",
            ItemData = """{"secret":"JBSWY3DPEHPK3PXP"}"""
        };
        var card = new SecureItem
        {
            ItemType = VaultItemType.BankCard,
            Title = "Everyday Visa",
            ItemData = """{"number":"4111111111111111"}"""
        };

        await repository.SaveSecureItemAsync(note);
        await repository.SaveSecureItemAsync(totp);
        await repository.SaveSecureItemAsync(card);

        var all = await repository.GetSecureItemsAsync();
        var onlyTotp = await repository.GetSecureItemsAsync(VaultItemType.Totp);

        Assert.Equal(["Everyday Visa", "GitHub OTP", "Recovery note"], all.OrderBy(item => item.Title).Select(item => item.Title).ToArray());
        Assert.Equal("GitHub OTP", Assert.Single(onlyTotp).Title);
        Assert.All(all, item => Assert.False(string.IsNullOrWhiteSpace(item.MdbxFolderId)));

        var notePayloadJson = bridge.GetEntryPayloadJson(database.WorkingCopyPath!, note.MdbxFolderId!);
        using (var payload = JsonDocument.Parse(notePayloadJson!))
        {
            Assert.Equal("note", payload.RootElement.GetProperty("kind").GetString());
            Assert.Equal(note.Id, payload.RootElement.GetProperty("room_id").GetInt64());
            Assert.Equal(note.ItemData, payload.RootElement.GetProperty("item_data").GetString());
            Assert.True(payload.RootElement.TryGetProperty("attachments", out _));
            Assert.False(payload.RootElement.TryGetProperty("schemaVersion", out _));
            Assert.False(payload.RootElement.TryGetProperty("data", out _));
            Assert.False(payload.RootElement.TryGetProperty("title", out _));
        }

        await repository.SoftDeleteSecureItemAsync(note.Id);

        Assert.DoesNotContain(await repository.GetSecureItemsAsync(), item => item.Id == note.Id);
        Assert.Contains(await repository.GetSecureItemsAsync(includeDeleted: true), item => item.Id == note.Id && item.IsDeleted);
    }

    [Fact]
    public async Task Repository_prefers_mdbx_secure_item_payload_over_sqlite_cache()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "MDBX truth",
            Notes = "keep this",
            ItemData = """{"body":"mdbx"}"""
        };
        await repository.SaveSecureItemAsync(note);

        await sqliteRepository.SaveSecureItemAsync(new SecureItem
        {
            Id = note.Id,
            ItemType = VaultItemType.Note,
            Title = "SQLite stale",
            Notes = "stale notes",
            ItemData = """{"body":"sqlite"}""",
            MdbxDatabaseId = note.MdbxDatabaseId,
            MdbxFolderId = note.MdbxFolderId
        });

        var reloaded = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Note));

        Assert.Equal("MDBX truth", reloaded.Title);
        Assert.Equal("keep this", reloaded.Notes);
        Assert.Equal("""{"body":"mdbx"}""", reloaded.ItemData);
    }

    [Fact]
    public async Task Repository_soft_deletes_mdbx_secure_item_when_sqlite_cache_is_missing()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "MDBX-only note",
            Notes = "delete from MDBX"
        };
        await repository.SaveSecureItemAsync(note);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.SecureItems);

        Assert.Equal("MDBX-only note", Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Note)).Title);

        await repository.SoftDeleteSecureItemAsync(note.Id);

        Assert.Empty(await repository.GetSecureItemsAsync(VaultItemType.Note));
        Assert.Contains(await repository.GetSecureItemsAsync(VaultItemType.Note, includeDeleted: true), item => item.Id == note.Id && item.IsDeleted);
        Assert.Equal(0, bridge.CountActiveEntries(database.WorkingCopyPath!));
        Assert.Equal(1, bridge.CountDeletedEntries(database.WorkingCopyPath!));
    }

    [Fact]
    public async Task Repository_migrates_secure_item_image_paths_to_mdbx_attachments()
    {
        var contentStore = new FakeAttachmentContentStore();
        var repository = CreateRepository(out var bridge, contentStore);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var frontContent = "front image bytes"u8.ToArray();
        var backContent = "back image bytes"u8.ToArray();
        contentStore.Put("secure_attachments/front.png", frontContent);
        contentStore.Put("secure_attachments/back.png", backContent);
        var document = new SecureItem
        {
            ItemType = VaultItemType.Document,
            Title = "Passport",
            ItemData = WalletItemDataCodec.EncodeDocument(new DocumentWalletData
            {
                DocumentNumber = "P-123",
                ImagePaths = ["secure_attachments/front.png", "secure_attachments/back.png"]
            }),
            ImagePaths = WalletItemDataCodec.EncodeImagePaths(["secure_attachments/front.png", "secure_attachments/back.png"])
        };

        await repository.SaveSecureItemAsync(document);

        var reloaded = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Document));
        var imagePaths = WalletItemDataCodec.DecodeDocument(reloaded).ImagePaths;

        Assert.Equal(2, imagePaths.Count);
        Assert.All(imagePaths, path => Assert.StartsWith("mdbx:", path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(frontContent, bridge.ReadAttachmentContent(database.WorkingCopyPath!, imagePaths[0]));
        Assert.Equal(backContent, bridge.ReadAttachmentContent(database.WorkingCopyPath!, imagePaths[1]));
        Assert.Equal(frontContent, await repository.TryReadAttachmentContentAsync(new Attachment
        {
            OwnerType = "SECURE_ITEM",
            OwnerId = reloaded.Id,
            FileName = "front.png",
            ContentType = "image/png",
            StoragePath = imagePaths[0]
        }));
        Assert.Equal(backContent, await repository.TryReadAttachmentContentAsync(new Attachment
        {
            OwnerType = "SECURE_ITEM",
            OwnerId = reloaded.Id,
            FileName = "back.png",
            ContentType = "image/png",
            StoragePath = imagePaths[1]
        }));
        Assert.Null(contentStore.TryRead("secure_attachments/front.png"));
        Assert.Null(contentStore.TryRead("secure_attachments/back.png"));
        Assert.Equal(2, bridge.CountActiveAttachmentsForEntry(database.WorkingCopyPath!, reloaded.MdbxFolderId!));
    }

    [Fact]
    public async Task Repository_recovers_secure_item_attachments_from_mdbx_when_sqlite_cache_is_missing()
    {
        var contentStore = new FakeAttachmentContentStore();
        var repository = CreateRepository(out _, out var sqliteRepository, contentStore);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var frontContent = "front image bytes"u8.ToArray();
        contentStore.Put("secure_attachments/front.png", frontContent);
        var document = new SecureItem
        {
            ItemType = VaultItemType.Document,
            Title = "MDBX-only attachment list",
            ItemData = WalletItemDataCodec.EncodeDocument(new DocumentWalletData
            {
                DocumentNumber = "P-123",
                ImagePaths = ["secure_attachments/front.png"]
            }),
            ImagePaths = WalletItemDataCodec.EncodeImagePaths(["secure_attachments/front.png"])
        };

        await repository.SaveSecureItemAsync(document);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.SecureItems);

        var attachment = Assert.Single(await repository.GetAttachmentsAsync("SECURE_ITEM", document.Id));

        Assert.Equal("SECURE_ITEM", attachment.OwnerType);
        Assert.Equal(document.Id, attachment.OwnerId);
        Assert.Equal("front.png", attachment.FileName);
        Assert.Equal("image/png", attachment.ContentType);
        Assert.Equal(frontContent.Length, attachment.SizeBytes);
        Assert.StartsWith("mdbx:", attachment.StoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(frontContent, await repository.TryReadAttachmentContentAsync(attachment));
    }

    [Fact]
    public async Task Repository_migrates_import_restored_secure_item_images_to_mdbx_attachments()
    {
        var contentStore = new FakeAttachmentContentStore();
        var repository = CreateRepository(out var bridge, contentStore);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var document = new SecureItem
        {
            ItemType = VaultItemType.Document,
            Title = "Imported passport",
            ItemData = WalletItemDataCodec.EncodeDocument(new DocumentWalletData
            {
                DocumentNumber = "P-123",
                ImagePaths = ["mdbx:source-vault-image"]
            }),
            ImagePaths = WalletItemDataCodec.EncodeImagePaths(["mdbx:source-vault-image"])
        };

        await repository.SaveSecureItemAsync(document);

        Assert.Equal("mdbx:source-vault-image", Assert.Single(WalletItemDataCodec.DecodeDocument(document).ImagePaths));
        Assert.Equal(0, bridge.CountActiveAttachmentsForEntry(database.WorkingCopyPath!, document.MdbxFolderId!));

        var restoredContent = "restored imported image bytes"u8.ToArray();
        contentStore.Put("secure_attachments/imported-document.png", restoredContent);
        var importedData = WalletItemDataCodec.DecodeDocument(document);
        importedData.ImagePaths = ["secure_attachments/imported-document.png"];
        document.ItemData = WalletItemDataCodec.EncodeDocument(importedData);
        document.ImagePaths = WalletItemDataCodec.EncodeImagePaths(importedData.ImagePaths);

        await repository.SaveSecureItemAsync(document);

        var reloaded = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Document));
        var imagePath = Assert.Single(WalletItemDataCodec.DecodeDocument(reloaded).ImagePaths);

        Assert.StartsWith("mdbx:", imagePath, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("mdbx:source-vault-image", imagePath);
        Assert.Equal(restoredContent, bridge.ReadAttachmentContent(database.WorkingCopyPath!, imagePath));
        Assert.Null(contentStore.TryRead("secure_attachments/imported-document.png"));
        Assert.Equal(1, bridge.CountActiveAttachmentsForEntry(database.WorkingCopyPath!, reloaded.MdbxFolderId!));
    }

    [Fact]
    public async Task Repository_updates_secure_item_mdbx_attachments_when_image_paths_change()
    {
        var contentStore = new FakeAttachmentContentStore();
        var repository = CreateRepository(out var bridge, contentStore);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        contentStore.Put("secure_attachments/front.png", "front image bytes"u8.ToArray());
        contentStore.Put("secure_attachments/back.png", "back image bytes"u8.ToArray());
        var card = new SecureItem
        {
            ItemType = VaultItemType.BankCard,
            Title = "Card",
            ItemData = WalletItemDataCodec.EncodeBankCard(new BankCardWalletData
            {
                CardNumber = "4111111111111111",
                ImagePaths = ["secure_attachments/front.png"]
            }),
            ImagePaths = WalletItemDataCodec.EncodeImagePaths(["secure_attachments/front.png"])
        };
        await repository.SaveSecureItemAsync(card);
        var firstImagePath = Assert.Single(WalletItemDataCodec.DecodeBankCard(card).ImagePaths);

        var cardData = WalletItemDataCodec.DecodeBankCard(card);
        cardData.ImagePaths = ["secure_attachments/back.png"];
        card.ItemData = WalletItemDataCodec.EncodeBankCard(cardData);
        card.ImagePaths = WalletItemDataCodec.EncodeImagePaths(cardData.ImagePaths);
        await repository.SaveSecureItemAsync(card);

        var reloaded = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.BankCard));
        var imagePath = Assert.Single(WalletItemDataCodec.DecodeBankCard(reloaded).ImagePaths);

        Assert.StartsWith("mdbx:", imagePath, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(firstImagePath, imagePath);
        Assert.Equal("back image bytes"u8.ToArray(), bridge.ReadAttachmentContent(database.WorkingCopyPath!, imagePath));
        Assert.Null(bridge.TryReadAttachmentContent(database.WorkingCopyPath!, firstImagePath));
        Assert.Equal(1, bridge.CountActiveAttachmentsForEntry(database.WorkingCopyPath!, reloaded.MdbxFolderId!));
    }

    [Fact]
    public async Task Repository_updates_secure_item_mdbx_attachments_when_sqlite_cache_is_missing()
    {
        var contentStore = new FakeAttachmentContentStore();
        var repository = CreateRepository(out var bridge, out var sqliteRepository, contentStore);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        contentStore.Put("secure_attachments/front.png", "front image bytes"u8.ToArray());
        contentStore.Put("secure_attachments/back.png", "back image bytes"u8.ToArray());
        var card = new SecureItem
        {
            ItemType = VaultItemType.BankCard,
            Title = "MDBX-only card",
            ItemData = WalletItemDataCodec.EncodeBankCard(new BankCardWalletData
            {
                CardNumber = "4111111111111111",
                ImagePaths = ["secure_attachments/front.png"]
            }),
            ImagePaths = WalletItemDataCodec.EncodeImagePaths(["secure_attachments/front.png"])
        };
        await repository.SaveSecureItemAsync(card);
        var firstImagePath = Assert.Single(WalletItemDataCodec.DecodeBankCard(card).ImagePaths);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.SecureItems);

        var mdbxOnlyCard = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.BankCard));
        var cardData = WalletItemDataCodec.DecodeBankCard(mdbxOnlyCard);
        cardData.ImagePaths = ["secure_attachments/back.png"];
        mdbxOnlyCard.ItemData = WalletItemDataCodec.EncodeBankCard(cardData);
        mdbxOnlyCard.ImagePaths = WalletItemDataCodec.EncodeImagePaths(cardData.ImagePaths);

        await repository.SaveSecureItemAsync(mdbxOnlyCard);

        var reloaded = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.BankCard));
        var imagePath = Assert.Single(WalletItemDataCodec.DecodeBankCard(reloaded).ImagePaths);
        Assert.StartsWith("mdbx:", imagePath, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(firstImagePath, imagePath);
        Assert.Equal("back image bytes"u8.ToArray(), bridge.ReadAttachmentContent(database.WorkingCopyPath!, imagePath));
        Assert.Null(bridge.TryReadAttachmentContent(database.WorkingCopyPath!, firstImagePath));
        Assert.Null(contentStore.TryRead("secure_attachments/back.png"));
        Assert.Equal(1, bridge.CountActiveAttachmentsForEntry(database.WorkingCopyPath!, reloaded.MdbxFolderId!));
    }

    [Fact]
    public async Task Repository_soft_deletes_mdbx_secure_item_without_mutating_stale_sqlite_row()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var card = new SecureItem
        {
            ItemType = VaultItemType.BankCard,
            Title = "MDBX truth card",
            ItemData = WalletItemDataCodec.EncodeBankCard(new BankCardWalletData
            {
                CardNumber = "4111111111111111"
            }),
            ImagePaths = "[]"
        };
        await repository.SaveSecureItemAsync(card);
        var staleCard = new SecureItem
        {
            Id = card.Id,
            ItemType = VaultItemType.BankCard,
            Title = "SQLite stale card",
            ItemData = WalletItemDataCodec.EncodeBankCard(new BankCardWalletData
            {
                CardNumber = "4000000000000002"
            }),
            ImagePaths = "[]",
            CreatedAt = card.CreatedAt,
            UpdatedAt = card.UpdatedAt,
            MdbxDatabaseId = card.MdbxDatabaseId,
            MdbxFolderId = card.MdbxFolderId
        };
        await sqliteRepository.SaveSecureItemAsync(staleCard);

        await repository.SoftDeleteSecureItemAsync(card.Id);

        var deletedCard = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.BankCard, includeDeleted: true));
        var deletedCardData = WalletItemDataCodec.DecodeBankCard(deletedCard);
        Assert.True(deletedCard.IsDeleted);
        Assert.Equal("MDBX truth card", deletedCard.Title);
        Assert.Equal("4111111111111111", deletedCardData.CardNumber);

        var staleSqliteCard = Assert.Single(await sqliteRepository.GetSecureItemsAsync(VaultItemType.BankCard, includeDeleted: true));
        Assert.False(staleSqliteCard.IsDeleted);
        Assert.Equal("SQLite stale card", staleSqliteCard.Title);
    }

    [Fact]
    public async Task Repository_reads_legacy_secure_item_payloads_from_mdbx_store()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        using var vault = await bridge.OpenVaultAsync(database.WorkingCopyPath!, database.EncryptedPassword!, "monica-avalonia");
        var project = await vault.CreateProjectAsync("Monica");
        var legacyItem = new SecureItem
        {
            Id = 42,
            ItemType = VaultItemType.Note,
            Title = "Legacy secure payload",
            Notes = "old shape",
            ItemData = """{"body":"legacy"}"""
        };
        var legacyPayloadJson = JsonSerializer.Serialize(new
        {
            Kind = "secure-item",
            SchemaVersion = 1,
            Data = legacyItem
        }, MdbxPayloadJsonOptions);
        await vault.CreateEntryAsync(project.ProjectId, "note", legacyItem.Title, legacyPayloadJson);

        var reloaded = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Note));

        Assert.Equal(42, reloaded.Id);
        Assert.Equal("Legacy secure payload", reloaded.Title);
        Assert.Equal("old shape", reloaded.Notes);
        Assert.Equal("""{"body":"legacy"}""", reloaded.ItemData);
    }

    [Fact]
    public async Task Repository_rejects_business_writes_when_no_default_mdbx_vault_exists()
    {
        var repository = CreateRepository(out var bridge);
        var password = new PasswordEntry
        {
            Title = "SQLite only",
            Password = "secret"
        };

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => repository.SavePasswordAsync(password));

        Assert.Empty(await repository.GetPasswordsAsync());
        Assert.Empty(bridge.OpenedPaths);
    }

    [Fact]
    public async Task Repository_rebinds_new_items_with_foreign_ids_to_the_default_mdbx()
    {
        var repository = CreateRepository(out _);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Foreign password",
            Password = "secret",
            MdbxDatabaseId = 999,
            MdbxFolderId = "foreign-entry"
        };
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Foreign note",
            MdbxDatabaseId = 999,
            MdbxFolderId = "foreign-note"
        };

        await repository.SavePasswordAsync(password);
        await repository.SaveSecureItemAsync(note);

        var reloadedPassword = Assert.Single(await repository.GetPasswordsAsync());
        var reloadedNote = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Note));

        Assert.Equal(database.Id, reloadedPassword.MdbxDatabaseId);
        Assert.False(string.IsNullOrWhiteSpace(reloadedPassword.MdbxFolderId));
        Assert.Equal(database.Id, reloadedNote.MdbxDatabaseId);
        Assert.False(string.IsNullOrWhiteSpace(reloadedNote.MdbxFolderId));
    }

    [Fact]
    public async Task Repository_does_not_import_existing_sqlite_items_after_default_mdbx_vault_is_added()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        var password = new PasswordEntry
        {
            Title = "Before MDBX",
            Password = "legacy-secret"
        };
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Legacy note",
            Notes = "created before the vault"
        };
        await sqliteRepository.SavePasswordAsync(password);
        await sqliteRepository.SaveSecureItemAsync(note);

        await SaveDefaultMdbxDatabaseAsync(repository);

        Assert.Empty(await repository.GetPasswordsAsync());
        Assert.Empty(await repository.GetSecureItemsAsync(VaultItemType.Note));

        Assert.Equal("legacy-secret", Assert.Single(await sqliteRepository.GetPasswordsAsync()).Password);
        Assert.Equal("created before the vault", Assert.Single(await sqliteRepository.GetSecureItemsAsync(VaultItemType.Note)).Notes);
    }

    [Fact]
    public async Task Repository_rebinds_new_items_with_foreign_mdbx_ids_to_current_vault()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var localCategory = new Category { Name = "Local category" };
        await repository.SaveCategoryAsync(localCategory);
        var localPassword = new PasswordEntry
        {
            Title = "Local login",
            Password = "local-secret",
            CategoryId = localCategory.Id
        };
        var localNote = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Local note",
            Notes = "local truth",
            CategoryId = localCategory.Id
        };
        await repository.SavePasswordAsync(localPassword);
        await repository.SaveSecureItemAsync(localNote);
        var localCategoryMdbxId = localCategory.MdbxFolderId;
        var localPasswordMdbxId = localPassword.MdbxFolderId;
        var localNoteMdbxId = localNote.MdbxFolderId;

        var importedCategory = new Category
        {
            Name = "Imported category",
            MdbxDatabaseId = 999,
            MdbxFolderId = localCategoryMdbxId
        };
        await repository.SaveCategoryAsync(importedCategory);
        var importedPassword = new PasswordEntry
        {
            Title = "Imported login",
            Password = "imported-secret",
            MdbxDatabaseId = 999,
            MdbxFolderId = localPasswordMdbxId,
            CategoryId = importedCategory.Id
        };
        var importedNote = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Imported note",
            Notes = "imported truth",
            MdbxDatabaseId = 999,
            MdbxFolderId = localNoteMdbxId,
            CategoryId = importedCategory.Id
        };
        await repository.SavePasswordAsync(importedPassword);
        await repository.SaveSecureItemAsync(importedNote);

        var passwords = await repository.GetPasswordsAsync();
        var notes = await repository.GetSecureItemsAsync(VaultItemType.Note);
        var categories = await repository.GetCategoriesAsync();

        Assert.Equal(database.Id, importedCategory.MdbxDatabaseId);
        Assert.Equal(database.Id, importedPassword.MdbxDatabaseId);
        Assert.Equal(database.Id, importedNote.MdbxDatabaseId);
        Assert.NotEqual(localCategoryMdbxId, importedCategory.MdbxFolderId);
        Assert.NotEqual(localPasswordMdbxId, importedPassword.MdbxFolderId);
        Assert.NotEqual(localNoteMdbxId, importedNote.MdbxFolderId);
        Assert.Contains(passwords, item => item.Id == localPassword.Id && item.Title == "Local login");
        Assert.Contains(passwords, item => item.Id == importedPassword.Id && item.Title == "Imported login");
        Assert.Contains(notes, item => item.Id == localNote.Id && item.Title == "Local note");
        Assert.Contains(notes, item => item.Id == importedNote.Id && item.Title == "Imported note");
        Assert.Contains(categories, item => item.Id == localCategory.Id && item.Name == "Local category");
        Assert.Contains(categories, item => item.Id == importedCategory.Id && item.Name == "Imported category");
        Assert.Equal("Local category", bridge.GetProjectTitleForEntry(database.WorkingCopyPath!, localPassword.MdbxFolderId!));
        Assert.Equal("Imported category", bridge.GetProjectTitleForEntry(database.WorkingCopyPath!, importedPassword.MdbxFolderId!));
    }

    [Fact]
    public async Task Repository_roundtrips_categories_through_mdbx_projects()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var category = new Category
        {
            Name = "Work",
            SortOrder = 3
        };
        await repository.SaveCategoryAsync(category);
        var password = new PasswordEntry
        {
            Title = "Work login",
            Username = "dev",
            Password = "secret",
            CategoryId = category.Id
        };
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Work note",
            Notes = "project scoped",
            CategoryId = category.Id
        };

        await repository.SavePasswordAsync(password);
        await repository.SaveSecureItemAsync(note);

        var categories = await repository.GetCategoriesAsync();
        var reloadedCategory = Assert.Single(categories);
        var reloadedPassword = Assert.Single(await repository.GetPasswordsAsync());
        var reloadedNote = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Note));

        Assert.Equal(database.Id, reloadedCategory.MdbxDatabaseId);
        Assert.False(string.IsNullOrWhiteSpace(reloadedCategory.MdbxFolderId));
        Assert.Equal(category.Id, reloadedPassword.CategoryId);
        Assert.Equal(category.Id, reloadedNote.CategoryId);
        Assert.Contains("Work", bridge.GetProjectTitles(database.WorkingCopyPath!));
        Assert.Equal("Work", bridge.GetProjectTitleForEntry(database.WorkingCopyPath!, reloadedPassword.MdbxFolderId!));
        Assert.Equal("Work", bridge.GetProjectTitleForEntry(database.WorkingCopyPath!, reloadedNote.MdbxFolderId!));
    }

    [Fact]
    public async Task Repository_recovers_mdbx_project_categories_when_sqlite_category_cache_is_missing()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var category = new Category
        {
            Name = "Work",
            SortOrder = 3
        };
        await repository.SaveCategoryAsync(category);
        var password = new PasswordEntry
        {
            Title = "Work login",
            Password = "secret",
            CategoryId = category.Id
        };
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Work note",
            Notes = "project scoped",
            CategoryId = category.Id
        };
        await repository.SavePasswordAsync(password);
        await repository.SaveSecureItemAsync(note);
        await sqliteRepository.DeleteCategoryAsync(category.Id);

        var recoveredCategory = Assert.Single(await repository.GetCategoriesAsync());
        var reloadedPassword = Assert.Single(await repository.GetPasswordsAsync());
        var reloadedNote = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Note));

        Assert.NotEqual(0, recoveredCategory.Id);
        Assert.Equal("Work", recoveredCategory.Name);
        Assert.Equal(database.Id, recoveredCategory.MdbxDatabaseId);
        Assert.False(string.IsNullOrWhiteSpace(recoveredCategory.MdbxFolderId));
        Assert.Equal(recoveredCategory.Id, reloadedPassword.CategoryId);
        Assert.Equal(recoveredCategory.Id, reloadedNote.CategoryId);
        Assert.Equal("Work", bridge.GetProjectTitleForEntry(database.WorkingCopyPath!, reloadedPassword.MdbxFolderId!));
        Assert.Equal("Work", bridge.GetProjectTitleForEntry(database.WorkingCopyPath!, reloadedNote.MdbxFolderId!));
    }

    [Fact]
    public async Task Repository_recovers_mdbx_project_categories_for_deleted_entries_when_sqlite_category_cache_is_missing()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var category = new Category { Name = "Work" };
        await repository.SaveCategoryAsync(category);
        var password = new PasswordEntry
        {
            Title = "Deleted work login",
            Password = "secret",
            CategoryId = category.Id
        };
        await repository.SavePasswordAsync(password);
        await repository.SoftDeletePasswordAsync(password.Id);
        await sqliteRepository.DeleteCategoryAsync(category.Id);

        var recoveredCategory = Assert.Single(await repository.GetCategoriesAsync());
        var deletedPassword = Assert.Single(await repository.GetPasswordsAsync(includeDeleted: true));

        Assert.Equal("Work", recoveredCategory.Name);
        Assert.Equal(recoveredCategory.Id, deletedPassword.CategoryId);
        Assert.True(deletedPassword.IsDeleted);
        Assert.Equal("Work", bridge.GetProjectTitleForEntry(database.WorkingCopyPath!, deletedPassword.MdbxFolderId!));
    }

    [Fact]
    public async Task Repository_unassigns_mdbx_category_entries_even_when_sqlite_cache_is_stale()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var category = new Category { Name = "Work" };
        await repository.SaveCategoryAsync(category);
        var password = new PasswordEntry
        {
            Title = "Work login",
            Username = "dev",
            Password = "secret",
            CategoryId = category.Id
        };
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Work note",
            Notes = "project scoped",
            CategoryId = category.Id
        };
        await repository.SavePasswordAsync(password);
        await repository.ReplaceCustomFieldsAsync(password.Id, [
            new CustomField
            {
                EntryId = password.Id,
                Title = "env",
                Value = "prod"
            }
        ]);
        await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "old-secret",
            LastUsedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await repository.SaveSecureItemAsync(note);
        await sqliteRepository.ClearPasswordHistoryAsync(password.Id);
        await sqliteRepository.ReplaceCustomFieldsAsync(password.Id, []);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.SecureItems);

        Assert.Equal("Work", bridge.GetProjectTitleForEntry(database.WorkingCopyPath!, password.MdbxFolderId!));
        Assert.Equal("Work", bridge.GetProjectTitleForEntry(database.WorkingCopyPath!, note.MdbxFolderId!));

        await repository.DeleteCategoryAsync(category.Id);

        var reloadedPassword = Assert.Single(await repository.GetPasswordsAsync());
        var reloadedNote = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Note));
        var customField = Assert.Single(await repository.GetCustomFieldsAsync(password.Id));
        var history = Assert.Single(await repository.GetPasswordHistoryAsync(password.Id));
        Assert.Null(reloadedPassword.CategoryId);
        Assert.Null(reloadedNote.CategoryId);
        Assert.Equal("Monica", bridge.GetProjectTitleForEntry(database.WorkingCopyPath!, reloadedPassword.MdbxFolderId!));
        Assert.Equal("Monica", bridge.GetProjectTitleForEntry(database.WorkingCopyPath!, reloadedNote.MdbxFolderId!));
        Assert.Equal("env", customField.Title);
        Assert.Equal("prod", customField.Value);
        Assert.Equal("old-secret", history.Password);
        Assert.Equal("Work", Assert.Single(await repository.GetCategoriesAsync()).Name);
    }

    [Fact]
    public async Task Repository_moves_mdbx_password_entry_when_category_and_login_type_change()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var work = new Category { Name = "Work" };
        var personal = new Category { Name = "Personal" };
        await repository.SaveCategoryAsync(work);
        await repository.SaveCategoryAsync(personal);
        var password = new PasswordEntry
        {
            Title = "Movable login",
            Username = "dev",
            Password = "secret",
            CategoryId = work.Id,
            LoginType = PasswordLoginType.Password
        };
        await repository.SavePasswordAsync(password);
        var originalMdbxEntryId = password.MdbxFolderId;

        password.CategoryId = personal.Id;
        password.LoginType = PasswordLoginType.SshKey;
        password.SshKeyData = "private-key-material";
        await repository.SavePasswordAsync(password);

        var reloaded = Assert.Single(await repository.GetPasswordsAsync());

        Assert.Equal(originalMdbxEntryId, reloaded.MdbxFolderId);
        Assert.Equal(personal.Id, reloaded.CategoryId);
        Assert.Equal(PasswordLoginType.SshKey, reloaded.LoginType);
        Assert.Equal("Personal", bridge.GetProjectTitleForEntry(database.WorkingCopyPath!, reloaded.MdbxFolderId!));
        Assert.Equal(1, bridge.CountEntries(database.WorkingCopyPath!));
    }

    [Fact]
    public async Task Repository_saves_new_password_attachment_content_to_mdbx()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "With attachment",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var content = "attachment bytes"u8.ToArray();
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "recovery.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/recovery.enc",
            SizeBytes = content.Length
        };

        await repository.SaveAttachmentAsync(attachment, content);

        var saved = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.StartsWith("mdbx:", saved.StoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(content, bridge.ReadAttachmentContent(database.WorkingCopyPath!, saved.StoragePath));
        Assert.Equal(content, await repository.TryReadAttachmentContentAsync(saved));

        await repository.DeleteAttachmentAsync(saved.Id, saved);

        Assert.Empty(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Null(bridge.TryReadAttachmentContent(database.WorkingCopyPath!, saved.StoragePath));
    }

    [Fact]
    public async Task Repository_saves_password_attachment_to_mdbx_when_sqlite_password_cache_is_missing()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "MDBX-only attachment owner",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);
        var content = "mdbx-only attachment bytes"u8.ToArray();
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "owner-cache-missing.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/owner-cache-missing.enc",
            SizeBytes = content.Length
        };

        await repository.SaveAttachmentAsync(attachment, content);

        var saved = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Equal("owner-cache-missing.txt", saved.FileName);
        Assert.StartsWith("mdbx:", saved.StoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(content, bridge.ReadAttachmentContent(database.WorkingCopyPath!, saved.StoragePath));
        Assert.Equal(content, await repository.TryReadAttachmentContentAsync(saved));
    }

    [Fact]
    public async Task Repository_saves_password_attachment_metadata_when_sqlite_password_cache_is_missing()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "MDBX-only metadata owner",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "metadata-only.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/metadata-only.enc",
            SizeBytes = 42
        };

        await repository.SaveAttachmentAsync(attachment);

        var saved = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Equal("metadata-only.txt", saved.FileName);
        Assert.Equal(42, saved.SizeBytes);
    }

    [Fact]
    public async Task Repository_reads_password_attachment_metadata_from_mdbx_payload()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "MDBX attachment metadata",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var withoutAttachment = new PasswordEntry
        {
            Title = "MDBX without attachment",
            Password = "secret"
        };
        await repository.SavePasswordAsync(withoutAttachment);
        var content = "attachment metadata bytes"u8.ToArray();
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "codes.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/codes.enc",
            SizeBytes = content.Length
        };

        await repository.SaveAttachmentAsync(attachment, content);
        await sqliteRepository.DeleteAttachmentAsync(attachment.Id, attachment);
        await sqliteRepository.SaveAttachmentAsync(new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "sqlite-stale.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/stale.enc",
            SizeBytes = 99
        });

        var attachments = await repository.GetAttachmentsAsync("PASSWORD", password.Id);
        var grouped = await repository.GetAttachmentsByOwnerIdsAsync("PASSWORD", [password.Id]);
        var ownerIds = await repository.GetAttachmentOwnerIdsAsync("PASSWORD");
        var searchMatches = await repository.SearchAttachmentOwnerIdsAsync("PASSWORD", "codes");

        var saved = Assert.Single(attachments);
        Assert.Equal("codes.txt", saved.FileName);
        Assert.StartsWith("mdbx:", saved.StoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["codes.txt"], grouped[password.Id].Select(item => item.FileName).ToArray());
        Assert.Contains(password.Id, ownerIds);
        Assert.DoesNotContain(withoutAttachment.Id, ownerIds);
        Assert.Equal([password.Id], searchMatches);
        Assert.Empty(await repository.SearchAttachmentOwnerIdsAsync("PASSWORD", "sqlite-stale"));
    }

    [Fact]
    public async Task Repository_preserves_mdbx_only_attachment_metadata_when_password_is_resaved()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Preserve attachment",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "codes.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/codes.enc",
            SizeBytes = 5
        };
        await repository.SaveAttachmentAsync(attachment, "codes"u8.ToArray());
        var savedAttachment = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        await sqliteRepository.DeleteAttachmentAsync(savedAttachment.Id, savedAttachment);

        password.Title = "Preserved attachment";
        await repository.SavePasswordAsync(password);

        var reloadedAttachment = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Equal("codes.txt", reloadedAttachment.FileName);
        Assert.Equal(savedAttachment.StoragePath, reloadedAttachment.StoragePath);
        Assert.Equal("codes"u8.ToArray(), bridge.ReadAttachmentContent(database.WorkingCopyPath!, reloadedAttachment.StoragePath));
    }

    [Fact]
    public async Task Repository_preserves_mdbx_password_fields_when_attachment_sync_uses_stale_sqlite_cache()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "MDBX truth",
            Username = "fresh-user",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        password.Title = "SQLite stale";
        password.Username = "stale-user";
        await sqliteRepository.SavePasswordAsync(password);
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "codes.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/codes.enc",
            SizeBytes = 5
        };

        await repository.SaveAttachmentAsync(attachment, "codes"u8.ToArray());

        var reloaded = Assert.Single(await repository.GetPasswordsAsync());
        Assert.Equal("MDBX truth", reloaded.Title);
        Assert.Equal("fresh-user", reloaded.Username);
        Assert.Equal("codes.txt", Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id)).FileName);
    }

    [Fact]
    public async Task Repository_preserves_mdbx_only_attachment_metadata_when_custom_fields_change()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Preserve fields attachment",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "codes.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/codes.enc",
            SizeBytes = 5
        };
        await repository.SaveAttachmentAsync(attachment, "codes"u8.ToArray());
        var savedAttachment = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        await sqliteRepository.DeleteAttachmentAsync(savedAttachment.Id, savedAttachment);

        await repository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { Title = "Hint", Value = "Keep attachment" }
        ]);

        var reloadedAttachment = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Equal("codes.txt", reloadedAttachment.FileName);
        Assert.Equal(savedAttachment.StoragePath, reloadedAttachment.StoragePath);
        Assert.Equal("codes"u8.ToArray(), bridge.ReadAttachmentContent(database.WorkingCopyPath!, reloadedAttachment.StoragePath));
    }

    [Fact]
    public async Task Repository_updates_mdbx_attachment_metadata_after_delete()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Delete attachment metadata",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var first = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "first.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/first.enc",
            SizeBytes = 5
        };
        var second = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "second.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/second.enc",
            SizeBytes = 6
        };
        await repository.SaveAttachmentAsync(first, "first"u8.ToArray());
        await repository.SaveAttachmentAsync(second, "second"u8.ToArray());

        await repository.DeleteAttachmentAsync(first.Id, first);

        var remaining = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Equal("second.txt", remaining.FileName);
        Assert.Equal(1, bridge.CountActiveAttachments(database.WorkingCopyPath!));
        Assert.Null(bridge.TryReadAttachmentContent(database.WorkingCopyPath!, first.StoragePath));
        Assert.NotNull(bridge.TryReadAttachmentContent(database.WorkingCopyPath!, second.StoragePath));
    }

    [Fact]
    public async Task Repository_deletes_mdbx_attachment_content_when_deleted_by_id_only()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Delete attachment by id",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "codes.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/codes.enc",
            SizeBytes = 5
        };
        await repository.SaveAttachmentAsync(attachment, "codes"u8.ToArray());
        var saved = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));

        await repository.DeleteAttachmentAsync(saved.Id);

        Assert.Empty(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Equal(0, bridge.CountActiveAttachments(database.WorkingCopyPath!));
        Assert.Null(bridge.TryReadAttachmentContent(database.WorkingCopyPath!, saved.StoragePath));
    }

    [Fact]
    public async Task Repository_deletes_payload_only_mdbx_attachment_content_when_deleted_by_id_only()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Delete payload attachment by id",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "codes.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/codes.enc",
            SizeBytes = 5
        };
        await repository.SaveAttachmentAsync(attachment, "codes"u8.ToArray());
        var saved = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        await sqliteRepository.DeleteAttachmentAsync(saved.Id, saved);

        await repository.DeleteAttachmentAsync(saved.Id);

        Assert.Empty(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Equal(0, bridge.CountActiveAttachments(database.WorkingCopyPath!));
        Assert.Null(bridge.TryReadAttachmentContent(database.WorkingCopyPath!, saved.StoragePath));
    }

    [Fact]
    public async Task Repository_deletes_mdbx_attachment_by_id_when_sqlite_password_cache_is_missing()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Delete attachment without owner cache",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "codes.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/codes.enc",
            SizeBytes = 5
        };
        await repository.SaveAttachmentAsync(attachment, "codes"u8.ToArray());
        var saved = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);

        await repository.DeleteAttachmentAsync(saved.Id);

        Assert.Empty(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Equal(0, bridge.CountActiveAttachments(database.WorkingCopyPath!));
        Assert.Null(bridge.TryReadAttachmentContent(database.WorkingCopyPath!, saved.StoragePath));
    }

    [Fact]
    public async Task Repository_writes_password_attachment_content_to_mdbx_when_supplied()
    {
        var contentStore = new FakeAttachmentContentStore();
        var repository = CreateRepository(out var bridge, contentStore);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Legacy attachment",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var content = "legacy attachment bytes"u8.ToArray();
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "legacy.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/legacy.enc",
            SizeBytes = content.Length
        };
        await repository.SaveAttachmentAsync(attachment, content);

        var migrated = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        var reloaded = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));

        Assert.Equal(attachment.Id, migrated.Id);
        Assert.StartsWith("mdbx:", migrated.StoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(migrated.StoragePath, reloaded.StoragePath);
        Assert.Equal(content, bridge.ReadAttachmentContent(database.WorkingCopyPath!, migrated.StoragePath));
        Assert.Null(contentStore.TryRead("secure_attachments/legacy.enc"));
    }

    [Fact]
    public async Task Repository_reads_mdbx_attachment_content_when_sqlite_password_cache_is_missing()
    {
        var contentStore = new FakeAttachmentContentStore();
        var repository = CreateRepository(out var bridge, out var sqliteRepository, contentStore);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Legacy attachment without owner cache",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var content = "legacy owner cache missing bytes"u8.ToArray();
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "owner-cache-missing.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/owner-cache-missing.enc",
            SizeBytes = content.Length
        };
        await repository.SaveAttachmentAsync(attachment, content);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);

        var migrated = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));

        Assert.Equal(attachment.Id, migrated.Id);
        Assert.StartsWith("mdbx:", migrated.StoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(content, bridge.ReadAttachmentContent(database.WorkingCopyPath!, migrated.StoragePath));
        Assert.Null(contentStore.TryRead("secure_attachments/owner-cache-missing.enc"));
    }

    [Fact]
    public async Task Repository_does_not_import_legacy_password_attachments_after_default_mdbx_vault_is_added()
    {
        var contentStore = new FakeAttachmentContentStore();
        var repository = CreateRepository(out var bridge, out var sqliteRepository, contentStore);
        var password = new PasswordEntry
        {
            Title = "Legacy before vault",
            Password = "secret"
        };
        await sqliteRepository.SavePasswordAsync(password);
        var content = "legacy before vault bytes"u8.ToArray();
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "before-vault.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/before-vault.enc",
            SizeBytes = content.Length
        };
        await sqliteRepository.SaveAttachmentAsync(attachment);
        contentStore.Put(attachment.StoragePath, content);

        var database = await SaveDefaultMdbxDatabaseAsync(repository);

        Assert.Empty(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Equal(0, bridge.CountActiveAttachments(database.WorkingCopyPath!));
        Assert.Equal(content, contentStore.TryRead(attachment.StoragePath));
    }

    [Fact]
    public async Task Repository_clears_password_scope_from_mdbx_without_rehydrating_entries()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Portal",
            Username = "dev",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var boundNote = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Recovery",
            Notes = "keep this note",
            BoundPasswordId = password.Id
        };
        await repository.SaveSecureItemAsync(boundNote);

        await repository.ClearVaultDataAsync(VaultClearScope.Passwords);

        Assert.Empty(await repository.GetPasswordsAsync());
        Assert.True(Assert.Single(await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true)).IsDeleted);
        var remaining = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Note));
        Assert.Null(remaining.BoundPasswordId);
        Assert.Equal(1, bridge.CountActiveEntries(database.WorkingCopyPath!));
        Assert.Equal(1, bridge.CountDeletedEntries(database.WorkingCopyPath!));
    }

    [Fact]
    public async Task Repository_clears_password_scope_and_detaches_mdbx_only_bound_totp()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Portal",
            Username = "dev",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var boundTotp = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Portal OTP",
            ItemData = """{"secret":"JBSWY3DPEHPK3PXP"}""",
            BoundPasswordId = password.Id
        };
        await repository.SaveSecureItemAsync(boundTotp);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.SecureItems);

        await repository.ClearVaultDataAsync(VaultClearScope.Passwords);

        Assert.Empty(await repository.GetPasswordsAsync());
        var remaining = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Totp));
        Assert.Equal(boundTotp.Id, remaining.Id);
        Assert.Null(remaining.BoundPasswordId);
        Assert.Equal(1, bridge.CountActiveEntries(database.WorkingCopyPath!));
        Assert.Equal(1, bridge.CountDeletedEntries(database.WorkingCopyPath!));
    }

    [Fact]
    public async Task Repository_clears_secure_item_scope_from_mdbx_without_rehydrating_entries()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Portal",
            Password = "secret"
        };
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Recovery",
            Notes = "remove from secure scope"
        };
        await repository.SavePasswordAsync(password);
        await repository.SaveSecureItemAsync(note);

        await repository.ClearVaultDataAsync(VaultClearScope.SecureItems);

        Assert.Equal("Portal", Assert.Single(await repository.GetPasswordsAsync()).Title);
        Assert.True(Assert.Single(await repository.GetSecureItemsAsync(includeDeleted: true)).IsDeleted);
        Assert.Equal(1, bridge.CountActiveEntries(database.WorkingCopyPath!));
        Assert.Equal(1, bridge.CountDeletedEntries(database.WorkingCopyPath!));
    }

    [Fact]
    public async Task Repository_permanently_deletes_password_from_mdbx_without_rehydrating_entry()
    {
        var repository = CreateRepository(out var bridge);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Permanent",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "codes.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/codes.enc",
            SizeBytes = 5
        };
        await repository.SaveAttachmentAsync(attachment, "codes"u8.ToArray());
        var savedAttachment = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));

        await repository.DeletePasswordPermanentlyAsync(password.Id);

        Assert.True(Assert.Single(await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true)).IsDeleted);
        Assert.Empty(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Equal(0, bridge.CountActiveEntries(database.WorkingCopyPath!));
        Assert.Equal(1, bridge.CountDeletedEntries(database.WorkingCopyPath!));
        Assert.Equal(0, bridge.CountActiveAttachments(database.WorkingCopyPath!));
        Assert.Null(bridge.TryReadAttachmentContent(database.WorkingCopyPath!, savedAttachment.StoragePath));
    }

    [Fact]
    public async Task Repository_uses_mdbx_password_payload_when_sqlite_password_cache_is_missing()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Payload truth",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        await repository.ReplaceCustomFieldsAsync(password.Id, [
            new CustomField
            {
                EntryId = password.Id,
                Title = "env",
                Value = "prod"
            }
        ]);
        await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "old-secret",
            LastUsedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        var content = "payload bytes"u8.ToArray();
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "payload.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/payload.enc",
            SizeBytes = content.Length
        };
        await repository.SaveAttachmentAsync(attachment, content);
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);

        var reloaded = Assert.Single(await repository.GetPasswordsAsync());
        var field = Assert.Single(await repository.GetCustomFieldsAsync(password.Id));
        var history = Assert.Single(await repository.GetPasswordHistoryAsync(password.Id));
        var savedAttachment = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Equal(password.Id, reloaded.Id);
        Assert.Equal("env", field.Title);
        Assert.Equal("prod", field.Value);
        Assert.Equal("old-secret", history.Password);
        Assert.Equal("payload.txt", savedAttachment.FileName);
        Assert.Equal(content, await repository.TryReadAttachmentContentAsync(savedAttachment));

        await repository.SoftDeletePasswordAsync(password.Id);

        Assert.Empty(await repository.GetPasswordsAsync());
        Assert.Contains(await repository.GetPasswordsAsync(includeDeleted: true), entry => entry.Id == password.Id && entry.IsDeleted);

        await repository.RestorePasswordAsync(password.Id);

        Assert.Equal("Payload truth", Assert.Single(await repository.GetPasswordsAsync()).Title);
    }

    [Fact]
    public async Task Repository_permanently_deletes_mdbx_password_when_sqlite_password_cache_is_missing()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "MDBX-only delete",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var totp = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "MDBX-only OTP",
            ItemData = """{"secret":"JBSWY3DPEHPK3PXP"}""",
            BoundPasswordId = password.Id
        };
        await repository.SaveSecureItemAsync(totp);
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "codes.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/codes.enc",
            SizeBytes = 5
        };
        await repository.SaveAttachmentAsync(attachment, "codes"u8.ToArray());
        var savedAttachment = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        await sqliteRepository.ClearVaultDataAsync(VaultClearScope.Passwords);

        await repository.DeletePasswordPermanentlyAsync(password.Id);

        Assert.Empty(await repository.GetPasswordsAsync());
        Assert.Equal(0, bridge.CountActiveEntries(database.WorkingCopyPath!));
        Assert.Equal(2, bridge.CountDeletedEntries(database.WorkingCopyPath!));
        Assert.Equal(0, bridge.CountActiveAttachments(database.WorkingCopyPath!));
        Assert.Null(bridge.TryReadAttachmentContent(database.WorkingCopyPath!, savedAttachment.StoragePath));
    }

    [Fact]
    public async Task Repository_permanent_delete_removes_attachments_found_only_in_mdbx_payload()
    {
        var repository = CreateRepository(out var bridge, out var sqliteRepository);
        var database = await SaveDefaultMdbxDatabaseAsync(repository);
        var password = new PasswordEntry
        {
            Title = "Payload-only attachment",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = password.Id,
            FileName = "codes.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/codes.enc",
            SizeBytes = 5
        };
        await repository.SaveAttachmentAsync(attachment, "codes"u8.ToArray());
        var savedAttachment = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        await sqliteRepository.DeleteAttachmentAsync(savedAttachment.Id, savedAttachment);

        await repository.DeletePasswordPermanentlyAsync(password.Id);

        Assert.True(Assert.Single(await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true)).IsDeleted);
        Assert.Equal(0, bridge.CountActiveAttachments(database.WorkingCopyPath!));
        Assert.Null(bridge.TryReadAttachmentContent(database.WorkingCopyPath!, savedAttachment.StoragePath));
    }

    private static IMonicaRepository CreateRepository(out FakeMdbxNativeBridge bridge, IAttachmentContentStore? attachmentContentStore = null) =>
        CreateRepository(out bridge, out _, attachmentContentStore);

    private static PasswordEntry CreateSqlitePasswordStub(PasswordEntry source) => new()
    {
        Id = source.Id,
        Title = $"SQLite stale {source.Title}",
        Password = "sqlite-stale",
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt
    };

    private static IMonicaRepository CreateRepository(out FakeMdbxNativeBridge bridge, out IMonicaRepository sqliteRepository, IAttachmentContentStore? attachmentContentStore = null)
    {
        var factory = new SqliteConnectionFactory(GetTempDatabasePath());
        var inner = new MonicaRepository(factory, new DatabaseMigrator(factory));
        sqliteRepository = inner;
        bridge = new FakeMdbxNativeBridge();
        return new MdbxBackedMonicaRepository(inner, new MdbxVaultStore(bridge), attachmentContentStore);
    }

    private static async Task<LocalMdbxDatabase> SaveDefaultMdbxDatabaseAsync(
        IMonicaRepository repository,
        MdbxStorageLocation storageLocation = MdbxStorageLocation.Internal,
        string sourceType = "LOCAL_INTERNAL")
    {
        var database = new LocalMdbxDatabase
        {
            Name = "Local",
            FilePath = Path.Combine(GetTempRootPath(), $"{Guid.NewGuid():N}.mdbx"),
            WorkingCopyPath = Path.Combine(GetTempRootPath(), $"{Guid.NewGuid():N}.mdbx"),
            StorageLocation = storageLocation,
            SourceType = sourceType,
            EncryptedPassword = "test-mdbx-password",
            IsDefault = true,
            IsOfflineAvailable = true,
            LastSyncStatus = storageLocation is MdbxStorageLocation.Internal or MdbxStorageLocation.External
                ? SyncStatus.LocalOnly
                : SyncStatus.PendingUpload
        };
        await repository.SaveMdbxDatabaseAsync(database);
        return database;
    }

    private static string GetTempDatabasePath()
    {
        var path = Path.Combine(GetTempRootPath(), $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static string ReadMdbxFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mdbx", fileName));

    private static string GetTempRootPath()
    {
        return TestTempPaths.Root;
    }

    private sealed class FakeMdbxNativeBridge : IMdbxNativeBridge
    {
        private readonly Dictionary<string, FakeMdbxNativeVault> _vaults = new(StringComparer.OrdinalIgnoreCase);

        public bool IsAvailable => true;
        public List<string> OpenedPaths { get; } = [];

        public Task<IMdbxNativeVault> CreateVaultAsync(string path, string password, string deviceId, MdbxTigaMode mode, CancellationToken cancellationToken = default)
        {
            var vault = new FakeMdbxNativeVault(deviceId);
            _vaults[path] = vault;
            return Task.FromResult<IMdbxNativeVault>(vault);
        }

        public Task<IMdbxNativeVault> OpenVaultAsync(string path, string password, string deviceId, CancellationToken cancellationToken = default)
        {
            OpenedPaths.Add(path);
            if (!_vaults.TryGetValue(path, out var vault))
            {
                vault = new FakeMdbxNativeVault(deviceId);
                _vaults[path] = vault;
            }

            return Task.FromResult<IMdbxNativeVault>(vault);
        }

        public byte[] ReadAttachmentContent(string path, string storagePath) =>
            TryReadAttachmentContent(path, storagePath)
            ?? throw new InvalidOperationException($"Attachment '{storagePath}' was not found.");

        public byte[]? TryReadAttachmentContent(string path, string storagePath)
        {
            if (!_vaults.TryGetValue(path, out var vault))
            {
                return null;
            }

            var attachmentId = MdbxVaultStore.TryParseAttachmentStoragePath(storagePath);
            return attachmentId is null ? null : vault.TryReadAttachmentContent(attachmentId);
        }

        public IReadOnlyList<string> GetProjectTitles(string path) =>
            _vaults.TryGetValue(path, out var vault) ? vault.GetProjectTitles() : [];

        public string? GetProjectTitleForEntry(string path, string entryId) =>
            _vaults.TryGetValue(path, out var vault) ? vault.GetProjectTitleForEntry(entryId) : null;

        public int CountEntries(string path) =>
            _vaults.TryGetValue(path, out var vault) ? vault.CountEntries() : 0;

        public int CountActiveEntries(string path) =>
            _vaults.TryGetValue(path, out var vault) ? vault.CountEntries(deleted: false) : 0;

        public int CountDeletedEntries(string path) =>
            _vaults.TryGetValue(path, out var vault) ? vault.CountEntries(deleted: true) : 0;

        public int CountActiveAttachments(string path) =>
            _vaults.TryGetValue(path, out var vault) ? vault.CountAttachments(deleted: false) : 0;

        public int CountActiveAttachmentsForEntry(string path, string entryId) =>
            _vaults.TryGetValue(path, out var vault) ? vault.CountAttachments(entryId, deleted: false) : 0;

        public string? GetEntryPayloadJson(string path, string entryId) =>
            _vaults.TryGetValue(path, out var vault) ? vault.GetEntryPayloadJson(entryId) : null;

        public void SeedEntry(string path, string projectTitle, string entryType, string title, string payloadJson)
        {
            if (!_vaults.TryGetValue(path, out var vault))
            {
                vault = new FakeMdbxNativeVault("seed-device");
                _vaults[path] = vault;
            }

            vault.SeedEntry(projectTitle, entryType, title, payloadJson);
        }
    }

    private sealed class FakeAttachmentContentStore : IAttachmentContentStore
    {
        private readonly Dictionary<string, byte[]> _content = new(StringComparer.OrdinalIgnoreCase);

        public void Put(string storagePath, byte[] content) =>
            _content[storagePath] = content.ToArray();

        public byte[]? TryRead(string storagePath) =>
            _content.TryGetValue(storagePath, out var content) ? content.ToArray() : null;

        public Task<byte[]?> TryReadAttachmentContentAsync(Attachment attachment, CancellationToken cancellationToken = default) =>
            Task.FromResult(TryRead(attachment.StoragePath));

        public Task DeleteAttachmentContentAsync(Attachment attachment, CancellationToken cancellationToken = default)
        {
            _content.Remove(attachment.StoragePath);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMdbxNativeVault(string deviceId) : IMdbxNativeVault
    {
        private readonly List<MdbxNativeProjectRecord> _projects = [];
        private readonly List<MdbxNativeEntryRecord> _entries = [];
        private readonly List<MdbxNativeAttachmentRecord> _attachments = [];
        private readonly Dictionary<string, byte[]> _attachmentContent = [];
        private int _nextProjectId = 1;
        private int _nextEntryId = 1;
        private int _nextAttachmentId = 1;

        public Task<MdbxNativeVaultInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new MdbxNativeVaultInfo("fake-vault", deviceId));

        public Task<MdbxNativeProjectRecord> CreateProjectAsync(string title, CancellationToken cancellationToken = default)
        {
            var project = new MdbxNativeProjectRecord($"project-{_nextProjectId++}", title, Deleted: false);
            _projects.Add(project);
            return Task.FromResult(project);
        }

        public Task<IReadOnlyList<MdbxNativeProjectRecord>> ListProjectsAsync(bool includeDeleted, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MdbxNativeProjectRecord>>(
                _projects.Where(project => includeDeleted || !project.Deleted).ToList());

        public Task<MdbxNativeEntryRecord> CreateEntryAsync(string projectId, string entryType, string title, string payloadJson, CancellationToken cancellationToken = default)
        {
            var entry = new MdbxNativeEntryRecord($"entry-{_nextEntryId++}", projectId, entryType, title, payloadJson, Deleted: false);
            _entries.Add(entry);
            return Task.FromResult(entry);
        }

        public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListEntriesAsync(string projectId, string? entryType = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MdbxNativeEntryRecord>>(
                _entries.Where(entry => Matches(entry, projectId, entryType) && !entry.Deleted).ToList());

        public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListDeletedEntriesAsync(string projectId, string? entryType = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MdbxNativeEntryRecord>>(
                _entries.Where(entry => Matches(entry, projectId, entryType) && entry.Deleted).ToList());

        public Task<MdbxNativeEntryRecord> UpdateEntryAsync(string projectId, string entryId, string entryType, string title, string payloadJson, CancellationToken cancellationToken = default)
        {
            var index = _entries.FindIndex(entry => entry.EntryId == entryId && entry.ProjectId == projectId);
            if (index < 0)
            {
                throw new InvalidOperationException($"Entry '{entryId}' was not found.");
            }

            var updated = _entries[index] with
            {
                EntryType = entryType,
                Title = title,
                PayloadJson = payloadJson
            };
            _entries[index] = updated;
            return Task.FromResult(updated);
        }

        public Task<MdbxNativeEntryRecord> MoveEntryAsync(string projectId, string entryId, string targetProjectId, CancellationToken cancellationToken = default)
        {
            var index = _entries.FindIndex(entry => entry.EntryId == entryId && entry.ProjectId == projectId);
            if (index < 0)
            {
                throw new InvalidOperationException($"Entry '{entryId}' was not found.");
            }

            var updated = _entries[index] with { ProjectId = targetProjectId };
            _entries[index] = updated;
            return Task.FromResult(updated);
        }

        public Task DeleteEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default)
        {
            SetDeleted(projectId, entryId, deleted: true);
            return Task.CompletedTask;
        }

        public Task<MdbxNativeEntryRecord> RestoreEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SetDeleted(projectId, entryId, deleted: false));

        public Task<MdbxNativeAttachmentRecord> CreateAttachmentMetadataAsync(
            string projectId,
            string? entryId,
            string fileName,
            string? mediaType,
            string contentHash,
            ulong originalSize,
            CancellationToken cancellationToken = default)
        {
            var attachment = new MdbxNativeAttachmentRecord(
                $"attachment-{_nextAttachmentId++}",
                projectId,
                entryId,
                fileName,
                mediaType,
                "metadata-only",
                contentHash,
                originalSize,
                0,
                0,
                Deleted: false);
            _attachments.Add(attachment);
            return Task.FromResult(attachment);
        }

        public Task<IReadOnlyList<MdbxNativeAttachmentRecord>> ListAttachmentsByProjectAsync(string projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MdbxNativeAttachmentRecord>>(
                _attachments.Where(attachment => attachment.ProjectId == projectId && !attachment.Deleted).ToList());

        public Task<IReadOnlyList<MdbxNativeAttachmentRecord>> ListAttachmentsByEntryAsync(string entryId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MdbxNativeAttachmentRecord>>(
                _attachments.Where(attachment => attachment.EntryId == entryId && !attachment.Deleted).ToList());

        public Task<MdbxNativeAttachmentRecord> WriteAttachmentInlineContentAsync(string attachmentId, byte[] content, CancellationToken cancellationToken = default)
        {
            var index = _attachments.FindIndex(attachment => attachment.AttachmentId == attachmentId);
            if (index < 0)
            {
                throw new InvalidOperationException($"Attachment '{attachmentId}' was not found.");
            }

            _attachmentContent[attachmentId] = content.ToArray();
            var updated = _attachments[index] with
            {
                StorageMode = "embedded-inline",
                OriginalSize = (ulong)content.LongLength,
                StoredSize = (ulong)content.LongLength,
                ChunkCount = 1
            };
            _attachments[index] = updated;
            return Task.FromResult(updated);
        }

        public Task<byte[]> ReadAttachmentContentAsync(string attachmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TryReadAttachmentContent(attachmentId) ?? throw new InvalidOperationException($"Attachment '{attachmentId}' was not found."));

        public Task<MdbxNativeAttachmentRecord> RenameAttachmentAsync(string attachmentId, string fileName, string? mediaType, CancellationToken cancellationToken = default)
        {
            var index = _attachments.FindIndex(attachment => attachment.AttachmentId == attachmentId);
            if (index < 0)
            {
                throw new InvalidOperationException($"Attachment '{attachmentId}' was not found.");
            }

            var updated = _attachments[index] with
            {
                FileName = fileName,
                MediaType = mediaType
            };
            _attachments[index] = updated;
            return Task.FromResult(updated);
        }

        public Task DeleteAttachmentAsync(string attachmentId, CancellationToken cancellationToken = default)
        {
            var index = _attachments.FindIndex(attachment => attachment.AttachmentId == attachmentId);
            if (index >= 0)
            {
                _attachments[index] = _attachments[index] with { Deleted = true };
            }

            _attachmentContent.Remove(attachmentId);
            return Task.CompletedTask;
        }

        public byte[]? TryReadAttachmentContent(string attachmentId) =>
            _attachmentContent.TryGetValue(attachmentId, out var content) ? content.ToArray() : null;

        public IReadOnlyList<string> GetProjectTitles() =>
            _projects.Select(project => project.Title).ToList();

        public string? GetProjectTitleForEntry(string entryId)
        {
            var entry = _entries.FirstOrDefault(entry => string.Equals(entry.EntryId, entryId, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return null;
            }

            return _projects.FirstOrDefault(project => string.Equals(project.ProjectId, entry.ProjectId, StringComparison.OrdinalIgnoreCase))?.Title;
        }

        public int CountEntries() => _entries.Count;

        public int CountEntries(bool deleted) =>
            _entries.Count(entry => entry.Deleted == deleted);

        public int CountAttachments(bool deleted) =>
            _attachments.Count(attachment => attachment.Deleted == deleted);

        public int CountAttachments(string entryId, bool deleted) =>
            _attachments.Count(attachment => attachment.EntryId == entryId && attachment.Deleted == deleted);

        public string? GetEntryPayloadJson(string entryId) =>
            _entries.FirstOrDefault(entry => string.Equals(entry.EntryId, entryId, StringComparison.OrdinalIgnoreCase))?.PayloadJson;

        public void SeedEntry(string projectTitle, string entryType, string title, string payloadJson)
        {
            var project = _projects.FirstOrDefault(project => string.Equals(project.Title, projectTitle, StringComparison.OrdinalIgnoreCase));
            if (project is null)
            {
                project = new MdbxNativeProjectRecord($"project-{_nextProjectId++}", projectTitle, Deleted: false);
                _projects.Add(project);
            }

            _entries.Add(new MdbxNativeEntryRecord(
                $"entry-{_nextEntryId++}",
                project.ProjectId,
                entryType,
                title,
                payloadJson,
                Deleted: false));
        }

        private MdbxNativeEntryRecord SetDeleted(string projectId, string entryId, bool deleted)
        {
            var index = _entries.FindIndex(entry => entry.EntryId == entryId && entry.ProjectId == projectId);
            if (index < 0)
            {
                throw new InvalidOperationException($"Entry '{entryId}' was not found.");
            }

            var updated = _entries[index] with { Deleted = deleted };
            _entries[index] = updated;
            return updated;
        }

        private static bool Matches(MdbxNativeEntryRecord entry, string projectId, string? entryType) =>
            entry.ProjectId == projectId &&
            (entryType is null || string.Equals(entry.EntryType, entryType, StringComparison.OrdinalIgnoreCase));

        public void Dispose()
        {
        }
    }
}
