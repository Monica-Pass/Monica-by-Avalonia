using Monica.App.Services;
using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Mdbx;
using Monica.Data.Repositories;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class CanonicalVaultBootstrapServiceTests
{
    [Fact]
    public void Legacy_business_data_notice_signature_is_stable_and_count_sensitive()
    {
        var first = new LegacyBusinessDataInspection(1, 2, 3, 4, 5, 6);
        var same = new LegacyBusinessDataInspection(1, 2, 3, 4, 5, 6);
        var changed = new LegacyBusinessDataInspection(2, 2, 3, 4, 5, 6);

        Assert.Equal(first.NoticeSignature, same.NoticeSignature);
        Assert.NotEqual(first.NoticeSignature, changed.NoticeSignature);
        Assert.Empty(LegacyBusinessDataInspection.Empty.NoticeSignature);
    }

    [Fact]
    public async Task Canonical_repository_bootstrap_creates_and_registers_default_mdbx()
    {
        var context = CreateContext();

        var result = await context.Bootstrap.EnsureReadyAsync();

        Assert.True(result.Created);
        Assert.True(result.Database.IsDefault);
        Assert.True(File.Exists(result.Database.WorkingCopyPath));
        Assert.False(result.LegacyBusinessData.HasData);
        var stored = Assert.Single(await context.Repository.GetMdbxDatabasesAsync());
        Assert.True(stored.IsDefault);
        Assert.Equal(result.Database.WorkingCopyPath, stored.WorkingCopyPath);
    }

    [Fact]
    public async Task Canonical_repository_bootstrap_reports_legacy_sqlite_rows_without_importing_them()
    {
        var context = CreateContext();
        await context.SqliteRepository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Legacy SQLite row",
            Password = "legacy-secret"
        });

        var result = await context.Bootstrap.EnsureReadyAsync();

        Assert.True(result.LegacyBusinessData.HasData);
        Assert.Equal(1, result.LegacyBusinessData.PasswordCount);
        Assert.Equal("Legacy SQLite row", Assert.Single(await context.SqliteRepository.GetPasswordsAsync()).Title);
        Assert.Empty(await context.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
    }

    [Fact]
    public async Task Canonical_repository_bootstrap_reuses_existing_usable_vault()
    {
        var context = CreateContext();
        var first = await context.Bootstrap.EnsureReadyAsync();

        var second = await context.Bootstrap.EnsureReadyAsync();

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Database.Id, second.Database.Id);
        Assert.Single(await context.Repository.GetMdbxDatabasesAsync());
    }

    private static TestContext CreateContext()
    {
        var root = TestTempPaths.CreateDirectoryPath();
        var factory = new SqliteConnectionFactory(Path.Combine(root, "metadata.db"));
        var migrator = new DatabaseMigrator(factory);
        var sqliteRepository = new MonicaRepository(factory, migrator);
        var repository = new MdbxBackedMonicaRepository(
            sqliteRepository,
            new MdbxVaultStore(new UnavailableMdbxNativeBridge()));
        var path = Path.Combine(root, "canonical.mdbx");
        var bootstrap = new CanonicalVaultBootstrapService(
            repository,
            new FakeMdbxVaultService(),
            new LegacyBusinessDataInspector(factory, migrator),
            new FixedPathProvider(path));
        return new TestContext(repository, sqliteRepository, bootstrap);
    }

    private sealed record TestContext(
        IMonicaRepository Repository,
        IMonicaRepository SqliteRepository,
        ICanonicalVaultBootstrapService Bootstrap);

    private sealed class FixedPathProvider(string path) : ICanonicalVaultPathProvider
    {
        public string CreateAvailablePath() => path;
    }

    private sealed class FakeMdbxVaultService : IMdbxVaultService
    {
        public Task<LocalMdbxDatabase> CreateLocalMetadataAsync(
            string name,
            string filePath,
            MdbxTigaMode mode = MdbxTigaMode.Multi,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllBytes(filePath, [1]);
            return Task.FromResult(new LocalMdbxDatabase
            {
                Name = name,
                FilePath = filePath,
                WorkingCopyPath = filePath,
                StorageLocation = MdbxStorageLocation.Internal,
                SourceType = "LOCAL_INTERNAL",
                TigaMode = mode,
                EncryptedPassword = "fixture-password",
                IsOfflineAvailable = true,
                LastSyncStatus = SyncStatus.LocalOnly
            });
        }

        public Task<Stream> OpenLocalStreamAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(File.OpenRead(database.WorkingCopyPath ?? database.FilePath));
    }
}
