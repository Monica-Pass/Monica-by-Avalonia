using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Platform.Services;

namespace Monica.App.Services;

public sealed record CanonicalVaultBootstrapResult(
    LocalMdbxDatabase Database,
    bool Created,
    LegacyBusinessDataInspection LegacyBusinessData);

public interface ICanonicalVaultBootstrapService
{
    Task<CanonicalVaultBootstrapResult> EnsureReadyAsync(CancellationToken cancellationToken = default);
}

public interface ICanonicalVaultPathProvider
{
    string CreateAvailablePath();
}

public sealed class CanonicalVaultPathProvider : ICanonicalVaultPathProvider
{
    public string CreateAvailablePath()
    {
        var preferred = MonicaAppDataPaths.GetPath(Path.Combine("mdbx", "local.mdbx"));
        if (!File.Exists(preferred))
        {
            return preferred;
        }

        return MonicaAppDataPaths.GetPath(Path.Combine("mdbx", $"local-{Guid.NewGuid():N}.mdbx"));
    }
}

/// <summary>
/// Ensures an unlocked desktop profile has one usable default MDBX vault.
/// Legacy SQLite business rows are reported but never copied automatically.
/// </summary>
public sealed class CanonicalVaultBootstrapService(
    IMonicaRepository repository,
    IMdbxVaultService mdbxVaultService,
    ILegacyBusinessDataInspector legacyBusinessDataInspector,
    ICanonicalVaultPathProvider canonicalVaultPathProvider) : ICanonicalVaultBootstrapService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<CanonicalVaultBootstrapResult> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var legacyBusinessData = await legacyBusinessDataInspector.InspectAsync(cancellationToken);
            var databases = (await repository.GetMdbxDatabasesAsync(cancellationToken)).ToList();
            var currentDefault = databases
                .Where(database => database.IsDefault)
                .FirstOrDefault(IsUsable);
            if (currentDefault is not null)
            {
                return new CanonicalVaultBootstrapResult(currentDefault, Created: false, legacyBusinessData);
            }

            var existing = databases.FirstOrDefault(IsUsable);
            if (existing is not null)
            {
                await SetDefaultAsync(databases, existing, cancellationToken);
                return new CanonicalVaultBootstrapResult(existing, Created: false, legacyBusinessData);
            }

            var path = canonicalVaultPathProvider.CreateAvailablePath();
            var metadata = await mdbxVaultService.CreateLocalMetadataAsync(
                "Monica",
                path,
                MdbxTigaMode.Multi,
                cancellationToken);
            metadata.IsDefault = true;
            await repository.SaveMdbxDatabaseAsync(metadata, cancellationToken);
            return new CanonicalVaultBootstrapResult(metadata, Created: true, legacyBusinessData);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SetDefaultAsync(
        IReadOnlyList<LocalMdbxDatabase> databases,
        LocalMdbxDatabase selected,
        CancellationToken cancellationToken)
    {
        foreach (var database in databases)
        {
            var isDefault = database.Id == selected.Id;
            if (database.IsDefault == isDefault)
            {
                continue;
            }

            database.IsDefault = isDefault;
            await repository.SaveMdbxDatabaseAsync(database, cancellationToken);
        }
    }

    private static bool IsUsable(LocalMdbxDatabase database)
    {
        var path = database.WorkingCopyPath ?? database.FilePath;
        return !string.IsNullOrWhiteSpace(path) &&
               File.Exists(path) &&
               !string.IsNullOrWhiteSpace(database.EncryptedPassword);
    }

}
