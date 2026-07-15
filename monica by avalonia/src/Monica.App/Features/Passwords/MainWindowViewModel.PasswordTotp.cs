using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void RefreshPasswordTotpDisplay(PasswordEntry entry)
        => PasswordPresentationState.RefreshTotp(entry, _totpService);

    private async Task SynchronizeBoundTotpAsync(PasswordEntry entry)
    {
        var existing = await _repository.GetSecureItemsByBoundPasswordIdAsync(entry.Id, includeDeleted: true);
        var active = existing.Where(item => !item.IsDeleted).OrderBy(item => item.Id).ToArray();
        var data = TotpDataResolver.FromAuthenticatorKey(entry.AuthenticatorKey, entry.Title, entry.Username);
        if (data is null || string.IsNullOrWhiteSpace(data.Secret))
        {
            foreach (var item in active)
            {
                await _repository.SoftDeleteSecureItemAsync(item.Id);
            }

            return;
        }

        var primary = active.FirstOrDefault() ?? existing.OrderBy(item => item.Id).FirstOrDefault() ?? new SecureItem
        {
            ItemType = VaultItemType.Totp,
            CreatedAt = DateTimeOffset.UtcNow
        };

        primary.ItemType = VaultItemType.Totp;
        primary.Title = entry.Title;
        primary.Notes = string.IsNullOrWhiteSpace(data.AccountName) ? entry.Username : data.AccountName;
        primary.ItemData = TotpDataResolver.ToItemData(data);
        primary.BoundPasswordId = entry.Id;
        primary.CategoryId = entry.CategoryId;
        primary.KeepassDatabaseId = entry.KeepassDatabaseId;
        primary.KeepassGroupPath = entry.KeepassGroupPath;
        primary.KeepassEntryUuid = entry.KeepassEntryUuid;
        primary.KeepassGroupUuid = entry.KeepassGroupUuid;
        primary.MdbxDatabaseId = entry.MdbxDatabaseId;
        primary.MdbxFolderId = entry.MdbxFolderId;
        primary.BitwardenVaultId = entry.BitwardenVaultId;
        primary.BitwardenFolderId = entry.BitwardenFolderId;
        primary.BitwardenCipherId = entry.BitwardenCipherId;
        primary.BitwardenRevisionDate = entry.BitwardenRevisionDate;
        primary.BitwardenLocalModified = entry.BitwardenLocalModified;
        primary.IsFavorite = entry.IsFavorite;
        primary.IsDeleted = false;
        primary.DeletedAt = null;
        primary.SyncStatus = entry.BitwardenVaultId is null ? SyncStatus.None : SyncStatus.Pending;
        await _repository.SaveSecureItemAsync(primary);

        foreach (var duplicate in active.Skip(1))
        {
            await _repository.SoftDeleteSecureItemAsync(duplicate.Id);
        }
    }
    private static SecureItem BuildVirtualTotpItem(PasswordEntry entry)
        => TotpPresentationState.BuildVirtualItem(entry);

    private void RefreshBoundTotpPresentation(IEnumerable<PasswordEntry> changedPasswords)
    {
        var selectedBoundPasswordId = SelectedTotpItem?.BoundPasswordId;
        var changedById = changedPasswords
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.Last());
        if (changedById.Count == 0)
        {
            return;
        }

        var presentationChanged = false;
        for (var index = TotpItems.Count - 1; index >= 0; index--)
        {
            if (TotpItems[index].BoundPasswordId is { } boundPasswordId && changedById.ContainsKey(boundPasswordId))
            {
                TotpItems.RemoveAt(index);
                presentationChanged = true;
            }
        }

        var activePasswordIds = Passwords.Select(item => item.Id).ToHashSet();
        foreach (var password in changedById.Values.Reverse())
        {
            if (!activePasswordIds.Contains(password.Id) || !password.HasAuthenticator)
            {
                continue;
            }

            var item = BuildVirtualTotpItem(password);
            TrackTotpSelection(item);
            RefreshTotpDisplay(item);
            TotpItems.Insert(0, item);
            presentationChanged = true;
        }

        if (!presentationChanged)
        {
            return;
        }

        if (selectedBoundPasswordId is { } passwordId)
        {
            SelectedTotpItem = TotpItems.FirstOrDefault(item => item.BoundPasswordId == passwordId)
                ?? TotpItems.FirstOrDefault();
        }
        else if (SelectedTotpItem is not null && !TotpItems.Contains(SelectedTotpItem))
        {
            SelectedTotpItem = TotpItems.FirstOrDefault();
        }

        RaiseTotpCountState();
    }
}
