using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Repositories;

namespace Monica.Tests;

public sealed partial class DataRepositoryTests
{
    [Fact]
    public async Task Repository_restores_and_permanently_removes_secure_items()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Recycle note",
            ItemData = "{\"body\":\"secret\"}"
        };

        await repository.SaveSecureItemAsync(note);
        await repository.SoftDeleteSecureItemAsync(note.Id);
        var deleted = await repository.GetSecureItemsAsync(includeDeleted: true);
        Assert.Contains(deleted, item => item.Id == note.Id && item.IsDeleted && item.DeletedAt is not null);

        await repository.RestoreSecureItemAsync(note.Id);
        var restored = await repository.GetSecureItemsAsync();
        Assert.Contains(restored, item => item.Id == note.Id && !item.IsDeleted && item.DeletedAt is null);

        await repository.SoftDeleteSecureItemAsync(note.Id);
        await repository.DeleteSecureItemPermanentlyAsync(note.Id);
        var afterPurge = await repository.GetSecureItemsAsync(includeDeleted: true);
        Assert.DoesNotContain(afterPurge, item => item.Id == note.Id);
    }
}
