using Monica.Core.Models;
using Monica.Data.Repositories;

namespace Monica.App.ViewModels;

internal sealed record VaultLoadSnapshot(
    IReadOnlyList<PasswordEntry> ActivePasswords,
    IReadOnlyList<PasswordEntry> ArchivedPasswords,
    IReadOnlyList<PasswordEntry> DeletedPasswords,
    IReadOnlyList<long> PasswordAttachmentOwnerIds,
    IReadOnlyList<SecureItem> NoteItems,
    IReadOnlyList<SecureItem> WalletItems,
    IReadOnlyList<SecureItem> StoredTotps,
    IReadOnlyList<SecureItem> DeletedSecureItems,
    IReadOnlyList<Category> Categories,
    IReadOnlyDictionary<long, PasswordQuickAccessRecord> PasswordQuickAccessRecords,
    IReadOnlyList<LocalMdbxDatabase> MdbxDatabases)
{
    public IEnumerable<PasswordEntry> AllPasswords =>
        ActivePasswords.Concat(ArchivedPasswords).Concat(DeletedPasswords);

    public IReadOnlyList<SecureItem> PreparedTotpItems { get; init; } = [];
}

internal static class VaultSnapshotLoader
{
    public static async Task<VaultLoadSnapshot> LoadAsync(IMonicaRepository repository)
    {
        try
        {
            return await LoadCoreAsync(repository);
        }
        finally
        {
            (repository as ITransientVaultReadCache)?.ReleaseVaultItemSnapshots();
        }
    }

    private static async Task<VaultLoadSnapshot> LoadCoreAsync(IMonicaRepository repository)
    {
        var allPasswords = await AppDiagnostics.MeasureAsync(
            "Load passwords",
            () => repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        var allPasswordItems = allPasswords
            .Select(static item => item.CreateDetachedCopy())
            .ToArray();
        var activePasswords = allPasswordItems.Where(item => !item.IsDeleted && !item.IsArchived).ToArray();
        var archivedPasswords = allPasswordItems.Where(item => !item.IsDeleted && item.IsArchived).ToArray();
        var deletedPasswords = allPasswordItems
            .Where(item => item.IsDeleted && item.DeletedAt != DateTimeOffset.UnixEpoch)
            .ToArray();
        var attachmentOwnerIdsTask = AppDiagnostics.MeasureAsync(
            "Load password attachment owner IDs",
            () => repository.GetAttachmentOwnerIdsAsync("PASSWORD"));
        var secureItemsTask = AppDiagnostics.MeasureAsync(
            "Load secure items",
            () => repository.GetSecureItemsAsync(includeDeleted: true));
        var categoriesTask = AppDiagnostics.MeasureAsync(
            "Load categories",
            () => repository.GetCategoriesAsync());
        var quickAccessRecordsTask = AppDiagnostics.MeasureAsync(
            "Load password quick access",
            () => repository.GetPasswordQuickAccessRecordsAsync());
        var databasesTask = AppDiagnostics.MeasureAsync(
            "Load MDBX database metadata",
            () => repository.GetMdbxDatabasesAsync());

        await Task.WhenAll(
            attachmentOwnerIdsTask,
            secureItemsTask,
            categoriesTask,
            quickAccessRecordsTask,
            databasesTask);

        var attachmentOwnerIds = await attachmentOwnerIdsTask;
        var secureItems = (await secureItemsTask)
            .Select(static item => item.CreateDetachedCopy())
            .ToArray();
        var activeSecureItems = secureItems
            .Where(item => !item.IsDeleted && item.DeletedAt != DateTimeOffset.UnixEpoch)
            .ToArray();
        var deletedSecureItems = secureItems
            .Where(item => item.IsDeleted && item.DeletedAt != DateTimeOffset.UnixEpoch)
            .ToArray();
        var noteItems = activeSecureItems
            .Where(item => item.ItemType == VaultItemType.Note)
            .ToArray();
        var walletItems = activeSecureItems
            .Where(item => item.ItemType is VaultItemType.BankCard or VaultItemType.Document or
                VaultItemType.BillingAddress or VaultItemType.PaymentAccount)
            .ToArray();
        var storedTotps = activeSecureItems
            .Where(item => item.ItemType == VaultItemType.Totp)
            .ToArray();

        var categories = await categoriesTask;
        var quickAccessRecords = PasswordQuickAccessCache.Create(await quickAccessRecordsTask);
        var databases = await databasesTask;

        return new VaultLoadSnapshot(
            activePasswords,
            archivedPasswords,
            deletedPasswords,
            attachmentOwnerIds,
            noteItems,
            walletItems,
            storedTotps,
            deletedSecureItems,
            categories,
            quickAccessRecords,
            databases);
    }
}
