using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Monica.Core.Models;

namespace Monica.Core.Bitwarden;

/// <summary>
/// Turns a local entry into the body Bitwarden's create/update cipher endpoint expects. The shape is
/// the deliberate inverse of <c>BitwardenCipherDecoder</c> in Monica.Platform: every field the decoder
/// reads back is the only field written out, so push then pull reproduces the local entry instead of
/// drifting. It lives here because the write-back producer runs in Monica.Data, which cannot reach
/// Platform, and everything this needs - the cipher-string crypto, the entity, the protocol exception -
/// is already Core. Anything that cannot round-trip loses the remote object on write, so it throws.
/// </summary>
public static partial class BitwardenCipherPayloadBuilder
{
    public const int MaximumPayloadUtf8Bytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Whether this entry has a shape Bitwarden can carry, asked before a local entry is promised to a
    /// vault so the count of uploads the user was told about is the count that can actually happen.
    /// </summary>
    public static bool CanEncode(PasswordEntry entry) =>
        entry is not null && FindUnsupportedChange(entry) is null;

    public static string BuildLoginCipher(
        PasswordEntry entry,
        BitwardenSymmetricKey key,
        IReadOnlyList<CustomField>? customFields = null,
        IReadOnlyList<PasswordHistoryEntry>? history = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        var refusal = FindUnsupportedChange(entry);
        if (refusal is not null)
        {
            throw new BitwardenProtocolException(refusal);
        }

        customFields ??= [];
        history ??= [];

        var payload = new CipherRequestDto
        {
            FolderId = string.IsNullOrWhiteSpace(entry.BitwardenFolderId) ? null : entry.BitwardenFolderId,
            Type = 1,
            Name = BitwardenCipherStringCrypto.EncryptString(entry.Title.Trim(), key),
            Notes = EncryptOptional(entry.Notes, key),
            Favorite = entry.IsFavorite,
            ArchivedDate = FormatDate(entry.IsArchived ? entry.ArchivedAt ?? entry.UpdatedAt : null),
            Login = new LoginRequestDto
            {
                Username = EncryptOptional(entry.Username, key),
                Password = EncryptOptional(entry.Password, key),
                Totp = EncryptOptional(entry.AuthenticatorKey, key),
                Uris = string.IsNullOrEmpty(entry.Website)
                    ? null
                    : [new UriRequestDto { Uri = EncryptOptional(entry.Website, key) }],
                Fido2Credentials = BuildPasskeys(entry.PasskeyBindings, key)
            },
            Fields = BuildFields(customFields, key),
            PasswordHistory = BuildHistory(history, key)
        };

        var json = JsonSerializer.Serialize(payload, PayloadOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumPayloadUtf8Bytes)
        {
            throw new BitwardenProtocolException("Bitwarden cipher payload exceeds the supported size.");
        }

        return json;
    }

    private static string? FindUnsupportedChange(PasswordEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.BitwardenCipherType != 1)
        {
            return "Monica can only write back Bitwarden login ciphers.";
        }

        if (entry.LoginType != PasswordLoginType.Password)
        {
            return "Monica cannot write back this login type to Bitwarden.";
        }

        if (entry.HasAttachments)
        {
            return "Monica cannot write back a Bitwarden cipher that has attachments.";
        }

        if (entry.IsDeleted)
        {
            return "Monica deletes Bitwarden ciphers through the trash endpoint, not the update payload.";
        }

        if (string.IsNullOrWhiteSpace(entry.Title))
        {
            return "A Bitwarden cipher requires a title.";
        }

