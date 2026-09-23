using System.Text.Json;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Data.Repositories;

namespace Monica.Data.Bitwarden;

/// <summary>
/// One row of the conflict list. The payload carries plaintext field values, so only a title is read
/// out of it; nothing here can render a password.
/// </summary>
public sealed record BitwardenConflictSummary(
    long BackupId,
    string CipherId,
    bool IsPassword,
    string Title,
    string Reason,
    DateTimeOffset CreatedAt);

public interface IBitwardenConflictRestoreService
{
    Task<IReadOnlyList<BitwardenConflictSummary>> GetSummariesAsync(
        long vaultId,
        CancellationToken cancellationToken = default);

    Task RestoreAsync(long vaultId, long backupId, CancellationToken cancellationToken = default);

    Task DiscardAsync(long vaultId, long backupId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Gives back the local content the merge engine overwrote. A backup exists only because a pull was
/// about to destroy this device's version, so restoring is the only way to recover it - and until the
/// restored content is uploaded the remote keeps winning. Writing it back leaves the entry differing
/// from the sync baseline, which is what makes the next synchronization offer it to the server instead
/// of losing it a second time.
/// </summary>
public sealed class BitwardenConflictRestoreService(
    IMonicaRepository repository,
    IBitwardenConflictBackupStore conflictStore) : IBitwardenConflictRestoreService
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<BitwardenConflictSummary>> GetSummariesAsync(
        long vaultId,
        CancellationToken cancellationToken = default)
    {
        var backups = await conflictStore.GetUnresolvedAsync(vaultId, cancellationToken);
        return backups
            .Select(backup =>
            {
                var stored = ReadBackup(backup.PayloadJson);
                return new BitwardenConflictSummary(
                    backup.Id,
                    backup.CipherId,
                    stored.Password is not null,
                    stored.Title,
                    backup.Reason,
                    backup.CreatedAt);
            })
            .ToList();
    }

    public async Task RestoreAsync(long vaultId, long backupId, CancellationToken cancellationToken = default)
    {
        var backup = await GetUnresolvedAsync(vaultId, backupId, cancellationToken);
        var stored = ReadBackup(backup.PayloadJson);
        var categories = (await repository.GetCategoriesAsync(cancellationToken))
            .Where(category => category.BitwardenVaultId == vaultId)
            .Select(category => category.Id)
            .ToHashSet();

        if (stored.Password is { } entry)
        {
            var existing = (await repository.GetPasswordsAsync(true, true, cancellationToken))
                .FirstOrDefault(candidate => candidate.Id == backup.LocalItemId);
            // The revision is a pointer into the server's history, not part of the content, so keep the
            // one this device last confirmed rather than the stale one inside the backup.
            entry.BitwardenRevisionDate = existing?.BitwardenRevisionDate ?? entry.BitwardenRevisionDate;
            entry.Id = existing?.Id ?? 0;
            entry.BitwardenVaultId = vaultId;
            entry.BitwardenCipherId = backup.CipherId;
            entry.BitwardenLocalModified = true;
            entry.CategoryId = categories.Contains(entry.CategoryId ?? 0) ? entry.CategoryId : null;
            await repository.SavePasswordAsync(entry, cancellationToken);
            await repository.ReplaceCustomFieldsAsync(entry.Id, stored.CustomFields, cancellationToken);
            await repository.ClearPasswordHistoryAsync(entry.Id, cancellationToken);
            foreach (var history in stored.History)
            {
                await repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
                {
                    EntryId = entry.Id,
                    Password = history.Password,
                    LastUsedAt = history.LastUsedAt
                }, cancellationToken);
            }
        }
        else
        {
            var item = stored.SecureItem!;
            var existing = (await repository.GetSecureItemsAsync(null, true, cancellationToken))
                .FirstOrDefault(candidate => candidate.Id == backup.LocalItemId);
            item.BitwardenRevisionDate = existing?.BitwardenRevisionDate ?? item.BitwardenRevisionDate;
            item.Id = existing?.Id ?? 0;
            item.BitwardenVaultId = vaultId;
            item.BitwardenCipherId = backup.CipherId;
            item.BitwardenLocalModified = true;
            item.CategoryId = categories.Contains(item.CategoryId ?? 0) ? item.CategoryId : null;
            await repository.SaveSecureItemAsync(item, cancellationToken);
        }

        await conflictStore.ResolveAsync(backupId, cancellationToken);
    }

    public async Task DiscardAsync(long vaultId, long backupId, CancellationToken cancellationToken = default)
    {
        await GetUnresolvedAsync(vaultId, backupId, cancellationToken);
        await conflictStore.ResolveAsync(backupId, cancellationToken);
    }

    private async Task<BitwardenConflictBackup> GetUnresolvedAsync(
        long vaultId,
        long backupId,
        CancellationToken cancellationToken) =>
        (await conflictStore.GetUnresolvedAsync(vaultId, cancellationToken))
            .FirstOrDefault(item => item.Id == backupId) ??
        throw new KeyNotFoundException("The Bitwarden conflict backup is not unresolved for this vault.");

    private static StoredBackup ReadBackup(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        // Keyed on the payload, not the stored item_kind, because a row whose two disagree would
        // otherwise restore the wrong entity type onto a local identity.
        if (root.TryGetProperty("password", out var password))
        {
            return new StoredBackup(
                Deserialize<PasswordEntry>(password),
                null,
                ReadList<CustomField>(root, "customFields"),
                ReadList<PasswordHistoryEntry>(root, "passwordHistory"));
        }

        if (root.TryGetProperty("secureItem", out var secureItem))
        {
            return new StoredBackup(null, Deserialize<SecureItem>(secureItem), [], []);
        }

        throw new BitwardenProtocolException("The stored Bitwarden conflict backup is not a readable entry.");
    }

    private static IReadOnlyList<T> ReadList<T>(JsonElement root, string property) =>
        root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Array
            ? element.Deserialize<IReadOnlyList<T>>(PayloadOptions) ?? []
            : [];

    private static T Deserialize<T>(JsonElement element) =>
        element.Deserialize<T>(PayloadOptions) ??
        throw new BitwardenProtocolException("The stored Bitwarden conflict backup is not a readable entry.");

    private sealed record StoredBackup(
        PasswordEntry? Password,
        SecureItem? SecureItem,
        IReadOnlyList<CustomField> CustomFields,
        IReadOnlyList<PasswordHistoryEntry> History)
    {
        public string Title => Password?.Title ?? SecureItem!.Title;
    }
}
