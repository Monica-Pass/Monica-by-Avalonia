using System.Text.Json;
using Monica.Core.Models;
using Monica.Data.Mdbx;
using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// Cross-client acceptance harness required by docs/storage/MDBX-CROSS-CLIENT-CONTRACT.zh-CN.md §8:
/// a record is authored the way the other client authors it, the desktop reads and edits it through the
/// real store and the real native engine, and the bytes left on disk are compared afterwards. Every
/// assertion is a boolean, a key name or a count — never a payload value — so a failure cannot print a
/// decrypted secret.
/// Nested metadata inside custom_fields items is deliberately not asserted: the payload has no stable
/// id per item, so index matching could attach a foreign item's metadata to the wrong row, which is
/// worse than dropping it. That gap stays open and is named in HANDOFF.
/// </summary>
public sealed class MdbxCrossClientCompatTests
{
    private const string VaultPassword = "compat-harness-password-not-a-secret";
    private const string DeviceId = "compat-harness-device";

    private const string ForeignPasswordPayload = """
        {
          "kind": "password",
          "room_id": 4242,
          "website": "example.org",
          "username": "compat-user",
          "password_plain": "compat-initial-plain",
          "notes": "compat-notes",
          "sort_order": 7,
          "login_type": "PASSWORD",
          "custom_fields": [{"title":"Alias","value":"compat-alias","is_protected":false,"sort_order":0,"future_field_in_item":"not-preserved-yet"}],
          "recovery_codes": ["synthetic-one","示例二",null],
          "metadata": {"enabled":false,"counter":9007199254740993,"empty":""},
          "future_top_level": "not-understood-yet"
        }
        """;

    private const string ClearedPasswordPayload = """
        {
          "kind": "password",
          "room_id": 4243,
          "username": "compat-clear-user",
          "password": "legacy-value-that-must-not-come-back",
          "password_plain": ""
        }
        """;

    private const string SshKeyPasswordPayload = """
        {
          "kind": "password",
          "room_id": 4244,
          "username": "compat-ssh-user",
          "password_plain": "compat-initial-plain",
          "login_type": "SSH_KEY",
          "ssh_key_data": "compat-key-material-that-absence-must-inherit"
        }
        """;

    private const string ForeignSecureItemPayload = """
        {
          "kind": "billing_address",
          "room_id": 9001,
          "notes": "compat-notes",
          "sort_order": 3,
          "item_data": "{\"line1\":\"compat-line\"}",
          "image_paths": "[]",
          "recovery_codes": ["synthetic-one","示例二",null],
          "metadata": {"enabled":false,"counter":9007199254740993,"empty":""},
          "future_top_level": "not-understood-yet"
        }
        """;

    private const string UnknownTypePayload = """
        {
          "schema": "com.example.recovery-kit.v1",
          "account": "synthetic-user",
          "recovery_codes": ["synthetic-one","示例二",null],
          "metadata": {"enabled":false,"counter":9007199254740993,"empty":""}
        }
        """;

    [Fact]
    public async Task Editing_an_android_authored_password_keeps_the_top_level_fields_the_desktop_does_not_understand()
    {
        var authored = await AuthorAsync(new ForeignEntry("login", "Compat login", ForeignPasswordPayload));
        await EditPasswordOnDesktopAsync(authored);

        var stored = await ReadEntryAsync(authored.Path, authored.EntryIds[0]);
        Assert.True(HasField(stored.PayloadJson, "future_top_level"), "unknown top-level field was dropped");
        Assert.True(FieldRawEquals(stored.PayloadJson, "future_top_level", "\"not-understood-yet\""), "unknown field value changed");
        Assert.True(HasField(stored.PayloadJson, "recovery_codes"), "array field was dropped");
        Assert.Equal(3, ArrayLength(stored.PayloadJson, "recovery_codes"));
        Assert.Equal("9007199254740993", NestedRawText(stored.PayloadJson, "metadata", "counter"));
        Assert.Equal("false", NestedRawText(stored.PayloadJson, "metadata", "enabled"));
        Assert.Equal("\"\"", NestedRawText(stored.PayloadJson, "metadata", "empty"));
        Assert.True(FieldStringEquals(stored.PayloadJson, "notes", "desktop-edited-notes"), "the field the desktop owns was not written");
    }

