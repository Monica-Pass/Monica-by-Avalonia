using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

internal static class TotpPresentationState
{
    public static void Refresh(SecureItem item, ITotpService totpService)
    {
        var data = TotpDataResolver.ParseStoredItemData(item.ItemData, item.Title, item.Notes);
        if (data is null || string.IsNullOrWhiteSpace(data.Secret))
        {
            item.TotpCode = "------";
            item.TotpTimeRemaining = "";
            item.TotpProgress = 0;
            return;
        }

        item.TotpCode = totpService.GenerateCode(data.Secret, data.Period, data.Digits, data.OtpType, data.Counter);
        if (string.Equals(data.OtpType, "HOTP", StringComparison.OrdinalIgnoreCase))
        {
            item.TotpTimeRemaining = "";
            item.TotpProgress = 100;
            return;
        }

        item.TotpTimeRemaining = $"{totpService.GetRemainingSeconds(data.Period)}s";
        item.TotpProgress = totpService.GetProgress(data.Period);
    }

    public static SecureItem BuildVirtualItem(PasswordEntry entry)
    {
        var data = TotpDataResolver.FromAuthenticatorKey(entry.AuthenticatorKey, entry.Title, entry.Username);
        return new SecureItem
        {
            Id = -entry.Id,
            ItemType = VaultItemType.Totp,
            Title = entry.Title,
            Notes = string.IsNullOrWhiteSpace(data?.AccountName) ? entry.Username : data.AccountName,
            ItemData = data is null ? "{}" : TotpDataResolver.ToItemData(data),
            BoundPasswordId = entry.Id,
            CategoryId = entry.CategoryId,
            IsFavorite = entry.IsFavorite,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt
        };
    }
}
