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

        using var document = JsonDocument.Parse(AndroidMdbxPayloadCodec.EncodePassword(entry, fields, "note:84"));
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

        using var document = JsonDocument.Parse(AndroidMdbxPayloadCodec.EncodeSecureItem(item, "login:42"));
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

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mdbx", fileName));
}
