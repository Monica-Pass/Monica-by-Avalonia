using Monica.Core.Models;
using Monica.Data.Repositories;

namespace Monica.App.ViewModels;

internal sealed record VaultLoadSnapshot(
    IReadOnlyList<PasswordEntry> ActivePasswords,
    IReadOnlyList<PasswordEntry> ArchivedPasswords,
    IReadOnlyList<PasswordEntry> DeletedPasswords,
    IReadOnlyDictionary<long, IReadOnlyList<CustomField>> PasswordCustomFields,
    IReadOnlyDictionary<long, IReadOnlyList<Attachment>> PasswordAttachments,
    IReadOnlyList<SecureItem> NoteItems,
    IReadOnlyList<SecureItem> WalletItems,
    IReadOnlyList<SecureItem> StoredTotps,
    IReadOnlyList<Category> Categories,
    IReadOnlyDictionary<long, PasswordQuickAccessRecord> PasswordQuickAccessRecords,
    IReadOnlyList<LocalMdbxDatabase> MdbxDatabases)
{
    public IEnumerable<PasswordEntry> AllPasswords =>
        ActivePasswords.Concat(ArchivedPasswords).Concat(DeletedPasswords);
}

internal static class VaultSnapshotLoader
{
    public static async Task<VaultLoadSnapshot> LoadAsync(IMonicaRepository repository)
    {
        var allPasswords = await AppDiagnostics.MeasureAsync(
            "Load passwords",
            () => repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        var allPasswordItems = allPasswords
            .Select(static item => item.CreateDetachedCopy())
            .ToArray();
        var activePasswords = allPasswordItems.Where(item => !item.IsDeleted && !item.IsArchived).ToArray();
        var archivedPasswords = allPasswordItems.Where(item => !item.IsDeleted && item.IsArchived).ToArray();
        var deletedPasswords = allPasswordItems.Where(item => item.IsDeleted).ToArray();
        var passwordIds = allPasswordItems.Select(item => item.Id).ToArray();

        var customFieldsTask = AppDiagnostics.MeasureAsync(
            "Load password custom fields",
            () => repository.GetCustomFieldsByEntryIdsAsync(passwordIds));
        var attachmentsTask = AppDiagnostics.MeasureAsync(
            "Load password attachments",
            () => repository.GetAttachmentsByOwnerIdsAsync("PASSWORD", passwordIds));
        var secureItemsTask = AppDiagnostics.MeasureAsync(
            "Load secure items",
            () => repository.GetSecureItemsAsync());
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
            customFieldsTask,
            attachmentsTask,
            secureItemsTask,
            categoriesTask,
            quickAccessRecordsTask,
            databasesTask);

        var customFields = await customFieldsTask;
        var attachments = await attachmentsTask;
        var secureItems = await secureItemsTask;
        var noteItems = secureItems
            .Where(item => item.ItemType == VaultItemType.Note)
            .ToArray();
        var walletItems = secureItems
            .Where(item => item.ItemType is VaultItemType.BankCard or VaultItemType.Document)
            .ToArray();
        var storedTotps = secureItems
            .Where(item => item.ItemType == VaultItemType.Totp)
            .ToArray();

        var categories = await categoriesTask;
        var quickAccessRecords = (await quickAccessRecordsTask)
            .Where(record => record.OpenCount > 0 && record.PasswordId > 0)
            .ToDictionary(record => record.PasswordId);
        var databases = await databasesTask;

        return new VaultLoadSnapshot(
            activePasswords,
            archivedPasswords,
            deletedPasswords,
            customFields,
            attachments,
            noteItems,
            walletItems,
            storedTotps,
            categories,
            quickAccessRecords,
            databases);
    }
}
