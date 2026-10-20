using System.Text.Json;
using System.Text.Json.Nodes;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Mdbx;
using Monica.Data.Repositories;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class BitwardenMdbxAcknowledgementTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public async Task Real_native_acknowledgement_preserves_content_extensions_and_trash(
        bool password, bool deleted, bool legacy)
    {
        var bridge = new MdbxUniffiNativeBridge();
        var path = TestTempPaths.CreateFilePath(".mdbx");
        const string vaultPassword = "synthetic-bitwarden-metadata-test";
        const string title = "Saved while upload was in flight";
        var entry = new PasswordEntry
        {
            Id = 11,
            Title = title,
            Username = "synthetic-user",
            Password = "synthetic-new-password",
            BitwardenVaultId = 17,
            BitwardenCipherId = "cipher",
            BitwardenRevisionDate = "r1"
        };
        var note = new SecureItem
        {
            Id = 11,
            Title = title,
            ItemType = VaultItemType.Note,
            ItemData = "{\"content\":\"new note\"}",
            BitwardenVaultId = 17,
            BitwardenCipherId = "cipher",
            BitwardenRevisionDate = "r1"
        };
        CustomField[] fields = [new() { EntryId = 11, Title = "Recovery", Value = "synthetic-new-value", IsProtected = true }];
        PasswordHistoryEntry[] history = [new() { EntryId = 11, Password = "synthetic-history" }];
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonObject payload;
        if (legacy)
        {
            payload = password
                ? JsonSerializer.SerializeToNode(new { data = new { entry, customFields = fields, passwordHistory = history } }, jsonOptions)!.AsObject()
                : JsonSerializer.SerializeToNode(new { data = new { item = note } }, jsonOptions)!.AsObject();
        }
        else
        {
            payload = JsonNode.Parse(password
                ? AndroidMdbxPayloadCodec.EncodePassword(entry, fields, null, passwordHistory: history)
                : AndroidMdbxPayloadCodec.EncodeSecureItem(note, null))!.AsObject();
            // A future field named data is still part of a flat payload, not a legacy envelope.
            payload["data"] = new JsonObject { ["future"] = "keep me" };
        }
        payload["future_extension"] = JsonNode.Parse("{\"values\":[true,null,9007199254740993]}");
        string projectId;
        string nativeId;
        using (var native = await bridge.CreateVaultAsync(path, vaultPassword, "metadata-test", MdbxTigaMode.Multi))
        {
            projectId = (await native.CreateProjectAsync("Synthetic metadata test")).ProjectId;
            nativeId = (await native.CreateEntryAsync(projectId, password ? "login" : "note", title, payload.ToJsonString())).EntryId;
            if (deleted) await native.DeleteEntryAsync(projectId, nativeId);
        }

        var factory = new SqliteConnectionFactory(TestTempPaths.CreateFilePath(".db"));
        using (var store = new MdbxVaultStore(bridge))
        {
            var repository = new MdbxBackedMonicaRepository(new MonicaRepository(factory, new DatabaseMigrator(factory)), store);
            var database = new LocalMdbxDatabase
            {
                Name = "Synthetic",
                FilePath = path,
                WorkingCopyPath = path,
                EncryptedPassword = vaultPassword,
                IsDefault = true,
                StorageLocation = MdbxStorageLocation.Internal,
                IsOfflineAvailable = true
            };
            await repository.SaveMdbxDatabaseAsync(database);
            Assert.True(await repository.TryAcknowledgeBitwardenMutationAsync(new(
                password, 11, 17, "cipher", "cipher", "r2", database.Id, nativeId)));
            if (password)
            {
                var current = Assert.Single(await repository.GetPasswordsAsync(true, true));
                Assert.Equal(title, current.Title);
                Assert.Equal("synthetic-new-password", current.Password);
                Assert.Equal("r2", current.BitwardenRevisionDate);
                Assert.Equal(deleted, current.IsDeleted);
                Assert.Equal("synthetic-new-value", Assert.Single(await repository.GetCustomFieldsAsync(11)).Value);
                Assert.Equal("synthetic-history", Assert.Single(await repository.GetPasswordHistoryAsync(11)).Password);
                current.Title = "Edited after acknowledgement";
                current.Password = "second-edit";
                await repository.SavePasswordAsync(current);
                var edited = Assert.Single(await repository.GetPasswordsAsync(true, true));
                Assert.Equal("second-edit", edited.Password);
                Assert.Equal(deleted, edited.IsDeleted);
                Assert.Equal("r2", edited.BitwardenRevisionDate);
            }
            else
            {
                var current = Assert.Single(await repository.GetSecureItemsAsync(includeDeleted: true));
                Assert.Equal(title, current.Title);
                Assert.Equal(note.ItemData, current.ItemData);
                Assert.Equal("r2", current.BitwardenRevisionDate);
                Assert.Equal(deleted, current.IsDeleted);
                current.Title = "Edited after acknowledgement";
                current.ItemData = "{\"content\":\"second edit\"}";
                await repository.SaveSecureItemAsync(current);
                var edited = Assert.Single(await repository.GetSecureItemsAsync(includeDeleted: true));
                Assert.Equal(current.ItemData, edited.ItemData);
                Assert.Equal(deleted, edited.IsDeleted);
                Assert.Equal("r2", edited.BitwardenRevisionDate);
            }
        }

        using var verify = await bridge.OpenVaultAsync(path, vaultPassword, "verify-metadata");
        var records = deleted ? await verify.ListDeletedEntriesAsync(projectId) : await verify.ListEntriesAsync(projectId);
        var stored = Assert.Single(records);
        Assert.Equal(nativeId, stored.EntryId);
        using var after = JsonDocument.Parse(stored.PayloadJson);
        Assert.Equal("{\"values\":[true,null,9007199254740993]}", after.RootElement.GetProperty("future_extension").GetRawText());
        if (!legacy) Assert.Equal("keep me", after.RootElement.GetProperty("data").GetProperty("future").GetString());
    }
}
