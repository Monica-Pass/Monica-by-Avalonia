using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Mdbx;
using Monica.Data.Repositories;
using Monica.Data.Services;

namespace Monica.Tests;

public sealed class MdbxRepositoryTests
{
    [Fact]
    public async Task Repository_roundtrips_passwords_through_mdbx_store_when_default_vault_exists()
    {
        var repository = CreateRepository(out var bridge);
        await SaveDefaultMdbxDatabaseAsync(repository);
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
        Assert.Single(bridge.OpenedPaths);

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
        await sqliteRepository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { Title = "Old", Value = "stale-after-clear" }
        ]);

        Assert.Empty(await repository.GetCustomFieldsAsync(password.Id));
        Assert.Empty(await repository.SearchEntryIdsByCustomFieldContentAsync("stale-after-clear"));
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
    public async Task Repository_mirrors_legacy_sqlite_custom_fields_when_default_mdbx_vault_is_added()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        var password = new PasswordEntry
        {
            Title = "Legacy fields",
            Password = "legacy-secret"
        };
        await repository.SavePasswordAsync(password);
        await repository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { Title = "Legacy hint", Value = "mother maiden" }
        ]);

        await SaveDefaultMdbxDatabaseAsync(repository);
        Assert.Single(await repository.GetPasswordsAsync());
        await sqliteRepository.ReplaceCustomFieldsAsync(password.Id,
        [
            new CustomField { Title = "SQLite stale", Value = "after-mirror" }
        ]);

        var fields = await repository.GetCustomFieldsAsync(password.Id);

        Assert.Equal("Legacy hint", Assert.Single(fields).Title);
        Assert.Equal([password.Id], await repository.SearchEntryIdsByCustomFieldContentAsync("maiden"));
        Assert.Empty(await repository.SearchEntryIdsByCustomFieldContentAsync("after-mirror"));
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
        await sqliteRepository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "sqlite-stale-after-clear",
            LastUsedAt = DateTimeOffset.UtcNow
        });

        Assert.Empty(await repository.GetPasswordHistoryAsync(password.Id));
    }

    [Fact]
    public async Task Repository_mirrors_legacy_sqlite_password_history_when_default_mdbx_vault_is_added()
    {
        var repository = CreateRepository(out _, out var sqliteRepository);
        var password = new PasswordEntry
        {
            Title = "Legacy history",
            Password = "current"
        };
        await repository.SavePasswordAsync(password);
        await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "legacy-secret",
            LastUsedAt = DateTimeOffset.UtcNow
        });

        await SaveDefaultMdbxDatabaseAsync(repository);
        Assert.Single(await repository.GetPasswordsAsync());
        await sqliteRepository.ClearPasswordHistoryAsync(password.Id);
        await sqliteRepository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = password.Id,
            Password = "sqlite-stale-after-mirror",
            LastUsedAt = DateTimeOffset.UtcNow.AddMinutes(5)
        });

        var history = await repository.GetPasswordHistoryAsync(password.Id);

        Assert.Equal("legacy-secret", Assert.Single(history).Password);
    }

    [Fact]
    public async Task Repository_roundtrips_secure_items_through_mdbx_store()
    {
        var repository = CreateRepository(out _);
        await SaveDefaultMdbxDatabaseAsync(repository);
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

        await repository.SoftDeleteSecureItemAsync(note.Id);

        Assert.DoesNotContain(await repository.GetSecureItemsAsync(), item => item.Id == note.Id);
        Assert.Contains(await repository.GetSecureItemsAsync(includeDeleted: true), item => item.Id == note.Id && item.IsDeleted);
    }

    [Fact]
    public async Task Repository_keeps_sqlite_fallback_when_no_default_mdbx_vault_exists()
    {
        var repository = CreateRepository(out var bridge);
        var password = new PasswordEntry
        {
            Title = "SQLite only",
            Password = "secret"
        };

        await repository.SavePasswordAsync(password);

        var reloaded = Assert.Single(await repository.GetPasswordsAsync());
        Assert.Equal("SQLite only", reloaded.Title);
        Assert.Null(reloaded.MdbxDatabaseId);
        Assert.Empty(bridge.OpenedPaths);
    }

    [Fact]
    public async Task Repository_mirrors_existing_sqlite_items_after_default_mdbx_vault_is_added()
    {
        var repository = CreateRepository(out _);
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
        await repository.SavePasswordAsync(password);
        await repository.SaveSecureItemAsync(note);

        await SaveDefaultMdbxDatabaseAsync(repository);

        var mirroredPassword = Assert.Single(await repository.GetPasswordsAsync());
        var mirroredNote = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Note));

        Assert.Equal("legacy-secret", mirroredPassword.Password);
        Assert.NotNull(mirroredPassword.MdbxDatabaseId);
        Assert.False(string.IsNullOrWhiteSpace(mirroredPassword.MdbxFolderId));
        Assert.Equal("created before the vault", mirroredNote.Notes);
        Assert.NotNull(mirroredNote.MdbxDatabaseId);
        Assert.False(string.IsNullOrWhiteSpace(mirroredNote.MdbxFolderId));
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

        await repository.DeleteAttachmentAsync(saved.Id, saved);

        Assert.Empty(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Null(bridge.TryReadAttachmentContent(database.WorkingCopyPath!, saved.StoragePath));
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

        var saved = Assert.Single(attachments);
        Assert.Equal("codes.txt", saved.FileName);
        Assert.StartsWith("mdbx:", saved.StoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["codes.txt"], grouped[password.Id].Select(item => item.FileName).ToArray());
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
    public async Task Repository_migrates_existing_password_attachment_content_to_mdbx_when_listed()
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
        await repository.SaveAttachmentAsync(attachment);
        contentStore.Put(attachment.StoragePath, content);

        var migrated = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        var reloaded = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));

        Assert.Equal(attachment.Id, migrated.Id);
        Assert.StartsWith("mdbx:", migrated.StoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(migrated.StoragePath, reloaded.StoragePath);
        Assert.Equal(content, bridge.ReadAttachmentContent(database.WorkingCopyPath!, migrated.StoragePath));
        Assert.Null(contentStore.TryRead(attachment.StoragePath));
    }

    [Fact]
    public async Task Repository_migrates_legacy_password_attachments_after_default_mdbx_vault_is_added()
    {
        var contentStore = new FakeAttachmentContentStore();
        var repository = CreateRepository(out var bridge, contentStore);
        var password = new PasswordEntry
        {
            Title = "Legacy before vault",
            Password = "secret"
        };
        await repository.SavePasswordAsync(password);
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
        await repository.SaveAttachmentAsync(attachment);
        contentStore.Put(attachment.StoragePath, content);

        var database = await SaveDefaultMdbxDatabaseAsync(repository);

        var migrated = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        var reloaded = Assert.Single(await repository.GetAttachmentsAsync("PASSWORD", password.Id));

        Assert.StartsWith("mdbx:", migrated.StoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(migrated.StoragePath, reloaded.StoragePath);
        Assert.Equal(content, bridge.ReadAttachmentContent(database.WorkingCopyPath!, migrated.StoragePath));
        Assert.Null(contentStore.TryRead(attachment.StoragePath));
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
        Assert.Empty(await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        var remaining = Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Note));
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
        Assert.Empty(await repository.GetSecureItemsAsync(includeDeleted: true));
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

        Assert.Empty(await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.Empty(await repository.GetAttachmentsAsync("PASSWORD", password.Id));
        Assert.Equal(0, bridge.CountActiveEntries(database.WorkingCopyPath!));
        Assert.Equal(1, bridge.CountDeletedEntries(database.WorkingCopyPath!));
        Assert.Equal(0, bridge.CountActiveAttachments(database.WorkingCopyPath!));
        Assert.Null(bridge.TryReadAttachmentContent(database.WorkingCopyPath!, savedAttachment.StoragePath));
    }

    private static IMonicaRepository CreateRepository(out FakeMdbxNativeBridge bridge, IAttachmentContentStore? attachmentContentStore = null) =>
        CreateRepository(out bridge, out _, attachmentContentStore);

    private static IMonicaRepository CreateRepository(out FakeMdbxNativeBridge bridge, out IMonicaRepository sqliteRepository, IAttachmentContentStore? attachmentContentStore = null)
    {
        var factory = new SqliteConnectionFactory(GetTempDatabasePath());
        var inner = new MonicaRepository(factory, new DatabaseMigrator(factory));
        sqliteRepository = inner;
        bridge = new FakeMdbxNativeBridge();
        return new MdbxBackedMonicaRepository(inner, new MdbxVaultStore(bridge), attachmentContentStore);
    }

    private static async Task<LocalMdbxDatabase> SaveDefaultMdbxDatabaseAsync(IMonicaRepository repository)
    {
        var database = new LocalMdbxDatabase
        {
            Name = "Local",
            FilePath = Path.Combine(GetTempRootPath(), $"{Guid.NewGuid():N}.mdbx"),
            WorkingCopyPath = Path.Combine(GetTempRootPath(), $"{Guid.NewGuid():N}.mdbx"),
            StorageLocation = MdbxStorageLocation.Internal,
            SourceType = "LOCAL_INTERNAL",
            EncryptedPassword = "test-mdbx-password",
            IsDefault = true,
            IsOfflineAvailable = true,
            LastSyncStatus = SyncStatus.LocalOnly
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

    private static string GetTempRootPath()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "Monica by Avalonia.slnx")))
            {
                var root = Path.Combine(current, "artifacts", "monica-tests");
                Directory.CreateDirectory(root);
                return root;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        var fallback = Path.Combine(Path.GetTempPath(), "monica-tests");
        Directory.CreateDirectory(fallback);
        return fallback;
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
