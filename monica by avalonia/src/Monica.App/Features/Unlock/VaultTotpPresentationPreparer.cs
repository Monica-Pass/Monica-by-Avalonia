using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

internal static class VaultTotpPresentationPreparer
{
    public static IReadOnlyList<SecureItem> Prepare(
        VaultLoadSnapshot snapshot,
        ITotpService totpService,
        CancellationToken cancellationToken)
    {
        var activePasswordIds = snapshot.ActivePasswords.Select(item => item.Id).ToHashSet();
        var seenVirtualPasswordIds = new HashSet<long>();
        var preparedItems = new List<SecureItem>();

        foreach (var item in snapshot.StoredTotps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.BoundPasswordId is { } boundPasswordId && !activePasswordIds.Contains(boundPasswordId))
            {
                continue;
            }

            item.IsSelected = false;
            TotpPresentationState.Refresh(item, totpService);
            preparedItems.Add(item);
            if (item.BoundPasswordId is { } passwordId)
            {
                seenVirtualPasswordIds.Add(passwordId);
            }
        }

        foreach (var password in snapshot.ActivePasswords.Where(
                     item => item.HasAuthenticator && !seenVirtualPasswordIds.Contains(item.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var virtualItem = TotpPresentationState.BuildVirtualItem(password);
            TotpPresentationState.Refresh(virtualItem, totpService);
            preparedItems.Add(virtualItem);
        }

        return preparedItems;
    }
}
