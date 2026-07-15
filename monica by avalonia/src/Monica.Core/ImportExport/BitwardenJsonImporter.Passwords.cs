using System.Text.Json;
using Monica.Core.Models;

namespace Monica.Core.ImportExport;

public sealed partial class BitwardenJsonImporter
{
    private PasswordMapping MapPassword(JsonElement item, string sourceId, long sourceModelId, int type)
    {
        var metadata = ReadMetadata(item, sourceId, type);
        var fields = MapPasswordFields(item, sourceModelId, out var specialFields);
        var history = MapPasswordHistory(item, sourceModelId);
        var entry = type == 5
            ? MapSshKey(item, metadata, sourceModelId)
            : MapLogin(item, metadata, sourceModelId, specialFields);
        AddAttachmentMetadataFields(item, sourceModelId, fields);
        return new PasswordMapping(entry, fields, history);
    }

    private PasswordEntry MapLogin(
        JsonElement item,
        SourceMetadata metadata,
        long sourceModelId,
        IReadOnlyDictionary<string, string> specialFields)
    {
        var login = GetObject(item, "login");
        var websites = new List<string>();
        var appPackageName = specialFields.GetValueOrDefault("appPackageName", "");
        if (login is { } loginValue)
        {
            var uris = GetArray(loginValue, "uris", _limits.MaximumUrisPerLogin);
            if (uris is { } uriArray)
            {
                foreach (var uriElement in uriArray.EnumerateArray())
                {
                    if (uriElement.ValueKind != JsonValueKind.Object)
                    {
                        throw InvalidExport();
                    }

                    var uri = GetString(uriElement, "uri").Trim();
                    if (uri.StartsWith("androidapp://", StringComparison.OrdinalIgnoreCase))
                    {
                        appPackageName = uri["androidapp://".Length..].Trim();
                    }
                    else if (!string.IsNullOrWhiteSpace(uri) && !websites.Contains(uri, StringComparer.OrdinalIgnoreCase))
                    {
                        websites.Add(uri);
                    }
                }
            }
        }

        var isSshCompatibilityEntry = string.Equals(
            specialFields.GetValueOrDefault("monica_login_type", ""),
            "SSH_KEY",
            StringComparison.OrdinalIgnoreCase);
        return new PasswordEntry
        {
            Id = sourceModelId,
            Title = ResolveTitle(metadata.Title, "Bitwarden login"),
            Website = string.Join(", ", websites),
            Username = login is { } data ? GetString(data, "username") : "",
            Password = login is { } passwordData ? GetString(passwordData, "password") : "",
            Notes = metadata.Notes,
            IsFavorite = metadata.IsFavorite,
            Email = specialFields.GetValueOrDefault("email", ""),
            Phone = specialFields.GetValueOrDefault("phone", ""),
            AppPackageName = appPackageName,
            AppName = specialFields.GetValueOrDefault("appName", ""),
            AuthenticatorKey = login is { } totpData ? GetString(totpData, "totp") : "",
            PasskeyBindings = ReadRawArray(login, "fido2Credentials", _limits.MaximumFieldsPerItem),
            LoginType = isSshCompatibilityEntry ? PasswordLoginType.SshKey : PasswordLoginType.Password,
            SshKeyData = isSshCompatibilityEntry ? BuildSshKeyData(specialFields) : "",
            CreatedAt = metadata.CreatedAt,
            UpdatedAt = metadata.UpdatedAt,
            IsDeleted = metadata.DeletedAt is not null,
            DeletedAt = metadata.DeletedAt,
            ReplicaGroupId = $"bitwarden-json:{metadata.SourceId}",
            BitwardenCipherId = metadata.SourceId,
            BitwardenFolderId = EmptyToNull(metadata.FolderId),
            BitwardenRevisionDate = EmptyToNull(metadata.RevisionDate),
            BitwardenCipherType = 1,
            BitwardenLocalModified = false
        };
    }

    private static PasswordEntry MapSshKey(JsonElement item, SourceMetadata metadata, long sourceModelId)
    {
        var sshKey = GetObject(item, "sshKey");
        var publicKey = sshKey is { } data ? GetString(data, "publicKey") : "";
        var privateKey = sshKey is { } privateData ? GetString(privateData, "privateKey") : "";
        var fingerprint = sshKey is { } fingerprintData ? GetString(fingerprintData, "keyFingerprint") : "";
        var sshData = JsonSerializer.Serialize(new
        {
            algorithm = InferSshAlgorithm(publicKey),
            publicKeyOpenSsh = publicKey,
            privateKeyOpenSsh = privateKey,
            fingerprintSha256 = fingerprint,
            format = "OPENSSH"
        });
        return new PasswordEntry
        {
            Id = sourceModelId,
            Title = ResolveTitle(metadata.Title, "Bitwarden SSH key"),
            Username = fingerprint,
            Notes = metadata.Notes,
            IsFavorite = metadata.IsFavorite,
            LoginType = PasswordLoginType.SshKey,
            SshKeyData = sshData,
            CreatedAt = metadata.CreatedAt,
            UpdatedAt = metadata.UpdatedAt,
            IsDeleted = metadata.DeletedAt is not null,
            DeletedAt = metadata.DeletedAt,
            ReplicaGroupId = $"bitwarden-json:{metadata.SourceId}",
            BitwardenCipherId = metadata.SourceId,
            BitwardenFolderId = EmptyToNull(metadata.FolderId),
            BitwardenRevisionDate = EmptyToNull(metadata.RevisionDate),
            BitwardenCipherType = 5,
            BitwardenLocalModified = false
        };
    }

