using System.Text.Json;
using Monica.Core.Models;
using Monica.Data.Mdbx;

namespace Monica.Tests;

public sealed class AndroidMdbxPayloadCodecTests
{
    [Fact]
    public void Decode_password_reads_android_flat_payload_and_record_title()
    {
        var decoded = AndroidMdbxPayloadCodec.DecodePassword(
            ReadFixture("android-login-v1.json"),
            "Android 门户");

        Assert.NotNull(decoded);
        Assert.Equal(42, decoded.Entry.Id);
        Assert.Equal("Android 门户", decoded.Entry.Title);
        Assert.Equal("https://例子.example/登录", decoded.Entry.Website);
        Assert.Equal("测试用户@example.com", decoded.Entry.Username);
        Assert.Equal("S3cret-密码-🔐", decoded.Entry.Password);
        Assert.Equal("com.example.portal", decoded.Entry.AppPackageName);
        Assert.Equal("示例门户", decoded.Entry.AppName);
        Assert.Equal(7, decoded.Entry.CategoryId);
        Assert.Equal("login:42", decoded.Entry.MdbxFolderId);
        Assert.Equal(84, decoded.Entry.BoundNoteId);
        Assert.Equal(PasswordLoginType.Password, decoded.Entry.LoginType);
        Assert.Equal("note:84", decoded.BoundNoteEntryId);
        Assert.Contains("JBSWY3DPEHPK3PXP", decoded.Entry.AuthenticatorKey, StringComparison.Ordinal);
        Assert.Equal("fixture-credential", JsonDocument.Parse(decoded.Entry.PasskeyBindings).RootElement[0].GetProperty("credentialId").GetString());

        Assert.Collection(
            decoded.CustomFields,
            field =>
            {
                Assert.Equal("Account number", field.Title);
                Assert.Equal("A-00042", field.Value);
                Assert.False(field.IsProtected);
                Assert.Equal(0, field.SortOrder);
            },
            field =>
            {
                Assert.Equal("恢复代码", field.Title);
                Assert.Equal("fixture-recovery-code", field.Value);
                Assert.True(field.IsProtected);
                Assert.Equal(1, field.SortOrder);
            });
    }

    [Fact]
    public void Decode_password_accepts_android_legacy_aliases()
    {
        const string json =
            """
            {
              "kind": "password",
              "room_id": 5,
              "appPackageName": "legacy.package",
              "appName": "Legacy App",
              "password": "legacy-plain",
              "login_type": "WIFI",
              "mdbx_folder_id": "root",
              "customFields": [
                { "label": "Legacy field", "value": "value", "isProtected": true, "sortOrder": 3 }
              ]
            }
            """;

        var decoded = AndroidMdbxPayloadCodec.DecodePassword(json, "Legacy");

        Assert.NotNull(decoded);
        Assert.Equal("legacy.package", decoded.Entry.AppPackageName);
        Assert.Equal("Legacy App", decoded.Entry.AppName);
        Assert.Equal("legacy-plain", decoded.Entry.Password);
        Assert.Equal(PasswordLoginType.Wifi, decoded.Entry.LoginType);
        Assert.Null(decoded.Entry.MdbxFolderId);
        var field = Assert.Single(decoded.CustomFields);
        Assert.Equal("Legacy field", field.Title);
        Assert.True(field.IsProtected);
        Assert.Equal(3, field.SortOrder);
    }

    [Fact]
    public void Api_key_payload_round_trips_login_type_and_marker_fields()
    {
        var entry = new PasswordEntry
        {
            Id = 47,
            Title = "API service",
            Password = "sk-secret",
            LoginType = PasswordLoginType.ApiKey
        };
        CustomField[] fields =
        [
            new() { EntryId = 47, Title = ApiKeyEntryFields.Marker, Value = ApiKeyEntryFields.Type },
            new() { EntryId = 47, Title = ApiKeyEntryFields.ApiUrl, Value = "https://api.example.test/v1", SortOrder = 1 }
        ];

        var payload = AndroidMdbxPayloadCodec.EncodePassword(entry, fields, folderId: null);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal("API_KEY", document.RootElement.GetProperty("login_type").GetString());

        var decoded = AndroidMdbxPayloadCodec.DecodePassword(payload, entry.Title);
        Assert.NotNull(decoded);
        Assert.Equal(PasswordLoginType.ApiKey, decoded.Entry.LoginType);
        Assert.Contains(decoded.CustomFields, field => field.Title == ApiKeyEntryFields.ApiUrl);
    }

