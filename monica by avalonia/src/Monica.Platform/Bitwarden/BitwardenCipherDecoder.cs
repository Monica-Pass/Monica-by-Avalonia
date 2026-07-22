using System.Text.Json;
using Monica.Core.Bitwarden;
using Monica.Core.Models;

namespace Monica.Platform.Bitwarden;

internal sealed partial class BitwardenCipherDecoder(BitwardenSymmetricKey vaultKey)
{
    private const int MaximumFolders = 10_000;
    private const int MaximumCiphers = 100_000;
    private const int MaximumFieldsPerCipher = 1_000;
    private const int MaximumHistoryPerCipher = 1_000;

    public BitwardenRemoteSyncResult Decode(VaultSyncDto dto, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(dto);
        EnsureCount(dto.Folders.Count, MaximumFolders, "folders");
        EnsureCount(dto.Ciphers.Count, MaximumCiphers, "ciphers");
        var folders = dto.Folders.Select(DecodeFolder).ToArray();
        var metadata = new List<BitwardenRemoteCipherMetadata>(dto.Ciphers.Count);
        var decoded = new List<BitwardenDecodedCipher>(dto.Ciphers.Count);
        foreach (var cipher in dto.Ciphers)
        {
            var result = DecodeCipher(cipher);
            metadata.Add(result.Metadata);
            if (!result.Metadata.IsDeleted)
            {
                decoded.Add(result);
            }
        }

        var revision = metadata
            .Select(item => item.RevisionDate)
            .Concat(dto.Folders.Select(folder => folder.RevisionDate ?? ""))
            .Where(value => DateTimeOffset.TryParse(value, out _))
            .OrderByDescending(value => DateTimeOffset.Parse(value))
            .FirstOrDefault();
        return new BitwardenRemoteSyncResult(
            new BitwardenPullSnapshot(folders, metadata, revision, true, receivedAt),
            decoded,
            EmptyToNull(dto.Profile.Id),
            EmptyToNull(dto.Profile.Name));
    }

    private BitwardenRemoteFolder DecodeFolder(VaultFolderDto folder)
    {
        var id = RequiredIdentity(folder.Id, "folder");
        var name = DecryptRequired(folder.Name, vaultKey, "folder name").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BitwardenProtocolException($"Bitwarden folder {id} has an empty name.");
        }