    [Fact]
    public async Task Editing_only_notes_preserves_unknown_metadata_inside_custom_fields()
    {
        var authored = await AuthorAsync(new ForeignEntry("login", "Compat login", ForeignPasswordPayload));
        await EditPasswordOnDesktopAsync(authored);

        var stored = await ReadEntryAsync(authored.Path, authored.EntryIds[0]);
        using var before = JsonDocument.Parse(ForeignPasswordPayload);
        using var after = JsonDocument.Parse(stored.PayloadJson);
        Assert.True(JsonElement.DeepEquals(before.RootElement.GetProperty("custom_fields"),
            after.RootElement.GetProperty("custom_fields")), "untouched custom field metadata changed");
    }

    [Fact]
    public async Task Editing_an_android_authored_password_keeps_the_native_entry_type()
    {
        var authored = await AuthorAsync(new ForeignEntry("ssh-key", "Compat ssh login", SshKeyPasswordPayload));
        await EditPasswordOnDesktopAsync(authored);

        var stored = await ReadEntryAsync(authored.Path, authored.EntryIds[0]);

        Assert.Equal("ssh-key", stored.EntryType);
    }

    [Fact]
    public async Task An_explicitly_cleared_password_stays_cleared_instead_of_falling_back_to_the_legacy_field()
    {
        var authored = await AuthorAsync(new ForeignEntry("login", "Compat cleared login", ClearedPasswordPayload));

        var bridge = new MdbxUniffiNativeBridge();
        using (var store = new MdbxVaultStore(bridge))
        {
            var entry = Assert.Single(await store.GetPasswordsAsync(authored.Database));
            Assert.True(string.IsNullOrEmpty(entry.Password), "the legacy password field overwrote the explicit clear on read");

            await store.SavePasswordAsync(authored.Database, entry, [], new Dictionary<long, Category>());
        }

        var stored = await ReadEntryAsync(authored.Path, authored.EntryIds[0]);
        Assert.True(HasField(stored.PayloadJson, "password_plain"), "password_plain was not written");
        Assert.True(FieldRawEquals(stored.PayloadJson, "password_plain", "\"\""), "password_plain is no longer an explicit empty string");
        Assert.False(HasField(stored.PayloadJson, "password"), "the legacy password field survived the write");
    }

    [Fact]
    public async Task A_password_edit_leaves_ssh_key_material_alone_when_the_desktop_writes_no_value_for_it()
    {
        var authored = await AuthorAsync(new ForeignEntry("ssh-key", "Compat ssh login", SshKeyPasswordPayload));

        var bridge = new MdbxUniffiNativeBridge();
        using (var store = new MdbxVaultStore(bridge))
        {
            var entry = Assert.Single(await store.GetPasswordsAsync(authored.Database));
            entry.SshKeyData = "";
            await store.SavePasswordAsync(authored.Database, entry, [], new Dictionary<long, Category>());
        }

        var stored = await ReadEntryAsync(authored.Path, authored.EntryIds[0]);

        Assert.True(HasField(stored.PayloadJson, "ssh_key_data"), "an omitted field cleared the other client's key material");
    }

    [Fact]
    public async Task Editing_an_android_authored_secure_item_keeps_unknown_fields_and_the_native_entry_type()
    {
        var authored = await AuthorAsync(new ForeignEntry("identity", "Compat address", ForeignSecureItemPayload));

        var bridge = new MdbxUniffiNativeBridge();
        using (var store = new MdbxVaultStore(bridge))
        {
            var item = Assert.Single(await store.GetSecureItemsAsync(authored.Database));
            item.Notes = "desktop-edited-notes";
            await store.SaveSecureItemAsync(authored.Database, item, new Dictionary<long, Category>());
        }

        var stored = await ReadEntryAsync(authored.Path, authored.EntryIds[0]);
        Assert.Equal("identity", stored.EntryType);
        Assert.True(HasField(stored.PayloadJson, "future_top_level"), "unknown top-level field was dropped");
        Assert.Equal(3, ArrayLength(stored.PayloadJson, "recovery_codes"));
        Assert.Equal("9007199254740993", NestedRawText(stored.PayloadJson, "metadata", "counter"));
        Assert.True(FieldStringEquals(stored.PayloadJson, "notes", "desktop-edited-notes"), "the field the desktop owns was not written");
    }

    [Fact]
    public async Task A_desktop_edit_does_not_touch_a_record_whose_type_it_cannot_read()
    {
        var authored = await AuthorAsync(
            new ForeignEntry("login", "Compat login", ForeignPasswordPayload),
            new ForeignEntry("com.example.recovery-kit", "恢复资料", UnknownTypePayload));

        await EditPasswordOnDesktopAsync(authored);

        var stored = await ReadEntryAsync(authored.Path, authored.EntryIds[1]);
        Assert.Equal("com.example.recovery-kit", stored.EntryType);
        Assert.False(stored.Deleted);
        Assert.True(HasField(stored.PayloadJson, "schema"), "the unknown record lost a field");
        Assert.True(FieldStringEquals(stored.PayloadJson, "account", "synthetic-user"), "the unknown record was rewritten");
        Assert.Equal(3, ArrayLength(stored.PayloadJson, "recovery_codes"));
        Assert.Equal("9007199254740993", NestedRawText(stored.PayloadJson, "metadata", "counter"));
    }