    [Fact]
    public void Encode_password_writes_android_field_names_without_avalonia_wrapper()
    {
        var entry = new PasswordEntry
        {
            Id = 42,
            Title = "Record title is external",
            Website = "https://example.test",
            Username = "fixture-user",
            Password = "portable-secret",
            Notes = "fixture",
            AppPackageName = "com.example.test",
            AppName = "Example",
            CategoryId = 7,
            MdbxFolderId = "login:42",
            BoundNoteId = 84,
            LoginType = PasswordLoginType.Sso,
            AuthenticatorKey = "portable-authenticator",
            PasskeyBindings = "[]",
            BitwardenVaultId = 1
        };
        CustomField[] fields =
        [
            new() { EntryId = 42, Title = "Protected", Value = "value", IsProtected = true, SortOrder = 4 }
        ];

        using var document = JsonDocument.Parse(AndroidMdbxPayloadCodec.EncodePassword(entry, fields, "folder-77", "note:84"));
        var root = document.RootElement;

        Assert.Equal("password", root.GetProperty("kind").GetString());
        Assert.Equal(42, root.GetProperty("room_id").GetInt64());
        Assert.Equal("portable-secret", root.GetProperty("password_plain").GetString());
        Assert.Equal("com.example.test", root.GetProperty("app_package_name").GetString());
        Assert.Equal("SSO", root.GetProperty("login_type").GetString());
        Assert.Equal("note:84", root.GetProperty("bound_note_entry_id").GetString());
        Assert.True(root.GetProperty("bitwarden_mode").GetBoolean());
        Assert.False(root.GetProperty("keepass_mode").GetBoolean());
        Assert.Equal("Protected", root.GetProperty("custom_fields")[0].GetProperty("title").GetString());
        Assert.True(root.GetProperty("custom_fields")[0].GetProperty("is_protected").GetBoolean());
        Assert.False(root.TryGetProperty("data", out _));
        Assert.False(root.TryGetProperty("schemaVersion", out _));
        Assert.False(root.TryGetProperty("title", out _));
    }

    [Fact]
    public void Encode_password_omits_archive_extension_for_active_entries()
    {
        using var document = JsonDocument.Parse(AndroidMdbxPayloadCodec.EncodePassword(
            new PasswordEntry { Id = 42, Title = "Active" },
            [],
            folderId: null));

        Assert.False(document.RootElement.TryGetProperty("is_archived", out _));
        Assert.False(document.RootElement.TryGetProperty("archived_at", out _));
    }

    [Fact]
    public void Encode_password_round_trips_archive_state()
    {
        var archivedAt = new DateTimeOffset(2026, 5, 4, 3, 2, 1, TimeSpan.Zero);
        var entry = new PasswordEntry { Id = 42, Title = "Archived", IsArchived = true, ArchivedAt = archivedAt };

        var payload = AndroidMdbxPayloadCodec.EncodePassword(entry, [], folderId: null);
        using var document = JsonDocument.Parse(payload);
        Assert.True(document.RootElement.GetProperty("is_archived").GetBoolean());
        Assert.Equal(archivedAt.ToUnixTimeMilliseconds(), document.RootElement.GetProperty("archived_at").GetInt64());

        var decoded = AndroidMdbxPayloadCodec.DecodePassword(payload, "Archived");
        Assert.NotNull(decoded);
        Assert.True(decoded.Entry.IsArchived);
        Assert.Equal(archivedAt, decoded.Entry.ArchivedAt);
    }

    [Theory]
    [InlineData("android-note-v1.json", "note", VaultItemType.Note, 101)]
    [InlineData("android-totp-v1.json", "totp", VaultItemType.Totp, 102)]
    [InlineData("android-card-v1.json", "card", VaultItemType.BankCard, 103)]
    [InlineData("android-document-v1.json", "document-ref", VaultItemType.Document, 104)]
    public void Decode_secure_item_reads_android_flat_payload(
        string fixture,
        string entryType,
        VaultItemType expectedType,
        long expectedId)
    {
        var decoded = AndroidMdbxPayloadCodec.DecodeSecureItem(
            ReadFixture(fixture),
            $"Fixture {expectedId}",
            entryType);

        Assert.NotNull(decoded);
        Assert.Equal(expectedId, decoded.Item.Id);
        Assert.Equal($"Fixture {expectedId}", decoded.Item.Title);
        Assert.Equal(expectedType, decoded.Item.ItemType);
        Assert.False(string.IsNullOrWhiteSpace(decoded.Item.ItemData));
        Assert.StartsWith("[", decoded.Item.ImagePaths, StringComparison.Ordinal);
        if (expectedType == VaultItemType.Totp)
        {
            Assert.Equal("login:42", decoded.BoundPasswordEntryId);
        }
    }