    private List<CustomField> MapPasswordFields(
        JsonElement item,
        long sourceModelId,
        out IReadOnlyDictionary<string, string> specialFields)
    {
        var fieldsElement = GetArray(item, "fields", _limits.MaximumFieldsPerItem);
        var fields = new List<CustomField>();
        var special = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (fieldsElement is { } values)
        {
            foreach (var field in values.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object)
                {
                    throw InvalidExport();
                }

                var name = GetString(field, "name").Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = "Bitwarden field";
                }

                var value = GetString(field, "value");
                special[name] = value;
                if (IsMappedSpecialField(name))
                {
                    continue;
                }

                fields.Add(new CustomField
                {
                    EntryId = sourceModelId,
                    Title = name,
                    Value = value,
                    IsProtected = GetInt32(field, "type") == 1,
                    SortOrder = fields.Count
                });
            }
        }

        specialFields = special;
        return fields;
    }

    private IReadOnlyList<PasswordHistoryEntry> MapPasswordHistory(JsonElement item, long sourceModelId)
    {
        var historyElement = GetArray(item, "passwordHistory", _limits.MaximumHistoryEntriesPerItem);
        if (historyElement is null)
        {
            return [];
        }

        var history = new List<PasswordHistoryEntry>();
        foreach (var value in historyElement.Value.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw InvalidExport();
            }

            var password = GetString(value, "password");
            if (string.IsNullOrEmpty(password))
            {
                continue;
            }

            history.Add(new PasswordHistoryEntry
            {
                EntryId = sourceModelId,
                Password = password,
                LastUsedAt = GetDate(value, "lastUsedDate", DateTimeOffset.UnixEpoch)
            });
        }

        return history;
    }

    private void AddAttachmentMetadataFields(JsonElement item, long sourceModelId, ICollection<CustomField> fields)
    {
        var attachments = GetArray(item, "attachments", _limits.MaximumFieldsPerItem);
        if (attachments is null)
        {
            return;
        }

        foreach (var attachment in attachments.Value.EnumerateArray())
        {
            if (attachment.ValueKind != JsonValueKind.Object)
            {
                throw InvalidExport();
            }

            var fileName = GetString(attachment, "fileName").Trim();
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                fields.Add(new CustomField
                {
                    EntryId = sourceModelId,
                    Title = "Bitwarden attachment",
                    Value = fileName,
                    SortOrder = fields.Count
                });
            }
        }
    }

    private static string ReadRawArray(JsonElement? parent, string name, int maximum)
    {
        if (parent is null)
        {
            return "";
        }

        var value = GetArray(parent.Value, name, maximum);
        return value is null || value.Value.GetArrayLength() == 0 ? "" : value.Value.GetRawText();
    }

    private static bool IsMappedSpecialField(string name) =>
        name.Equals("email", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("phone", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("appPackageName", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("appName", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("monica_ssh_", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("monica_login_type", StringComparison.OrdinalIgnoreCase);

    private static string BuildSshKeyData(IReadOnlyDictionary<string, string> fields) =>
        JsonSerializer.Serialize(new
        {
            algorithm = fields.GetValueOrDefault("monica_ssh_algorithm", ""),
            publicKeyOpenSsh = fields.GetValueOrDefault("monica_ssh_public_key", ""),
            privateKeyOpenSsh = fields.GetValueOrDefault("monica_ssh_private_key", ""),
            fingerprintSha256 = fields.GetValueOrDefault("monica_ssh_fingerprint", ""),
            comment = fields.GetValueOrDefault("monica_ssh_comment", ""),
            format = fields.GetValueOrDefault("monica_ssh_format", "OPENSSH")
        });

    private static string InferSshAlgorithm(string publicKey) =>
        publicKey.StartsWith("ssh-rsa", StringComparison.OrdinalIgnoreCase) ? "RSA" :
        publicKey.StartsWith("ssh-ed25519", StringComparison.OrdinalIgnoreCase) ? "ED25519" : "";

    private sealed record PasswordMapping(
        PasswordEntry Entry,
        IReadOnlyList<CustomField> CustomFields,
        IReadOnlyList<PasswordHistoryEntry> History);
}
