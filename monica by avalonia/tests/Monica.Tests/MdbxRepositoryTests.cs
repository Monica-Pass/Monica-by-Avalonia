using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Mdbx;
using Monica.Data.Repositories;

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

    private static IMonicaRepository CreateRepository(out FakeMdbxNativeBridge bridge)
    {
        var factory = new SqliteConnectionFactory(GetTempDatabasePath());
        var inner = new MonicaRepository(factory, new DatabaseMigrator(factory));
        bridge = new FakeMdbxNativeBridge();
        return new MdbxBackedMonicaRepository(inner, new MdbxVaultStore(bridge));
    }

    private static async Task SaveDefaultMdbxDatabaseAsync(IMonicaRepository repository)
    {
        await repository.SaveMdbxDatabaseAsync(new LocalMdbxDatabase
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
        });
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
    }

    private sealed class FakeMdbxNativeVault(string deviceId) : IMdbxNativeVault
    {
        private readonly List<MdbxNativeProjectRecord> _projects = [];
        private readonly List<MdbxNativeEntryRecord> _entries = [];
        private int _nextProjectId = 1;
        private int _nextEntryId = 1;

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
