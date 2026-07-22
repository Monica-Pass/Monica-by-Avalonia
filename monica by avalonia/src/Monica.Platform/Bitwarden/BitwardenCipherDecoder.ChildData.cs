using System.Text.Json;
using Monica.Core.Bitwarden;
using Monica.Core.Models;

namespace Monica.Platform.Bitwarden;

internal sealed partial class BitwardenCipherDecoder
{
    private IReadOnlyList<CustomField> DecodeFields(
        IReadOnlyList<VaultFieldDto>? source,
        BitwardenSymmetricKey key)
    {
        EnsureCount(source?.Count ?? 0, MaximumFieldsPerCipher, "custom fields");
        if (source is null)
        {
            return [];
        }

        return source.Select((field, index) => new CustomField
        {
            Title = ResolveTitle(DecryptOptional(field.Name, key), $"Bitwarden field {index + 1}"),
            Value = DecryptOptional(field.Value, key),
            IsProtected = field.Type == 1,
            SortOrder = index
        }).ToArray();
    }

    private IReadOnlyList<PasswordHistoryEntry> DecodeHistory(
        IReadOnlyList<VaultPasswordHistoryDto>? source,
        BitwardenSymmetricKey key)
    {
        EnsureCount(source?.Count ?? 0, MaximumHistoryPerCipher, "password history entries");
        if (source is null)
        {
            return [];
        }

        return source.Select(history => new PasswordHistoryEntry
        {
            Password = DecryptOptional(history.Password, key),
            LastUsedAt = DateTimeOffset.TryParse(history.LastUsedDate, out var parsed)
                ? parsed
                : throw new BitwardenProtocolException("Bitwarden password history contains an invalid date.")
        }).ToArray();
    }

    private static string DecodePasskeys(
        IReadOnlyList<VaultFido2CredentialDto>? source,
        BitwardenSymmetricKey key)
    {
        if (source is null || source.Count == 0)
        {
            return "";
        }

        var decoded = source.Select(item => new
        {
            credentialId = DecryptOptional(item.CredentialId, key),
            keyType = DecryptOptional(item.KeyType, key),
            keyAlgorithm = DecryptOptional(item.KeyAlgorithm, key),
            keyCurve = DecryptOptional(item.KeyCurve, key),
            keyValue = DecryptOptional(item.KeyValue, key),
            rpId = DecryptOptional(item.RpId, key),
            rpName = DecryptOptional(item.RpName, key),
            counter = DecryptOptional(item.Counter, key),
            userHandle = DecryptOptional(item.UserHandle, key),
            userName = DecryptOptional(item.UserName, key),
            userDisplayName = DecryptOptional(item.UserDisplayName, key),
            discoverable = DecryptOptional(item.Discoverable, key),
            creationDate = DecryptOptional(item.CreationDate, key)
        });
        return JsonSerializer.Serialize(decoded, BitwardenHttpContent.JsonOptions);
    }
}