        return new BitwardenRemoteFolder(id, name, null);
    }

    private BitwardenDecodedCipher DecodeCipher(VaultCipherDto cipher)
    {
        var id = RequiredIdentity(cipher.Id, "cipher");
        var revision = RequiredRevision(cipher.RevisionDate, id);
        var isDeleted = !string.IsNullOrWhiteSpace(cipher.DeletedDate);
        var updatedAt = DateTimeOffset.Parse(revision);
        if (isDeleted)
        {
            var deletedMetadata = new BitwardenRemoteCipherMetadata(
                id,
                EmptyToNull(cipher.FolderId),
                revision,
                cipher.Type,
                true,
                $"deleted:{revision}",
                updatedAt);
            return new BitwardenDecodedCipher(deletedMetadata, null, null, [], []);
        }

        using var itemKey = ResolveItemKey(cipher);
        var key = itemKey ?? vaultKey;
        BitwardenDecodedCipher decoded = cipher.Type switch
        {
            1 => DecodeLogin(cipher, id, revision, updatedAt, key),
            2 => DecodeSecureNote(cipher, id, revision, updatedAt, key),
            3 => DecodeCard(cipher, id, revision, updatedAt, key),
            4 => DecodeIdentity(cipher, id, revision, updatedAt, key),
            5 => DecodeSshKey(cipher, id, revision, updatedAt, key),
            _ => throw new BitwardenProtocolException($"Unsupported Bitwarden cipher type {cipher.Type}.")
        };
        return decoded;
    }

    private BitwardenDecodedCipher DecodeLogin(
        VaultCipherDto cipher,
        string id,
        string revision,
        DateTimeOffset updatedAt,
        BitwardenSymmetricKey key)
    {
        var login = cipher.Login ?? new VaultLoginDto();
        EnsureCount(login.Uris?.Count ?? 0, MaximumFieldsPerCipher, "login URIs");
        EnsureCount(login.Fido2Credentials?.Count ?? 0, MaximumFieldsPerCipher, "passkeys");
        var fields = DecodeFields(cipher.Fields, key);
        var history = DecodeHistory(cipher.PasswordHistory, key);
        var entry = CreatePasswordBase(cipher, key);
        entry.Website = DecryptOptional(login.Uris?.FirstOrDefault()?.Uri, key);
        entry.Username = DecryptOptional(login.Username, key);
        entry.Password = DecryptOptional(login.Password, key);
        entry.AuthenticatorKey = DecryptOptional(login.Totp, key);
        entry.PasskeyBindings = DecodePasskeys(login.Fido2Credentials, key);
        return BuildPasswordResult(cipher, id, revision, updatedAt, entry, fields, history);
    }

    private BitwardenDecodedCipher DecodeSshKey(
        VaultCipherDto cipher,
        string id,
        string revision,
        DateTimeOffset updatedAt,
        BitwardenSymmetricKey key)
    {
        var ssh = cipher.SshKey ?? new VaultSshKeyDto();
        var entry = CreatePasswordBase(cipher, key);
        entry.LoginType = PasswordLoginType.SshKey;
        entry.SshKeyData = JsonSerializer.Serialize(new
        {
            privateKey = DecryptOptional(ssh.PrivateKey, key),
            publicKey = DecryptOptional(ssh.PublicKey, key),
            fingerprint = DecryptOptional(ssh.KeyFingerprint, key)
        }, BitwardenHttpContent.JsonOptions);
        return BuildPasswordResult(
            cipher,
            id,
            revision,
            updatedAt,
            entry,
            DecodeFields(cipher.Fields, key),
            []);
    }

    private PasswordEntry CreatePasswordBase(VaultCipherDto cipher, BitwardenSymmetricKey key) => new()
    {
        Title = ResolveTitle(DecryptRequired(cipher.Name, key, "cipher name"), "Bitwarden login"),
        Notes = DecryptOptional(cipher.Notes, key),
        IsFavorite = cipher.Favorite,
        IsArchived = !string.IsNullOrWhiteSpace(cipher.ArchivedDate),
        ArchivedAt = ParseOptionalDate(cipher.ArchivedDate),
        CreatedAt = ParseOptionalDate(cipher.CreationDate) ?? DateTimeOffset.UtcNow
    };

    private BitwardenDecodedCipher BuildPasswordResult(
        VaultCipherDto source,
        string id,
        string revision,
        DateTimeOffset updatedAt,
        PasswordEntry entry,
        IReadOnlyList<CustomField> fields,
        IReadOnlyList<PasswordHistoryEntry> history)
    {
        entry.UpdatedAt = updatedAt;
        entry.BitwardenFolderId = EmptyToNull(source.FolderId);
        entry.BitwardenCipherType = source.Type;
        var metadata = new BitwardenRemoteCipherMetadata(
            id,
            entry.BitwardenFolderId,
            revision,
            source.Type,
            false,
            BitwardenPayloadFingerprint.ForPassword(entry, fields, history),
            updatedAt);
        return new BitwardenDecodedCipher(metadata, entry, null, fields, history);
    }

    private BitwardenSymmetricKey? ResolveItemKey(VaultCipherDto cipher) =>
        string.IsNullOrWhiteSpace(cipher.Key)
            ? null
            : BitwardenCipherStringCrypto.DecryptSymmetricKey(cipher.Key, vaultKey);

    private static string DecryptRequired(
        string? value,
        BitwardenSymmetricKey key,
        string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new BitwardenProtocolException($"Bitwarden {fieldName} is missing.")
            : BitwardenCipherStringCrypto.DecryptToString(value, key);

    private static string DecryptOptional(string? value, BitwardenSymmetricKey key) =>
        string.IsNullOrWhiteSpace(value) ? "" : BitwardenCipherStringCrypto.DecryptToString(value, key);

    private static string RequiredIdentity(string? value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
        {
            throw new BitwardenProtocolException($"Bitwarden {kind} identity is invalid.");
        }

        return value;
    }

    private static string RequiredRevision(string? value, string id)
    {
        if (string.IsNullOrWhiteSpace(value) || !DateTimeOffset.TryParse(value, out _))
        {
            throw new BitwardenProtocolException($"Bitwarden cipher {id} has an invalid revision.");
        }

        return value;
    }

    private static void EnsureCount(int count, int maximum, string name)
    {
        if (count > maximum)
        {
            throw new BitwardenProtocolException($"Bitwarden {name} exceed the supported count.");
        }
    }

    private static string ResolveTitle(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static DateTimeOffset? ParseOptionalDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
