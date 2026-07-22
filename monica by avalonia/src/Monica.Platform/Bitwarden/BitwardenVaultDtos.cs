using System.Text.Json.Serialization;

namespace Monica.Platform.Bitwarden;

internal sealed record VaultSyncDto
{
    public VaultProfileDto Profile { get; init; } = new();
    public List<VaultFolderDto> Folders { get; init; } = [];
    public List<VaultCipherDto> Ciphers { get; init; } = [];
}

internal sealed record VaultProfileDto
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Email { get; init; }
}

internal sealed record VaultFolderDto
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? RevisionDate { get; init; }
}

internal sealed record VaultCipherDto
{
    public string? Id { get; init; }
    public string? OrganizationId { get; init; }
    public string? FolderId { get; init; }
    public int Type { get; init; }
    public string? Name { get; init; }
    public string? Notes { get; init; }
    public VaultLoginDto? Login { get; init; }
    public VaultCardDto? Card { get; init; }
    public VaultIdentityDto? Identity { get; init; }
    public VaultSecureNoteDto? SecureNote { get; init; }
    public VaultSshKeyDto? SshKey { get; init; }
    public List<VaultFieldDto>? Fields { get; init; }
    public List<VaultPasswordHistoryDto>? PasswordHistory { get; init; }
    public bool Favorite { get; init; }
    public int Reprompt { get; init; }
    public string? Key { get; init; }
    public string? RevisionDate { get; init; }
    public string? CreationDate { get; init; }
    public string? ArchivedDate { get; init; }
    public string? DeletedDate { get; init; }
}

internal sealed record VaultLoginDto
{
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? PasswordRevisionDate { get; init; }
    public string? Totp { get; init; }
    public List<VaultUriDto>? Uris { get; init; }
    public List<VaultFido2CredentialDto>? Fido2Credentials { get; init; }
}

internal sealed record VaultUriDto
{
    public string? Uri { get; init; }
    public int? Match { get; init; }
}

internal sealed record VaultFido2CredentialDto
{
    public string? CredentialId { get; init; }
    public string? KeyType { get; init; }
    public string? KeyAlgorithm { get; init; }
    public string? KeyCurve { get; init; }
    public string? KeyValue { get; init; }
    public string? RpId { get; init; }
    public string? RpName { get; init; }
    public string? Counter { get; init; }
    public string? UserHandle { get; init; }
    public string? UserName { get; init; }
    public string? UserDisplayName { get; init; }
    public string? Discoverable { get; init; }
    public string? CreationDate { get; init; }
}

internal sealed record VaultCardDto
{
    public string? CardholderName { get; init; }
    public string? Brand { get; init; }
    public string? Number { get; init; }
    public string? ExpMonth { get; init; }
    public string? ExpYear { get; init; }
    public string? Code { get; init; }
}

internal sealed record VaultIdentityDto
{
    public string? Title { get; init; }
    public string? FirstName { get; init; }
    public string? MiddleName { get; init; }
    public string? LastName { get; init; }
    public string? Address1 { get; init; }
    public string? Address2 { get; init; }
    public string? Address3 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public string? Company { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Ssn { get; init; }
    public string? Username { get; init; }
    public string? PassportNumber { get; init; }
    public string? LicenseNumber { get; init; }
}

internal sealed record VaultSecureNoteDto
{
    public int Type { get; init; }
}

internal sealed record VaultSshKeyDto
{
    public string? PrivateKey { get; init; }
    public string? PublicKey { get; init; }
    public string? KeyFingerprint { get; init; }
}

internal sealed record VaultFieldDto
{
    public string? Name { get; init; }
    public string? Value { get; init; }
    public int Type { get; init; }
    public int? LinkedId { get; init; }
}

internal sealed record VaultPasswordHistoryDto
{
    public string? Password { get; init; }
    public string? LastUsedDate { get; init; }
}

internal sealed record MutationCipherResponseDto
{
    public string? Id { get; init; }
    public string? RevisionDate { get; init; }
}
