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

    private static IMonicaRepository CreateRepository(out FakeMdbxNativeBridge bridge, IAttachmentContentStore? attachmentContentStore = null)
    {
        var factory = new SqliteConnectionFactory(GetTempDatabasePath());
        var inner = new MonicaRepository(factory, new DatabaseMigrator(factory));
        bridge = new FakeMdbxNativeBridge();
        return new MdbxBackedMonicaRepository(inner, new MdbxVaultStore(bridge), attachmentContentStore);
    }

    private static async Task<LocalMdbxDatabase> SaveDefaultMdbxDatabaseAsync(IMonicaRepository repository)
    {
        var database = new LocalMdbxDatabase
        {
            Name = "Local",
            FilePath = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.mdbx"),
            WorkingCopyPath = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.mdbx"),
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
        var path = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
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