    private static async Task EditPasswordOnDesktopAsync(AuthoredVault authored)
    {
        var bridge = new MdbxUniffiNativeBridge();
        using var store = new MdbxVaultStore(bridge);
        var entry = Assert.Single(await store.GetPasswordsAsync(authored.Database));
        var customFields = await store.GetPasswordCustomFieldsByEntryIdsAsync(authored.Database, [entry.Id]);
        entry.Notes = "desktop-edited-notes";
        await store.SavePasswordAsync(
            authored.Database,
            entry,
            customFields.TryGetValue(entry.Id, out var fields) ? fields : [],
            new Dictionary<long, Category>());
    }

    private sealed record ForeignEntry(string Type, string Title, string Payload);

    private sealed record AuthoredVault(string Path, LocalMdbxDatabase Database, IReadOnlyList<string> EntryIds);

    private static async Task<AuthoredVault> AuthorAsync(params ForeignEntry[] entries)
    {
        var bridge = new MdbxUniffiNativeBridge();
        Assert.True(bridge.IsAvailable, bridge.AvailabilityError ?? "native engine unavailable");
        var path = TestTempPaths.CreateFilePath(".mdbx");
        var ids = new List<string>();

        var vault = await bridge.CreateVaultAsync(path, VaultPassword, DeviceId, MdbxTigaMode.Multi);
        try
        {
            var rootProjectId = MdbxAndroidRoot.ProjectIdFor((await vault.GetInfoAsync()).VaultId);
            await vault.CreateProjectWithIdentityAsync(rootProjectId, MdbxAndroidRoot.Title, null);
            foreach (var entry in entries)
            {
                var record = await vault.CreateEntryAsync(rootProjectId, entry.Type, entry.Title, entry.Payload);
                ids.Add(record.EntryId);
            }
        }
        finally
        {
            vault.Dispose();
        }

        return new AuthoredVault(path, new LocalMdbxDatabase
        {
            Id = 1,
            Name = "Compat harness",
            FilePath = path,
            EncryptedPassword = VaultPassword
        }, ids);
    }

    private static async Task<MdbxNativeEntryRecord> ReadEntryAsync(string path, string entryId)
    {
        var bridge = new MdbxUniffiNativeBridge();
        var vault = await bridge.OpenVaultAsync(path, VaultPassword, DeviceId);
        try
        {
            foreach (var project in await vault.ListProjectsAsync())
            {
                foreach (var candidate in await vault.ListEntriesAsync(project.ProjectId, entryType: null))
                {
                    if (string.Equals(candidate.EntryId, entryId, StringComparison.Ordinal))
                    {
                        return candidate;
                    }
                }

                foreach (var candidate in await vault.ListDeletedEntriesAsync(project.ProjectId, entryType: null))
                {
                    if (string.Equals(candidate.EntryId, entryId, StringComparison.Ordinal))
                    {
                        return candidate;
                    }
                }
            }
        }
        finally
        {
            vault.Dispose();
        }

        throw new InvalidOperationException("The authored record is no longer on disk.");
    }

    private static JsonDocument Parse(string payloadJson) => JsonDocument.Parse(payloadJson);

    private static bool HasField(string payloadJson, string name)
    {
        using var document = Parse(payloadJson);
        return document.RootElement.TryGetProperty(name, out _);
    }

    private static bool FieldRawEquals(string payloadJson, string name, string expectedRaw)
    {
        using var document = Parse(payloadJson);
        return document.RootElement.TryGetProperty(name, out var value) &&
            value.GetRawText() == expectedRaw;
    }

    private static bool FieldStringEquals(string payloadJson, string name, string expected)
    {
        using var document = Parse(payloadJson);
        return document.RootElement.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            value.GetString() == expected;
    }

    private static int ArrayLength(string payloadJson, string name)
    {
        using var document = Parse(payloadJson);
        return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : -1;
    }

    private static string NestedRawText(string payloadJson, string objectName, string propertyName)
    {
        using var document = Parse(payloadJson);
        if (!document.RootElement.TryGetProperty(objectName, out var nested) ||
            !nested.TryGetProperty(propertyName, out var value))
        {
            return "";
        }

        return value.GetRawText();
    }
}