    [Fact]
    public void Encode_secure_item_writes_android_field_names_without_avalonia_wrapper()
    {
        var item = new SecureItem
        {
            Id = 103,
            ItemType = VaultItemType.BankCard,
            Title = "External title",
            Notes = "fixture",
            ItemData = "{\"cardNumber\":\"4111111111111111\"}",
            ImagePaths = "[]",
            CategoryId = 10,
            MdbxFolderId = "card:103",
            BoundPasswordId = 42,
            KeepassDatabaseId = 2
        };

        using var document = JsonDocument.Parse(AndroidMdbxPayloadCodec.EncodeSecureItem(item, "folder-103", "login:42"));
        var root = document.RootElement;

        Assert.Equal("bank_card", root.GetProperty("kind").GetString());
        Assert.Equal(103, root.GetProperty("room_id").GetInt64());
        Assert.Equal(item.ItemData, root.GetProperty("item_data").GetString());
        Assert.Equal("login:42", root.GetProperty("bound_password_entry_id").GetString());
        Assert.False(root.GetProperty("bitwarden_mode").GetBoolean());
        Assert.True(root.GetProperty("keepass_mode").GetBoolean());
        Assert.False(root.TryGetProperty("data", out _));
        Assert.False(root.TryGetProperty("schemaVersion", out _));
        Assert.False(root.TryGetProperty("title", out _));
    }

    [Fact]
    public void Encode_password_emits_every_key_android_writes()
    {
        // Verbatim key list from Monica Android's Mdbx2Repository.passwordMutation. A missing key
        // is either dropped or kept stale when the entry travels back to Android.
        string[] androidKeys =
        [
            "kind", "monica_entry_id", "room_id", "website", "username", "app_package_name",
            "app_name", "password_plain", "notes", "sort_order", "category_id", "mdbx_folder_id",
            "bound_note_room_id", "bound_note_entry_id", "login_type", "ssh_key_data",
            "authenticator_key", "passkey_bindings", "custom_fields", "bitwarden_mode", "keepass_mode"
        ];
        var entry = new PasswordEntry
        {
            Id = 42,
            Website = "https://example.test",
            Username = "fixture-user",
            Password = "portable-secret",
            SortOrder = 5,
            CategoryId = 7,
            MdbxFolderId = "folder-77",
            ReplicaGroupId = "login:42",
            BoundNoteId = 84,
            SshKeyData = "fixture-ssh-public-key-material",
            AuthenticatorKey = "portable-authenticator",
            PasskeyBindings = "[]"
        };

        using var document = JsonDocument.Parse(
            AndroidMdbxPayloadCodec.EncodePassword(entry, [], "folder-77", "note:84"));
        var produced = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        // deleted_at and the archive fields are Avalonia-only extensions; Android ignores unknown keys.
        Assert.Empty(produced.Except(androidKeys.Concat(["password_history", "attachments", "deleted_at"])));
        foreach (var key in androidKeys)
        {
            Assert.Contains(key, produced);
        }
    }

    [Fact]
    public void Decode_password_keeps_android_identity_and_ordering_fields()
    {
        const string json =
            """
            {
              "kind": "password",
              "monica_entry_id": "login:42",
              "room_id": 42,
              "sort_order": 5,
              "ssh_key_data": "fixture-ssh-private-key-material",
              "password_plain": "portable-secret"
            }
            """;

        var decoded = AndroidMdbxPayloadCodec.DecodePassword(json, "Android login");

        Assert.NotNull(decoded);
        Assert.Equal(5, decoded.Entry.SortOrder);
        Assert.Equal("fixture-ssh-private-key-material", decoded.Entry.SshKeyData);
        Assert.Equal("login:42", decoded.Entry.ReplicaGroupId);

        using var roundTrip = JsonDocument.Parse(AndroidMdbxPayloadCodec.EncodePassword(decoded.Entry, decoded.CustomFields, folderId: null));
        Assert.Equal("login:42", roundTrip.RootElement.GetProperty("monica_entry_id").GetString());
        Assert.Equal(5, roundTrip.RootElement.GetProperty("sort_order").GetInt32());
        Assert.Equal(
            "fixture-ssh-private-key-material",
            roundTrip.RootElement.GetProperty("ssh_key_data").GetString());
    }

    [Fact]
    public void Encode_password_does_not_write_an_empty_ssh_key_android_would_read_as_a_clear()
    {
        using var document = JsonDocument.Parse(AndroidMdbxPayloadCodec.EncodePassword(
            new PasswordEntry { Id = 42, SshKeyData = "" },
            [],
            folderId: null));

        Assert.False(document.RootElement.TryGetProperty("ssh_key_data", out _));
    }

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mdbx", fileName));
}
