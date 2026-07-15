using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.Tests;

public sealed partial class CoreServicesTests
{
    [Fact]
    public void BitwardenJson_maps_supported_items_and_preserves_source_metadata()
    {
        var snapshot = new ImportExportService().ImportBitwardenJson(BitwardenFixtureJson);

        var folder = Assert.Single(snapshot.Folders);
        Assert.Equal("folder-1", folder.Id);
        Assert.Equal("Team", folder.Name);
        Assert.Equal(2, snapshot.Passwords.Count);
        Assert.Equal(3, snapshot.SecureItems.Count);
        Assert.Equal(1, snapshot.UnsupportedItemCount);
        Assert.Equal(1, snapshot.AttachmentMetadataCount);

        var login = Assert.Single(snapshot.Passwords, item => item.BitwardenCipherType == 1);
        Assert.Equal("Production login", login.Title);
        Assert.Equal("dev@example.com", login.Username);
        Assert.Equal("login-secret", login.Password);
        Assert.Equal("https://example.com, https://account.example.com", login.Website);
        Assert.Equal("com.example.app", login.AppPackageName);
        Assert.Equal("dev@example.com", login.Email);
        Assert.Equal("+1-555-0100", login.Phone);
        Assert.Equal("JBSWY3DPEHPK3PXP", login.AuthenticatorKey);
        Assert.Equal("folder-1", login.BitwardenFolderId);
        Assert.Equal("cipher-login", login.BitwardenCipherId);
        Assert.Equal("bitwarden-json:cipher-login", login.ReplicaGroupId);
        Assert.Null(login.BitwardenVaultId);
        Assert.False(login.BitwardenLocalModified);
        Assert.Contains("passkey-id", login.PasskeyBindings, StringComparison.Ordinal);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), login.CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), login.UpdatedAt);

        var fieldGroup = Assert.Single(snapshot.PasswordCustomFields, group => group.PasswordId == login.Id);
        Assert.Contains(fieldGroup.Fields, field => field.Title == "API key" && field.Value == "field-secret" && field.IsProtected);
        Assert.Contains(fieldGroup.Fields, field => field.Title == "Enabled" && field.Value == "true" && !field.IsProtected);
        var historyGroup = Assert.Single(snapshot.PasswordHistory, group => group.PasswordId == login.Id);
        var history = Assert.Single(historyGroup.Entries);
        Assert.Equal("old-secret", history.Password);
        Assert.Equal(new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero), history.LastUsedAt);

        var ssh = Assert.Single(snapshot.Passwords, item => item.BitwardenCipherType == 5);
        Assert.Equal(PasswordLoginType.SshKey, ssh.LoginType);
        Assert.Contains("PRIVATE KEY", ssh.SshKeyData, StringComparison.Ordinal);
        Assert.Contains("ssh-ed25519", ssh.SshKeyData, StringComparison.Ordinal);

        var note = Assert.Single(snapshot.SecureItems, item => item.ItemType == VaultItemType.Note);
        Assert.Equal("Runbook", note.Title);
        Assert.Equal("Deploy carefully", NoteContentCodec.DecodeFromItem(note).Content);
        Assert.True(note.IsDeleted);
        Assert.Equal(new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero), note.DeletedAt);

        var card = Assert.Single(snapshot.SecureItems, item => item.ItemType == VaultItemType.BankCard);
        var cardData = WalletItemDataCodec.DecodeBankCard(card);
        Assert.Equal("4111111111111111", cardData.CardNumber);
        Assert.Equal("Dev User", cardData.CardholderName);
        Assert.Equal("Visa", cardData.Brand);

        var identity = Assert.Single(snapshot.SecureItems, item => item.ItemType == VaultItemType.Document);
        var identityData = WalletItemDataCodec.DecodeDocument(identity);
        Assert.Equal("P1234567", identityData.DocumentNumber);
        Assert.Equal("Dev Q User", identityData.FullName);
        Assert.Equal("PASSPORT", identityData.DocumentTypeString);
        Assert.Contains("dev@example.com", identityData.AdditionalInfo, StringComparison.Ordinal);
    }

    [Fact]
    public void BitwardenJson_rejects_encrypted_exports_without_echoing_payload()
    {
        const string encrypted = "{\"encrypted\":true,\"data\":\"never-echo-this-secret\"}";

        var error = Assert.Throws<BitwardenJsonImportException>(() =>
            new ImportExportService().ImportBitwardenJson(encrypted));

        Assert.Equal(BitwardenJsonImportError.EncryptedExport, error.Error);
        Assert.DoesNotContain("never-echo-this-secret", error.ToString(), StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void BitwardenJson_normalizes_malformed_exports()
    {
        var error = Assert.Throws<BitwardenJsonImportException>(() =>
            new ImportExportService().ImportBitwardenJson("{invalid-json"));

        Assert.Equal(BitwardenJsonImportError.InvalidExport, error.Error);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void BitwardenJson_enforces_configured_collection_limits()
    {
        var importer = new BitwardenJsonImporter(new BitwardenJsonImportLimits(MaximumItems: 1));
        const string json = "{\"encrypted\":false,\"folders\":[],\"items\":[{\"id\":\"1\",\"type\":1},{\"id\":\"2\",\"type\":1}]}";

        var error = Assert.Throws<BitwardenJsonImportException>(() => importer.Parse(json));

        Assert.Equal(BitwardenJsonImportError.ResourceLimitExceeded, error.Error);
    }

    private const string BitwardenFixtureJson = """
        {
          "encrypted": false,
          "folders": [
            { "id": "folder-1", "name": "Team" }
          ],
          "items": [
            {
              "id": "cipher-login",
              "folderId": "folder-1",
              "type": 1,
              "name": "Production login",
              "notes": "Primary account",
              "favorite": true,
              "creationDate": "2026-01-01T00:00:00Z",
              "revisionDate": "2026-02-01T00:00:00Z",
              "fields": [
                { "name": "email", "value": "dev@example.com", "type": 0 },
                { "name": "phone", "value": "+1-555-0100", "type": 0 },
                { "name": "API key", "value": "field-secret", "type": 1 },
                { "name": "Enabled", "value": "true", "type": 2 }
              ],
              "passwordHistory": [
                { "password": "old-secret", "lastUsedDate": "2025-12-01T00:00:00Z" }
              ],
              "attachments": [
                { "id": "attachment-1", "fileName": "manual.pdf" }
              ],
              "login": {
                "username": "dev@example.com",
                "password": "login-secret",
                "totp": "JBSWY3DPEHPK3PXP",
                "uris": [
                  { "uri": "https://example.com" },
                  { "uri": "androidapp://com.example.app" },
                  { "uri": "https://account.example.com" }
                ],
                "fido2Credentials": [
                  { "credentialId": "passkey-id", "rpId": "example.com", "userName": "dev@example.com" }
                ]
              }
            },
            {
              "id": "cipher-note",
              "folderId": "folder-1",
              "type": 2,
              "name": "Runbook",
              "notes": "Deploy carefully",
              "deletedDate": "2026-02-05T00:00:00Z",
              "secureNote": { "type": 0 }
            },
            {
              "id": "cipher-card",
              "type": 3,
              "name": "Corporate Visa",
              "notes": "Travel",
              "card": {
                "cardholderName": "Dev User",
                "brand": "Visa",
                "number": "4111111111111111",
                "expMonth": "12",
                "expYear": "2030",
                "code": "123"
              }
            },
            {
              "id": "cipher-identity",
              "type": 4,
              "name": "Developer identity",
              "identity": {
                "title": "Mx",
                "firstName": "Dev",
                "middleName": "Q",
                "lastName": "User",
                "address1": "1 Main Street",
                "city": "Example City",
                "state": "CA",
                "postalCode": "90001",
                "country": "US",
                "email": "dev@example.com",
                "phone": "+1-555-0100",
                "ssn": "111-22-3333",
                "passportNumber": "P1234567",
                "licenseNumber": "D7654321"
              }
            },
            {
              "id": "cipher-ssh",
              "type": 5,
              "name": "Deployment SSH",
              "sshKey": {
                "privateKey": "-----BEGIN OPENSSH PRIVATE KEY-----",
                "publicKey": "ssh-ed25519 AAAATEST dev@example.com",
                "keyFingerprint": "SHA256:test"
              }
            },
            {
              "id": "cipher-unknown",
              "type": 99,
              "name": "Future type"
            }
          ]
        }
        """;
}