        return null;
    }

    private static string? EncryptOptional(string value, BitwardenSymmetricKey key) =>
        string.IsNullOrEmpty(value) ? null : BitwardenCipherStringCrypto.EncryptString(value, key);

    private static List<Fido2CredentialRequestDto>? BuildPasskeys(string bindings, BitwardenSymmetricKey key)
    {
        if (string.IsNullOrWhiteSpace(bindings))
        {
            return null;
        }

        var credentials = new List<Fido2CredentialRequestDto>();
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(bindings);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new BitwardenProtocolException("Monica's stored passkey bindings are malformed.");
            }

            foreach (var credential in document.RootElement.EnumerateArray())
            {
                if (credential.ValueKind != JsonValueKind.Object)
                {
                    throw new BitwardenProtocolException("Monica's stored passkey bindings are malformed.");
                }

                credentials.Add(new Fido2CredentialRequestDto
                {
                    CredentialId = EncryptRequired(ReadPasskeyField(credential, "credentialId"), key),
                    KeyType = EncryptRequired(ReadPasskeyField(credential, "keyType"), key),
                    KeyAlgorithm = EncryptRequired(ReadPasskeyField(credential, "keyAlgorithm"), key),
                    KeyCurve = EncryptRequired(ReadPasskeyField(credential, "keyCurve"), key),
                    KeyValue = EncryptRequired(ReadPasskeyField(credential, "keyValue"), key),
                    RpId = EncryptRequired(ReadPasskeyField(credential, "rpId"), key),
                    Counter = EncryptOptionalOrNull(ReadPasskeyField(credential, "counter"), key),
                    UserHandle = EncryptOptionalOrNull(ReadPasskeyField(credential, "userHandle"), key),
                    UserName = EncryptOptionalOrNull(ReadPasskeyField(credential, "userName"), key),
                    UserDisplayName = EncryptOptionalOrNull(ReadPasskeyField(credential, "userDisplayName"), key),
                    RpName = EncryptOptionalOrNull(ReadPasskeyField(credential, "rpName"), key),
                    Discoverable = EncryptOptionalOrNull(ReadPasskeyField(credential, "discoverable"), key),
                    CreationDate = EncryptOptionalOrNull(ReadPasskeyField(credential, "creationDate"), key)
                });
            }
        }
        catch (JsonException exception)
        {
            throw new BitwardenProtocolException("Monica's stored passkey bindings are malformed.", exception);
        }
        finally
        {
            document?.Dispose();
        }

        return credentials.Count == 0 ? null : credentials;
    }

    private static string ReadPasskeyField(JsonElement credential, string name)
    {
        foreach (var property in credential.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString() ?? "";
                }

                if (property.Value.ValueKind == JsonValueKind.True)
                {
                    return "true";
                }

                if (property.Value.ValueKind == JsonValueKind.False)
                {
                    return "false";
                }

                throw new BitwardenProtocolException("Monica's stored passkey bindings are malformed.");
            }
        }

        return "";
    }

    private static string EncryptRequired(string value, BitwardenSymmetricKey key)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new BitwardenProtocolException(
                "A Bitwarden passkey is missing a field Monica would have to erase.");
        }

        return BitwardenCipherStringCrypto.EncryptString(value, key);
    }

    private static string? EncryptOptionalOrNull(string value, BitwardenSymmetricKey key) =>
        string.IsNullOrEmpty(value) ? null : BitwardenCipherStringCrypto.EncryptString(value, key);

    private static List<FieldRequestDto>? BuildFields(
        IReadOnlyList<CustomField> customFields,
        BitwardenSymmetricKey key)
    {
        if (customFields.Count == 0)
        {
            return null;
        }

        var ordered = customFields
            .OrderBy(field => field.SortOrder)
            .ThenBy(field => field.Title, StringComparer.Ordinal)
            .ToList();
        var fields = new List<FieldRequestDto>(ordered.Count);
        foreach (var field in ordered)
        {
            if (string.IsNullOrWhiteSpace(field.Title))
            {
                throw new BitwardenProtocolException(
                    "A Bitwarden custom field needs a name before this entry can be written back.");
            }

            fields.Add(new FieldRequestDto
            {
                Name = BitwardenCipherStringCrypto.EncryptString(field.Title.Trim(), key),
                Value = EncryptOptional(field.Value, key),
                Type = field.IsProtected ? 1 : 0
            });
        }

        return fields;
    }

    private static List<PasswordHistoryRequestDto>? BuildHistory(
        IReadOnlyList<PasswordHistoryEntry> history,
        BitwardenSymmetricKey key)
    {
        if (history.Count == 0)
        {
            return null;
        }

        return history
            .OrderBy(item => item.LastUsedAt)
            .Select(item => new PasswordHistoryRequestDto
            {
                Password = EncryptOptional(item.Password, key),
                LastUsedDate = FormatDate(item.LastUsedAt) ?? ""
            })
            .ToList();
    }

    private static string? FormatDate(DateTimeOffset? value) => value?.ToUniversalTime()
        .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}

internal sealed record CipherRequestDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FolderId { get; init; }

    public int Type { get; init; }

    public required string Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; init; }

    public bool Favorite { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArchivedDate { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LoginRequestDto? Login { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecureNoteRequestDto? SecureNote { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardRequestDto? Card { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IdentityRequestDto? Identity { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FieldRequestDto>? Fields { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PasswordHistoryRequestDto>? PasswordHistory { get; init; }
}

internal sealed record LoginRequestDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Username { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Password { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Totp { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<UriRequestDto>? Uris { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Fido2CredentialRequestDto>? Fido2Credentials { get; init; }
}

internal sealed record UriRequestDto
{
    public required string? Uri { get; init; }

    public int? Match { get; init; }
}

internal sealed record Fido2CredentialRequestDto
{
    public required string CredentialId { get; init; }

    public required string KeyType { get; init; }

    public required string KeyAlgorithm { get; init; }

    public required string KeyCurve { get; init; }

    public required string KeyValue { get; init; }

    public required string RpId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Counter { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserHandle { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserDisplayName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RpName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Discoverable { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreationDate { get; init; }
}

internal sealed record FieldRequestDto
{
    public required string Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; init; }

    public int Type { get; init; }

    public int? LinkedId { get; init; }
}

internal sealed record PasswordHistoryRequestDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Password { get; init; }

    public required string LastUsedDate { get; init; }
}
