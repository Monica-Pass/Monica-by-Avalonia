using Microsoft.Data.Sqlite;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    // View-model tests exercise transfer direction and metadata callbacks. Their small SQLite
    // fixtures are not encrypted native vaults; native backup/validation belongs to separate checks.
    private sealed class MdbxSnapshotTestVaultService : IMdbxVaultService
    {
        private readonly MdbxTestVaultEngine _engine = new();
        private readonly MdbxVaultService _local;

        public MdbxSnapshotTestVaultService() => _local = new MdbxVaultService(_engine);

        public Task<LocalMdbxDatabase> CreateLocalMetadataAsync(string name, string filePath,
            MdbxTigaMode mode = MdbxTigaMode.Multi, CancellationToken cancellationToken = default) =>
            _local.CreateLocalMetadataAsync(name, filePath, mode, cancellationToken);

        public Task<Stream> OpenLocalStreamAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default) =>
            _local.OpenLocalStreamAsync(database, cancellationToken);

        public async Task<Stream> OpenSnapshotStreamAsync(LocalMdbxDatabase database,
            CancellationToken cancellationToken = default) =>
            new MemoryStream(await File.ReadAllBytesAsync(LocalPath(database), cancellationToken), writable: false);

        public async Task CreateSnapshotAsync(LocalMdbxDatabase database, string destination,
            CancellationToken cancellationToken = default)
        {
            await using var snapshot = await OpenSnapshotStreamAsync(database, cancellationToken);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await snapshot.CopyToAsync(output, cancellationToken);
        }

        public async Task CommitSnapshotUploadAsync(LocalMdbxDatabase database, Stream snapshot,
            Func<bool, CancellationToken, Task> commitMetadata, CancellationToken cancellationToken = default)
        {
            var uploaded = Assert.IsType<MemoryStream>(snapshot).ToArray();
            var current = await File.ReadAllBytesAsync(LocalPath(database), cancellationToken);
            await commitMetadata(current.AsSpan().SequenceEqual(uploaded), cancellationToken);
        }

        public async Task<MdbxSnapshotRestoreResult> RestoreSnapshotAsync(LocalMdbxDatabase database,
            string incomingPath, Func<CancellationToken, Task> commitMetadata,
            CancellationToken cancellationToken = default)
        {
            // Inspect before publication so the existing invalid-download tests keep their contract.
            var inspection = await _engine.InspectAsync(incomingPath, cancellationToken);
            if (inspection.FormatVersion != "MDBX-2")
            {
                throw new InvalidOperationException("Invalid MDBX test fixture.");
            }
            var incoming = await File.ReadAllBytesAsync(incomingPath, cancellationToken);
            var target = LocalPath(database);
            var previous = File.Exists(target) ? await File.ReadAllBytesAsync(target, cancellationToken) : null;
            string? recovery = null;
            if (previous is not null && !previous.AsSpan().SequenceEqual(incoming))
            {
                recovery = Path.Combine(Path.GetDirectoryName(target)!,
                    $"{Path.GetFileNameWithoutExtension(target)}.local-conflict-{Guid.NewGuid():N}{Path.GetExtension(target)}");
                await File.WriteAllBytesAsync(recovery, previous, cancellationToken);
            }

            await File.WriteAllBytesAsync(target, incoming, cancellationToken);
            await commitMetadata(cancellationToken);
            return new MdbxSnapshotRestoreResult(recovery);
        }

        private static string LocalPath(LocalMdbxDatabase database) => database.WorkingCopyPath ?? database.FilePath;
    }

    private static async Task<byte[]> CreateChangedMdbxFixtureAsync(byte[] original)
    {
        var path = TestTempPaths.CreateFilePath(".mdbx");
        await File.WriteAllBytesAsync(path, original);
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            // Preserve vault identity while making the simulated remote revision differ from local.
            command.CommandText = "CREATE TABLE remote_fixture_revision (value TEXT NOT NULL); INSERT INTO remote_fixture_revision VALUES ('remote-v2');";
            await command.ExecuteNonQueryAsync();
        }

        return await File.ReadAllBytesAsync(path);
    }
}
