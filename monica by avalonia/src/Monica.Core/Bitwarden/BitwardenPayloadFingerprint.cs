using System.Security.Cryptography;
using System.Text.Json;
using Monica.Core.Models;

namespace Monica.Core.Bitwarden;

public static class BitwardenPayloadFingerprint
{
    public static string ForPassword(
        PasswordEntry entry,
        IReadOnlyList<CustomField> customFields,
        IReadOnlyList<PasswordHistoryEntry> history)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Hash(new
        {
            kind = "password",
            entry.Title,
            entry.Website,
            entry.Username,
            entry.Password,
            entry.Notes,
            entry.IsFavorite,
            entry.AppPackageName,
            entry.AppName,
            entry.Email,
            entry.Phone,
            entry.AddressLine,
            entry.City,
            entry.State,
            entry.ZipCode,
            entry.Country,
            entry.CreditCardNumber,
            entry.CreditCardHolder,
            entry.CreditCardExpiry,
            entry.CreditCardCvv,
            entry.AuthenticatorKey,
            entry.PasskeyBindings,
            entry.SshKeyData,
            loginType = entry.LoginType.ToString(),
            entry.SsoProvider,
            entry.WifiMetadata,
            entry.IsDeleted,
            entry.BitwardenFolderId,
            entry.BitwardenCipherType,
            customFields = customFields
                .OrderBy(field => field.SortOrder)
                .ThenBy(field => field.Title, StringComparer.Ordinal)
                .Select(field => new { field.Title, field.Value, field.IsProtected, field.SortOrder }),
            history = history
                .OrderBy(item => item.LastUsedAt)
                .Select(item => new { item.Password, item.LastUsedAt })
        });
    }

    public static string ForSecureItem(SecureItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Hash(new
        {
            kind = "secure-item",
            itemType = item.ItemType.ToString(),
            item.Title,
            item.Notes,
            item.IsFavorite,
            item.ItemData,
            item.ImagePaths,
            item.IsDeleted,
            item.BitwardenFolderId
        });
    }

    private static string Hash<T>(T payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
