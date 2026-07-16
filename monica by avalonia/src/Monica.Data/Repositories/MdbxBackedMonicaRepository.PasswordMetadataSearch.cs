namespace Monica.Data.Repositories;

public sealed partial class MdbxBackedMonicaRepository
{
    public async Task<PasswordMetadataSearchResult> SearchPasswordMetadataAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new PasswordMetadataSearchResult([], []);
        }

        var database = await GetDefaultLocalMdbxDatabaseAsync(cancellationToken);
        return database is null
            ? new PasswordMetadataSearchResult([], [])
            : await mdbxVaultStore.SearchPasswordMetadataAsync(database, query, cancellationToken);
    }
}
