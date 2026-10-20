using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using Monica.Core.Services;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Mdbx;
using Monica.Data.Repositories;
using Monica.Data.Passkeys;
using Monica.Core.Passkeys;
using System.Security.Cryptography;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class DevicePasswordSyncTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Standalone_reference_update_retains_keys_and_flags_or_rejects_changed_identity(bool differentKey)
    {
        await using var fixture = await Fixture.CreateAsync();
        var material = PasskeyKeyMaterialGenerator.Generate(PasskeyAlgorithm.Es256);
        var entry = new PasskeyEntry
        {
            CredentialId = Guid.NewGuid().ToString(),
            RpId = "example.test",
            RpName = "Reference fixture",
            UserId = "Zml4dHVyZQ==",
            PublicKey = material.PublicKeySpkiBase64,
            BackupEligible = false,
            BackupState = false
        };
        await fixture.Passkeys.SaveAsync(entry, material.PrivateKeyPkcs8Base64);
        var previous = (await fixture.Passkeys.GetAsync(entry.Id))!;
        var data = DevicePasskeyShape.Export(previous, null, verify: false);
        data["payload"]!.AsObject().Remove("backup_eligible"); data["payload"]!.AsObject().Remove("backup_state");
        data["payload"]!["public_key"] = differentKey ? PasskeyKeyMaterialGenerator.Generate(PasskeyAlgorithm.Es256).PublicKeySpkiBase64 : material.PublicKeySpkiBase64;
        var guard = Convert.ToHexString(SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(previous)));
        var update = () => fixture.Provider.ApplyPasskeyAsync(DevicePasskeyShape.Identity(previous), $"passkey:{previous.Id}", guard,
            false, data, CancellationToken.None);
        if (differentKey) Assert.Equal("passkey_conflict", (await Assert.ThrowsAsync<LanSyncException>(update)).Code);
        else Assert.NotNull(await update());
        var retained = (await fixture.Passkeys.GetAsync(previous.Id))!;
        Assert.Equal(material.PrivateKeyPkcs8Base64, await fixture.Passkeys.ResolvePrivateKeyAsync(retained));
        Assert.Equal(previous.PublicKey, retained.PublicKey);
        Assert.Equal(false, retained.BackupEligible);
        Assert.Equal(false, retained.BackupState);
    }

    [Theory]
    [InlineData(false, false, 0x05)]
    [InlineData(true, false, 0x0d)]
    [InlineData(true, true, 0x1d)]
    [InlineData(null, null, 0x1d)]
    public async Task Native_passkey_backup_flags_survive_projection_and_real_assertion(bool? eligible, bool? state, int flags)
    {
        await using var first = await Fixture.CreateAsync();
        await using var second = await Fixture.CreateAsync();
        var material = PasskeyKeyMaterialGenerator.Generate(PasskeyAlgorithm.Es256);
        var entry = new PasskeyEntry
        {
            CredentialId = Guid.NewGuid().ToString(),
            RpId = "example.test",
            RpName = "Backup flag fixture",
            UserId = "Zml4dHVyZQ==",
            PublicKey = material.PublicKeySpkiBase64
        };
        var payload = DevicePasskeyShape.Export(entry, material.PrivateKeyPkcs8Base64)["payload"]!.AsObject();
        payload["backup_eligible"] = eligible; payload["backup_state"] = state;
        var bridge = new MdbxUniffiNativeBridge();
        using (var vault = await bridge.OpenVaultAsync(first.Database.FilePath, first.Database.EncryptedPassword!, "backup-flag-fixture"))
        {
            var access = (IMdbxNativeDevicePasswordAccess)vault;
            await access.EnsureDeviceSyncFoldersAsync(new JsonArray(), CancellationToken.None);
            var root = (await access.ListDeviceSyncProjectsAsync(CancellationToken.None)).Single(p => MdbxAndroidRoot.IsRootTitle(p.Title));
            await access.CreateDeviceSyncEntryAsync(Guid.NewGuid().ToString(), root.ProjectId, "passkey", "Backup flag fixture", payload.ToJsonString(), CancellationToken.None);
        }
        await ExchangeAsync(first, second);
        var received = Assert.Single(await second.Passkeys.ListAllAsync());
        var privateKey = await second.Passkeys.ResolvePrivateKeyAsync(received);
        var challenge = RandomNumberGenerator.GetBytes(32);
        var assertion = PasskeyAuthenticator.Assert(received, privateKey!, challenge);
        Assert.Equal(flags, assertion.AuthenticatorData[32]);
        Assert.True(PasskeyKeyMaterialGenerator.Verify(received.PublicKeyAlgorithm, received.PublicKey,
            assertion.AuthenticatorData.Concat(assertion.ClientData.ClientDataHash).ToArray(), assertion.Signature));
        if (eligible is not null || state is not null)
        {
            // Emulate a projection written before these nullable columns existed.
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(Path.GetDirectoryName(second.Database.FilePath)!, "metadata.db")
            }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE passkeys SET backup_eligible=NULL,backup_state=NULL WHERE id=@id";
            command.Parameters.AddWithValue("@id", received.Id);
            await command.ExecuteNonQueryAsync();
            Assert.Null((await second.Passkeys.GetAsync(received.Id))!.BackupEligible);
            await second.Provider.ProjectNativePasskeysAsync(CancellationToken.None,
                await second.Vaults.ReadDevicePasswordsAsync(second.Database, CancellationToken.None));
            var restored = (await second.Passkeys.GetAsync(received.Id))!;
            Assert.Equal(eligible, restored.BackupEligible);
            Assert.Equal(state, restored.BackupState);
            Assert.Equal(flags, PasskeyAuthenticator.Assert(restored, privateKey!, challenge).AuthenticatorData[32]);
        }
    }

    [Theory]
    [InlineData(PasskeyAlgorithm.Es256)]
    [InlineData(PasskeyAlgorithm.Rs256)]
    [InlineData(PasskeyAlgorithm.Ps256)]
    public async Task Native_reference_update_preserves_private_key_across_public_encodings(int algorithm)
    {
        await using var fixture = await Fixture.CreateAsync();
        var material = PasskeyKeyMaterialGenerator.Generate(algorithm == PasskeyAlgorithm.Ps256 ? PasskeyAlgorithm.Rs256 : algorithm);
        var keyId = Guid.NewGuid().ToString();
        var entry = new PasskeyEntry
        {
            CredentialId = Guid.NewGuid().ToString(),
            RpId = "example.test",
            RpName = "Native reference key",
            UserId = "Zml4dHVyZQ==",
            PublicKeyAlgorithm = algorithm,
            PublicKey = material.PublicKeySpkiBase64
        };
        var bridge = new MdbxUniffiNativeBridge();
        using (var vault = await bridge.OpenVaultAsync(fixture.Database.FilePath, fixture.Database.EncryptedPassword!, "reference-fixture"))
        {
            var access = (IMdbxNativeDevicePasswordAccess)vault;
            await access.EnsureDeviceSyncFoldersAsync(new JsonArray(), CancellationToken.None);
            var root = (await access.ListDeviceSyncProjectsAsync(CancellationToken.None)).Single(p => MdbxAndroidRoot.IsRootTitle(p.Title));
            var originalPayload = DevicePasskeyShape.Export(entry, material.PrivateKeyPkcs8Base64)["payload"]!.AsObject();
            originalPayload["backup_eligible"] = false; originalPayload["backup_state"] = false;
            await access.CreateDeviceSyncEntryAsync(keyId, root.ProjectId, "passkey", "Native reference key",
                originalPayload.ToJsonString(), CancellationToken.None);
        }
        var before = Assert.Single(
            await fixture.Vaults.ReadDevicePasswordsAsync(fixture.Database, CancellationToken.None),
            s => s.Data["entry_type"]?.GetValue<string>() == "passkey");
        var reference = before.Data.DeepClone().AsObject();
        reference["payload"]!["private_key_alias"] = "";
        reference["payload"]!["key_status"] = "reference";
        reference["payload"]!["public_key"] = material.PublicKeySpkiBase64;
        reference["payload"]!["notes"] = "Reference metadata update";
        reference["payload"]!.AsObject().Remove("backup_eligible"); reference["payload"]!.AsObject().Remove("backup_state");
        Assert.NotNull(await fixture.Vaults.TryApplyDevicePasswordAsync(fixture.Database, before.GlobalId, before.LocalId,
            before.Guard, false, reference, CancellationToken.None));
        using (var vault = await bridge.OpenVaultAsync(fixture.Database.FilePath, fixture.Database.EncryptedPassword!, "reference-fixture"))
        {
            var access = (IMdbxNativeDevicePasswordAccess)vault;
            var summary = (await access.GetDeviceSyncSummaryAsync(keyId, CancellationToken.None))!;
            var current = (await access.ReadDeviceSyncObjectAsync(summary, CancellationToken.None))!;
            var payload = JsonNode.Parse(current.PayloadJson)!;
            Assert.Equal(material.PrivateKeyPkcs8Base64, payload["private_key_alias"]!.GetValue<string>());
            Assert.Equal("reference", payload["key_status"]!.GetValue<string>());
            Assert.Equal("Reference metadata update", payload["notes"]!.GetValue<string>());
            Assert.False(payload["backup_eligible"]!.GetValue<bool>());
            Assert.False(payload["backup_state"]!.GetValue<bool>());
        }
    }

    [Fact]
    public async Task Root_tags_and_native_passkey_relations_restore_live_credential_store_and_delete()
    {
        await using var first = await Fixture.CreateAsync(); await using var second = await Fixture.CreateAsync();
        var bridge = new MdbxUniffiNativeBridge(); var keyId = Guid.NewGuid().ToString(); var owner = Guid.NewGuid().ToString();
        var material = PasskeyKeyMaterialGenerator.Generate(PasskeyAlgorithm.Es256);
        var entry = new PasskeyEntry
        {
            CredentialId = Guid.NewGuid().ToString(),
            RpId = "example.test",
            RpName = "Native key",
            UserId = "Zml4dHVyZQ==",
            UserName = "fixture",
            PublicKeyAlgorithm = PasskeyAlgorithm.Es256,
            PublicKey = material.PublicKeySpkiBase64,
            Notes = "Native source"
        };
        await first.Passkeys.SaveAsync(entry, material.PrivateKeyPkcs8Base64);
        using (var native = await bridge.OpenVaultAsync(first.Database.FilePath, first.Database.EncryptedPassword!, "native-key-fixture"))
        {
            var access = (IMdbxNativeDevicePasswordAccess)native; await access.EnsureDeviceSyncFoldersAsync(new JsonArray(), CancellationToken.None);
            var root = (await access.ListDeviceSyncProjectsAsync(CancellationToken.None)).Single(p => MdbxAndroidRoot.IsRootTitle(p.Title));
            var metadata = Assert.Single(await access.ReadDeviceSyncRootMetadataAsync(CancellationToken.None));
            metadata.Data["payload"]!["tags"] = new JsonArray(new JsonObject { ["name"] = "Root fixture", ["payload"] = "{\"kind\":\"monica-project-tag\",\"extra\":\"synthetic\"}", ["version"] = 1 });
            await access.ApplyDeviceSyncRootMetadataAsync(root.ProjectId, metadata.Guard, false, false, metadata.Data, CancellationToken.None);
            await access.CreateDeviceSyncEntryAsync(keyId, root.ProjectId, "passkey", "Native key", DevicePasskeyShape.Export(entry, material.PrivateKeyPkcs8Base64)["payload"]!.ToJsonString(), CancellationToken.None);
            await access.CreateDeviceSyncEntryAsync(owner, root.ProjectId, "api-token", "Related token", new JsonObject
            {
                ["token"] = "synthetic",
                ["device_sync_pending_relations"] = new JsonArray(new JsonObject { ["target"] = keyId, ["kind"] = "com.monica.fixture.credential", ["payload"] = "{}", ["version"] = 1 })
            }.ToJsonString(), CancellationToken.None);
            await access.ResolveDeviceSyncRelationsAsync(CancellationToken.None);
        }
        await ExchangeAsync(first, second);
        var received = Assert.Single(await second.Passkeys.ListAllAsync());
        Assert.Equal("Native source", received.Notes);
        using (var native = await bridge.OpenVaultAsync(second.Database.FilePath, second.Database.EncryptedPassword!, "native-key-fixture"))
        {
            var access = (IMdbxNativeDevicePasswordAccess)native;
            Assert.Equal(keyId, (await access.ReadDeviceSyncRelationsAsync(owner, CancellationToken.None))[0]!["target"]!.GetValue<string>());
            Assert.Contains(await access.ReadDeviceSyncRootMetadataAsync(CancellationToken.None), m => m.Data["payload"]?["tags"]?.ToJsonString().Contains("Root fixture") == true);
        }
        received.Notes = "Changed on receiver";
        await second.Passkeys.SaveAsync(received);
        await ExchangeAsync(second, first);
        Assert.Equal("Changed on receiver", Assert.Single(await first.Passkeys.ListAllAsync()).Notes);
        await second.Passkeys.DeleteAsync(received.Id);
        await ExchangeAsync(second, first);
        Assert.Empty(await first.Passkeys.ListAllAsync()); Assert.Empty(await second.Passkeys.ListAllAsync());
    }

    [Fact]
    public async Task Relation_targets_use_existing_local_aliases()
    {
        await using var first = await Fixture.CreateAsync(); await using var second = await Fixture.CreateAsync();
        var bridge = new MdbxUniffiNativeBridge(); var a = "11111111-1111-4111-8111-111111111111"; var b = "22222222-2222-4222-8222-222222222222"; var owner = Guid.NewGuid().ToString();
        foreach (var (fixture, id) in new[] { (first, b), (second, a) }) using (var native = await bridge.OpenVaultAsync(fixture.Database.FilePath, fixture.Database.EncryptedPassword!, "alias-fixture"))
        {
            var root = await native.CreateProjectWithIdentityAsync("33333333-3333-4333-8333-333333333333", "Shared", null); var access = (IMdbxNativeDevicePasswordAccess)native;
            await access.CreateDeviceSyncEntryAsync(id, root.ProjectId, "note", "Same target", "{\"kind\":\"note\",\"item_data\":\"{\\\"content\\\":\\\"synthetic\\\"}\"}", CancellationToken.None);
            if (fixture == first)
            {
                await access.CreateDeviceSyncEntryAsync(owner, root.ProjectId, "api-token", "Owner", new JsonObject
                {
                    ["token"] = "synthetic",
                    ["device_sync_pending_relations"] = new JsonArray(new JsonObject
                    {
                        ["target"] = b,
                        ["kind"] = "com.monica.fixture.related",
                        ["payload"] = "{}",
                        ["version"] = 1
                    })
                }.ToJsonString(), CancellationToken.None);
                await access.ResolveDeviceSyncRelationsAsync(CancellationToken.None);
            }
        }
        // Folder membership is part of the payload; give both targets the same portable folder.
        var firstTarget = (await first.Vaults.ReadDevicePasswordsAsync(first.Database, CancellationToken.None)).Single(p => p.GlobalId == b);
        var secondTarget = (await second.Vaults.ReadDevicePasswordsAsync(second.Database, CancellationToken.None)).Single(p => p.GlobalId == a);
        secondTarget.Data["folder_path"] = firstTarget.Data["folder_path"]!.DeepClone();
        await second.Vaults.TryApplyDevicePasswordAsync(second.Database, a, secondTarget.LocalId, secondTarget.Guard, false, secondTarget.Data, CancellationToken.None);
        await ExchangeAsync(first, second);
        using (var native = await bridge.OpenVaultAsync(second.Database.FilePath, second.Database.EncryptedPassword!, "alias-fixture"))
        {
            Assert.Equal(a, (await ((IMdbxNativeDevicePasswordAccess)native).ReadDeviceSyncRelationsAsync(owner, CancellationToken.None))[0]!["target"]!.GetValue<string>());
        }
    }

    [Fact]
    public void Asset_references_preserve_arbitrary_user_text()
    {
        var data = new JsonObject
        {
            ["entry_type"] = "note",
            ["password_plain"] = "mdbx:fixture",
            ["notes"] = "mdbx:fixture",
            ["payload"] = new JsonObject
            {
                ["image_paths"] = "[\"mdbx:fixture\"]",
                ["item_data"] = "{\"content\":\"Text mdbx:fixture ![image](mdbx:fixture)\"}"
            }
        };
        DeviceVaultShape.RemapAssetPath(data, "mdbx:fixture", "device-asset:fixture");
        Assert.Equal("mdbx:fixture", data["password_plain"]!.GetValue<string>()); Assert.Equal("mdbx:fixture", data["notes"]!.GetValue<string>());
        Assert.Equal("[\"device-asset:fixture\"]", data["payload"]!["image_paths"]!.GetValue<string>());
        Assert.Equal("Text mdbx:fixture ![image](device-asset:fixture)", JsonNode.Parse(data["payload"]!["item_data"]!.GetValue<string>())!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Copied_vault_identifiers_keep_independent_records()
    {
        await using var first = await Fixture.CreateAsync(); await using var second = await Fixture.CreateAsync();
        var bridge = new MdbxUniffiNativeBridge(); var copyPath = Path.Combine(TestTempPaths.CreateDirectoryPath(), "copy.mdbx");
        await first.Repository.SavePasswordAsync(new PasswordEntry { Title = "Original copy", Password = "synthetic-original" });
        var original = Assert.Single(await first.Repository.GetPasswordsAsync());
        await ((IMdbxNativeSnapshotBridge)bridge).CreatePortableBackupAsync(first.Database.FilePath, copyPath, CancellationToken.None);
        var copied = new LocalMdbxDatabase { Name = "Copied", FilePath = copyPath, WorkingCopyPath = copyPath, EncryptedPassword = first.Database.EncryptedPassword, IsOfflineAvailable = true };
        await first.Repository.SaveMdbxDatabaseAsync(copied);
        var snapshot = (await first.Vaults.ReadDevicePasswordsAsync(copied, CancellationToken.None)).Single(p => p.Data["entry_type"]?.GetValue<string>() == "login");
        snapshot.Data["password_plain"] = "synthetic-copied"; snapshot.Data["title"] = "Changed copy";
        await first.Vaults.TryApplyDevicePasswordAsync(copied, snapshot.GlobalId, snapshot.LocalId, snapshot.Guard, false, snapshot.Data, CancellationToken.None);
        await ExchangeAsync(first, second);
        var received = await second.Repository.GetPasswordsAsync();
        Assert.Equal(2, received.Count);
        Assert.Contains(received, p => p.Password == "synthetic-original"); Assert.Contains(received, p => p.Password == "synthetic-copied");
        Assert.Equal("synthetic-original", Assert.Single(await first.Repository.GetPasswordsAsync()).Password);
    }
    [Fact]
    public async Task Legacy_wrapped_secure_item_keeps_its_data_notes_and_favorite()
    {
        await using var first = await Fixture.CreateAsync(); await using var second = await Fixture.CreateAsync();
        var bridge = new MdbxUniffiNativeBridge();
        using (var native = await bridge.OpenVaultAsync(first.Database.FilePath, first.Database.EncryptedPassword!, "legacy-secure"))
        {
            var project = await native.CreateProjectAsync("Legacy wallet");
            await native.CreateEntryAsync(project.ProjectId, "card", "Legacy card", JsonSerializer.Serialize(new
            {
                kind = "item",
                schemaVersion = 1,
                data = new
                {
                    item = new
                    {
                        id = 42,
                        itemType = 2,
                        notes = "synthetic-note",
                        isFavorite = true,
                        itemData = "{\"cardNumber\":\"synthetic-card\",\"extension\":true}",
                        imagePaths = "[]"
                    },
                    attachments = Array.Empty<object>()
                }
            }));
        }
        await ExchangeAsync(first, second);
        var item = Assert.Single(await second.Repository.GetSecureItemsAsync());
        Assert.Equal("synthetic-note", item.Notes); Assert.True(item.IsFavorite); Assert.Contains("synthetic-card", item.ItemData);
    }

    [Fact]
    public async Task Corrupted_incoming_asset_keeps_the_existing_business_record()
    {
        await using var first = await Fixture.CreateAsync(); await using var second = await Fixture.CreateAsync();
        await first.Repository.SavePasswordAsync(new PasswordEntry { Title = "Original", Password = "synthetic-old" });
        await ExchangeAsync(first, second);
        var changed = Assert.Single(await first.Repository.GetPasswordsAsync()); changed.Password = "synthetic-new";
        await first.Repository.SavePasswordAsync(changed);
        changed = Assert.Single(await first.Repository.GetPasswordsAsync());
        await first.Vaults.SavePasswordAttachmentAsync(first.Database, changed, new Attachment { FileName = "corrupt.bin", ContentType = "application/octet-stream" }, RandomNumberGenerator.GetBytes(700000));
        var snapshot = await first.Provider.PrepareAsync(TestTempPaths.CreateDirectoryPath(), CancellationToken.None);
        var path = Path.Combine(snapshot.Directory, "assets.monica"); var bytes = await File.ReadAllBytesAsync(path); bytes[^1] ^= 1; await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAnyAsync<Exception>(() => second.Provider.ApplyAsync(snapshot.Directory, Peer(snapshot), CancellationToken.None));
        Assert.Equal("synthetic-old", Assert.Single(await second.Repository.GetPasswordsAsync()).Password);
    }

    [Fact]
    public async Task Native_api_token_labels_and_cyclic_relations_survive_complete_sync()
    {
        await using var first = await Fixture.CreateAsync(); await using var second = await Fixture.CreateAsync();
        var bridge = new MdbxUniffiNativeBridge(); var a = Guid.NewGuid().ToString(); var b = Guid.NewGuid().ToString();
        using (var native = await bridge.OpenVaultAsync(first.Database.FilePath, first.Database.EncryptedPassword!, "metadata-fixture"))
        {
            var root = await native.CreateProjectAsync("Metadata"); var access = (IMdbxNativeDevicePasswordAccess)native;
            foreach (var (id, target) in new[] { (a, b), (b, a) })
            {
                await access.CreateDeviceSyncEntryAsync(id, root.ProjectId, "api-token", "Synthetic token",
                    new JsonObject
                    {
                        ["token"] = "synthetic-token",
                        ["device_sync_pending_relations"] = new JsonArray(new JsonObject
                        {
                            ["target"] = target,
                            ["kind"] = "com.monica.fixture.related",
                            ["payload"] = "{}",
                            ["version"] = 1
                        })
                    }.ToJsonString(), CancellationToken.None);
            }
            await access.ResolveDeviceSyncRelationsAsync(CancellationToken.None);
            var summary = await access.GetDeviceSyncSummaryAsync(a, CancellationToken.None);
            var payload = (await access.ReadDeviceSyncObjectAsync(summary!, CancellationToken.None))!.PayloadJson;
            var labels = new JsonArray(new JsonObject { ["name"] = "monica:api-token:fields:v1", ["payload"] = "{\"fields\":[{\"title\":\"Extra\",\"value\":\"synthetic\"}]}", ["version"] = 1 });
            await access.CommitDeviceSyncObjectAsync(new(a, root.ProjectId, "api-token", "Synthetic token", payload, summary, false, [], [], labels), CancellationToken.None);
        }
        await ExchangeAsync(first, second);
        using (var native = await bridge.OpenVaultAsync(second.Database.FilePath, second.Database.EncryptedPassword!, "metadata-fixture"))
        {
            var access = (IMdbxNativeDevicePasswordAccess)native;
            Assert.Equal(b, (await access.ReadDeviceSyncRelationsAsync(a, CancellationToken.None))[0]!["target"]!.GetValue<string>());
            Assert.Equal(a, (await access.ReadDeviceSyncRelationsAsync(b, CancellationToken.None))[0]!["target"]!.GetValue<string>());
            Assert.Contains("synthetic", (await access.ReadDeviceSyncLabelsAsync(a, CancellationToken.None))[0]!["payload"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Empty_and_nested_folders_and_preexisting_trash_are_synchronized()
    {
        await using var first = await Fixture.CreateAsync();
        await using var second = await Fixture.CreateAsync();
        var bridge = new MdbxUniffiNativeBridge();
        var parent = Guid.NewGuid().ToString(); var child = Guid.NewGuid().ToString(); var empty = Guid.NewGuid().ToString();
        var record = Guid.NewGuid().ToString();
        using (var native = await bridge.OpenVaultAsync(first.Database.FilePath, first.Database.EncryptedPassword!, "folder-fixture"))
        {
            await native.CreateProjectWithIdentityAsync(parent, "Parent", null);
            await native.CreateProjectWithIdentityAsync(child, "Child", parent);
            await native.CreateProjectWithIdentityAsync(empty, "Empty", parent);
            await ((IMdbxNativeDevicePasswordAccess)native).CreateDeviceSyncEntryAsync(record, child, "login", "Already in trash",
                new JsonObject { ["kind"] = "password", ["password_plain"] = "synthetic-trashed", ["is_archived"] = false }.ToJsonString(), CancellationToken.None);
            await native.DeleteEntryAsync(child, record);
        }
        await ExchangeAsync(first, second);
        using (var native = await bridge.OpenVaultAsync(second.Database.FilePath, second.Database.EncryptedPassword!, "folder-fixture"))
        {
            var projects = await ((IMdbxNativeDevicePasswordAccess)native).ListDeviceSyncProjectsAsync(CancellationToken.None);
            Assert.Equal(parent, Assert.Single(projects, p => p.ProjectId == child).ParentProjectId);
            Assert.Equal(parent, Assert.Single(projects, p => p.ProjectId == empty).ParentProjectId);
            var trash = Assert.Single(await native.ListDeletedEntriesAsync(child));
            Assert.Contains("synthetic-trashed", trash.PayloadJson);
        }
        Assert.Empty(await second.Repository.GetPasswordsAsync());
        Assert.Equal("synthetic-trashed", Assert.Single(await second.Repository.GetPasswordsAsync(includeDeleted: true)).Password);
    }
    [Theory]
    [InlineData(PasskeyAlgorithm.Es256)]
    [InlineData(PasskeyAlgorithm.Rs256)]
    [InlineData(PasskeyAlgorithm.Ps256)]
    public async Task Portable_passkeys_sync_protected_keys_and_produce_valid_signatures(int algorithm)
    {
        await using var first = await Fixture.CreateAsync();
        await using var second = await Fixture.CreateAsync();
        var material = PasskeyKeyMaterialGenerator.Generate(algorithm == PasskeyAlgorithm.Ps256 ? PasskeyAlgorithm.Rs256 : algorithm);
        var entry = new PasskeyEntry
        {
            CredentialId = Guid.NewGuid().ToString(),
            RpId = "example.com",
            RpName = "Fixture",
            UserId = Convert.ToBase64String("fixture-user"u8.ToArray()),
            UserName = "fixture",
            PublicKeyAlgorithm = algorithm,
            PublicKey = material.PublicKeySpkiBase64
        };
        await first.Passkeys.SaveAsync(entry, material.PrivateKeyPkcs8Base64);
        await ExchangeAsync(first, second);
        var received = Assert.Single(await second.Passkeys.ListAllAsync());
        Assert.StartsWith(PasskeyPrivateKeyRef.ReferencePrefix, received.PrivateKeyAlias);
        var privateKey = await second.Passkeys.ResolvePrivateKeyAsync(received);
        var challenge = RandomNumberGenerator.GetBytes(32);
        Assert.True(PasskeyKeyMaterialGenerator.Verify(algorithm, received.PublicKey, challenge,
            PasskeyKeyMaterialGenerator.Sign(algorithm, privateKey!, challenge)));
        await ExchangeAsync(second, first);
        Assert.Single(await first.Passkeys.ListAllAsync());
        await second.Passkeys.DeleteAsync(received.Id);
        await ExchangeAsync(second, first);
        Assert.Empty(await first.Passkeys.ListAllAsync());
    }

    [Fact]
    public async Task Multi_chunk_binary_attachments_sync_and_removal_is_applied()
    {
        await using var first = await Fixture.CreateAsync();
        await using var second = await Fixture.CreateAsync();
        var entry = new PasswordEntry { Title = "With attachment", Password = "synthetic-password" };
        await first.Repository.SavePasswordAsync(entry);
        entry = Assert.Single(await first.Repository.GetPasswordsAsync());
        var bytes = RandomNumberGenerator.GetBytes(2 * 1024 * 1024 + 17);
        await first.Vaults.SavePasswordAttachmentAsync(first.Database, entry, new Attachment { FileName = "synthetic.bin", ContentType = "application/octet-stream" }, bytes);
        var progress = new List<DeviceSyncProgress>();
        first.Provider.Progress += progress.Add;
        await ExchangeAsync(first, second);
        var received = Assert.Single(await second.Repository.GetPasswordsAsync());
        var files = await second.Vaults.GetPasswordAttachmentsByEntryIdsAsync(second.Database, [received.Id]);
        var attachment = Assert.Single(files[received.Id]);
        Assert.Equal(bytes, await second.Vaults.TryReadAttachmentContentAsync(second.Database, attachment));
        Assert.Contains(progress, p => p.TotalBytes == bytes.Length && p.Bytes > 0 && p.Bytes < p.TotalBytes);
        await second.Vaults.DeleteAttachmentAsync(second.Database, attachment);
        await ExchangeAsync(second, first);
        files = await first.Vaults.GetPasswordAttachmentsByEntryIdsAsync(first.Database, [entry.Id]);
        Assert.Empty(files.GetValueOrDefault(entry.Id, []));
    }
    [Fact]
    public async Task Standalone_authenticators_notes_and_wallet_items_sync_with_complete_payloads()
    {
        await using var first = await Fixture.CreateAsync();
        await using var second = await Fixture.CreateAsync();
        var itemData = new JsonObject
        {
            ["secret"] = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ",
            ["issuer"] = "Synthetic",
            ["accountName"] = "fixture",
            ["digits"] = 8,
            ["period"] = 30,
            ["algorithm"] = "SHA1",
            ["otpType"] = "TOTP",
            ["counter"] = 17,
            ["pin"] = "synthetic-pin",
            ["steamIdentitySecret"] = "synthetic-recovery",
            ["extension"] = new JsonObject { ["preserve"] = true }
        }.ToJsonString();
        var types = new[] { VaultItemType.Totp, VaultItemType.Note, VaultItemType.BankCard,
            VaultItemType.Document, VaultItemType.BillingAddress, VaultItemType.PaymentAccount };
        foreach (var type in types)
            await first.Repository.SaveSecureItemAsync(new SecureItem
            {
                ItemType = type,
                Title = $"Fixture {type}",
                Notes = "preserved note",
                ItemData = itemData
            });

        await ExchangeAsync(first, second);

        var received = await second.Repository.GetSecureItemsAsync();
        Assert.Equal(types.Length, received.Count);
        foreach (var type in types)
        {
            var item = Assert.Single(received, item => item.ItemType == type);
            Assert.Equal("preserved note", item.Notes);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(itemData), JsonNode.Parse(item.ItemData)));
        }
    }

    [Fact]
    public async Task Password_batches_larger_than_32_KiB_prepare_and_apply_without_invalid_request()
    {
        await using var first = await Fixture.CreateAsync();
        await using var second = await Fixture.CreateAsync();
        var notes = new string('n', 4096);
        for (var i = 0; i < 16; i++)
            await first.Repository.SavePasswordAsync(new PasswordEntry
            {
                Title = $"Batch {i}",
                Username = $"user-{i}",
                Password = $"synthetic-{i}",
                Notes = notes
            });

        var outgoing = await first.Provider.PrepareAsync(TestTempPaths.CreateDirectoryPath(), CancellationToken.None);
        Assert.True(new FileInfo(Path.Combine(outgoing.Directory, "changes.mdbxsync")).Length > 32 * 1024);
        var changed = await second.Provider.ApplyAsync(outgoing.Directory, Peer(outgoing), CancellationToken.None);

        Assert.True(changed >= 16U);
        var received = await second.Repository.GetPasswordsAsync();
        Assert.Equal(16, received.Count);
        Assert.All(received, entry => Assert.Equal(notes, entry.Notes));
        Assert.Equal(Enumerable.Range(0, 16).Select(i => $"synthetic-{i}").Order(), received.Select(entry => entry.Password).Order());
    }

    [Fact]
    public async Task Pairing_qr_is_available_before_password_content_can_be_read()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var session = new VaultSessionService();
        session.MarkUnlocked();
        var unavailable = new UnavailableData();
        await using var sync = new DeviceSyncCoordinator(fixture.Settings, unavailable, session,
            new UnicastOnlyNetwork(), transferRoot: TestTempPaths.CreateDirectoryPath());

        await sync.PairAsync();

        Assert.Equal("waiting", sync.State.Phase);
        Assert.Null(sync.State.Error);
        Assert.NotEmpty(sync.State.Invitations!);
        Assert.Equal(0, unavailable.Reads);
        Assert.Equal(0, unavailable.Applies);
    }

    [Fact]
    public async Task Pairing_remembers_devices_then_attempts_first_sync_without_claiming_it_succeeded()
    {
        await using var first = await Fixture.CreateAsync();
        await using var second = await Fixture.CreateAsync();
        using var firstSession = new VaultSessionService();
        using var secondSession = new VaultSessionService();
        firstSession.MarkUnlocked(); secondSession.MarkUnlocked();
        var firstData = new UnavailableData();
        var secondData = new UnavailableData();
        var network = new UnicastOnlyNetwork();
        await using var left = new DeviceSyncCoordinator(first.Settings, firstData, firstSession, network,
            transferRoot: TestTempPaths.CreateDirectoryPath());
        await using var right = new DeviceSyncCoordinator(second.Settings, secondData, secondSession, network,
            transferRoot: TestTempPaths.CreateDirectoryPath());
        await left.PairAsync();
        await right.PairAsync(Assert.Single(left.State.Invitations!));
        await PumpUntilAsync(left, right, () => !left.State.Pairing && !right.State.Pairing &&
            left.State.Peers.Count == 1 && right.State.Peers.Count == 1);

        Assert.Single((await first.Settings.LoadAsync()).Peers);
        Assert.Single((await second.Settings.LoadAsync()).Peers);
        Assert.Null(left.State.Peers[0].LastSyncedAt);
        Assert.Null(right.State.Peers[0].LastSyncedAt);
        Assert.Equal(0, firstData.Applies + secondData.Applies);
        Assert.True(firstData.Reads + secondData.Reads > 0);
    }

    private sealed class UnavailableData : IDeviceSyncDataProvider
    {
        public int Reads { get; private set; }
        public int Applies { get; private set; }
        public Task<string> GetRevisionAsync(CancellationToken token)
        {
            Reads++;
            throw new InvalidOperationException("Content is temporarily unavailable.");
        }
        public Task<DeviceSyncSnapshot> PrepareAsync(string directory, CancellationToken token) =>
            throw new InvalidOperationException("Content is temporarily unavailable.");
        public Task<uint> ApplyAsync(string directory, JsonElement peer, CancellationToken token)
        {
            Applies++;
            throw new InvalidOperationException("Content is temporarily unavailable.");
        }
        public Task PauseAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task Independent_local_vaults_sync_password_changes_without_switching_databases()
    {
        await using var first = await Fixture.CreateAsync();
        await using var second = await Fixture.CreateAsync();
        var alpha = new PasswordEntry { Title = "Alpha", Username = "a", Password = "synthetic-a" };
        var beta = new PasswordEntry { Title = "Beta", Username = "b", Password = "synthetic-b" };
        await first.Repository.SavePasswordAsync(alpha);
        await second.Repository.SavePasswordAsync(beta);
        await ExchangeAsync(first, second);
        Assert.Equal(2, (await first.Repository.GetPasswordsAsync()).Count);
        Assert.Equal(2, (await second.Repository.GetPasswordsAsync()).Count);
        Assert.NotEqual(first.Database.FilePath, second.Database.FilePath);

        var edited = (await first.Repository.GetPasswordsAsync()).Single(item => item.Title == "Alpha");
        edited.Password = "synthetic-new";
        await first.Repository.SavePasswordAsync(edited);
        var removed = (await second.Repository.GetPasswordsAsync()).Single(item => item.Title == "Beta");
        await second.Repository.SoftDeletePasswordAsync(removed.Id);
        await ExchangeAsync(first, second);

        foreach (var fixture in new[] { first, second })
        {
            var active = Assert.Single(await fixture.Repository.GetPasswordsAsync());
            Assert.Equal("Alpha", active.Title);
            Assert.Equal("synthetic-new", active.Password);
            Assert.Single(await fixture.Repository.GetPasswordsAsync(includeDeleted: true), item => item.IsDeleted);
            Assert.Equal(fixture.Database.Id, Assert.Single(await fixture.Repository.GetMdbxDatabasesAsync()).Id);
        }
        var stable = await first.Provider.GetRevisionAsync(CancellationToken.None);
        await ExchangeAsync(first, second);
        Assert.Equal(stable, await first.Provider.GetRevisionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Remote_apply_cannot_overwrite_a_local_edit_after_the_last_snapshot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var entry = new PasswordEntry { Title = "Login", Password = "synthetic-old" };
        await fixture.Repository.SavePasswordAsync(entry);
        var before = Assert.Single(await fixture.Vaults.ReadDevicePasswordsAsync(fixture.Database, CancellationToken.None), p => p.Data["entry_type"]?.GetValue<string>() == "login");
        entry.Password = "synthetic-later-local";
        await fixture.Repository.SavePasswordAsync(entry);
        var incoming = before.Data.DeepClone().AsObject();
        incoming["password_plain"] = "synthetic-remote";
        var applied = await fixture.Vaults.TryApplyDevicePasswordAsync(fixture.Database, before.GlobalId,
            before.LocalId, before.Guard, false, incoming, CancellationToken.None);
        Assert.Null(applied);
        Assert.Equal("synthetic-later-local", Assert.Single(await fixture.Repository.GetPasswordsAsync()).Password);
    }

    [Fact]
    public async Task Paired_devices_reconnect_after_restart_and_apply_offline_edits_without_pairing_again()
    {
        await using var first = await Fixture.CreateAsync();
        await using var second = await Fixture.CreateAsync();
        using var firstSession = new VaultSessionService();
        using var secondSession = new VaultSessionService();
        firstSession.MarkUnlocked(); secondSession.MarkUnlocked();
        var network = new LoopbackNetwork();
        await using var left = Coordinator(first, firstSession, network);
        await using var right = Coordinator(second, secondSession, network);
        var alpha = new PasswordEntry { Title = "Alpha", Password = "synthetic-start" };
        await first.Repository.SavePasswordAsync(alpha);
        await left.PairAsync();
        await right.PairAsync(network.Invitation);
        await PumpUntilAsync(left, right, () => left.State.Peers.Count == 1 && right.State.Peers.Count == 1 &&
            left.State.Peers[0].LastSyncedAt is not null && right.State.Peers[0].LastSyncedAt is not null);
        Assert.Equal("synthetic-start", Assert.Single(await second.Repository.GetPasswordsAsync()).Password);
        var pairKey = (await first.Settings.LoadAsync()).Peers.Single().TrustedKey;
        Assert.NotEqual(network.EnrollmentCode, pairKey);

        await left.DisposeAsync();
        network.Offline = true;
        var changed = Assert.Single(await second.Repository.GetPasswordsAsync());
        changed.Password = "synthetic-offline-edit";
        await second.Repository.SavePasswordAsync(changed);
        await right.TickAsync();

        await using var restarted = Coordinator(first, firstSession, network);
        network.Offline = false;
        await PumpUntilAsync(restarted, right, async () =>
            (await first.Repository.GetPasswordsAsync()).Any(item => item.Password == "synthetic-offline-edit"));
        Assert.Equal(pairKey, (await first.Settings.LoadAsync()).Peers.Single().TrustedKey);
        Assert.Equal(first.Database.Id, Assert.Single(await first.Repository.GetMdbxDatabasesAsync()).Id);
        Assert.Equal(second.Database.Id, Assert.Single(await second.Repository.GetMdbxDatabasesAsync()).Id);
        Assert.Equal(2, network.Enrollments);
    }

    [Fact]
    public async Task Receiving_waits_for_an_editor_and_locking_releases_the_session_then_resumes()
    {
        await using var first = await Fixture.CreateAsync();
        await using var second = await Fixture.CreateAsync();
        using var firstSession = new VaultSessionService();
        using var secondSession = new VaultSessionService();
        firstSession.MarkUnlocked(); secondSession.MarkUnlocked();
        var network = new LoopbackNetwork();
        await using var left = Coordinator(first, firstSession, network);
        await using var right = Coordinator(second, secondSession, network);
        await first.Repository.SavePasswordAsync(new PasswordEntry { Title = "Waiting", Password = "synthetic" });
        right.CanApply = () => false;
        await left.PairAsync();
        await right.PairAsync(network.Invitation);
        await PumpUntilAsync(left, right, () => network.HasContentReceived);
        Assert.Empty(await second.Repository.GetPasswordsAsync());
        right.CanApply = () => true;
        await PumpUntilAsync(left, right, () => left.State.Peers.Count == 1 && right.State.Peers.Count == 1 &&
            left.State.Peers[0].LastSyncedAt is not null && right.State.Peers[0].LastSyncedAt is not null);
        Assert.Single(await second.Repository.GetPasswordsAsync());

        firstSession.MarkLocked();
        await left.TickAsync();
        Assert.Equal("locked", left.State.Phase);
        var updated = Assert.Single(await second.Repository.GetPasswordsAsync());
        updated.Password = "synthetic-after-lock";
        await second.Repository.SavePasswordAsync(updated);
        firstSession.MarkUnlocked();
        await PumpUntilAsync(left, right, async () =>
            (await first.Repository.GetPasswordsAsync()).Any(item => item.Password == "synthetic-after-lock"));
    }

    [Fact]
    public async Task Native_rich_fields_survive_a_password_only_round_trip_and_invalid_content_is_rejected()
    {
        await using var first = await Fixture.CreateAsync();
        await using var second = await Fixture.CreateAsync();
        var entry = new PasswordEntry { Title = "Rich login", Password = "synthetic", LoginType = PasswordLoginType.SshKey, SshKeyData = "synthetic-ssh-json" };
        await first.Repository.SavePasswordAsync(entry);
        await first.Repository.ReplaceCustomFieldsAsync(entry.Id, [
            new CustomField { EntryId = entry.Id, Title = "Protected", Value = "synthetic-field", IsProtected = true, SortOrder = 2 }
        ]);
        await ExchangeAsync(first, second);
        var received = Assert.Single(await second.Repository.GetPasswordsAsync());
        Assert.Equal("synthetic-ssh-json", received.SshKeyData);
        var fields = await second.Repository.GetCustomFieldsAsync(received.Id);
        Assert.Contains(fields, field => field.Title == "Protected" && field.Value == "synthetic-field" && field.IsProtected);
        var before = Assert.Single(await second.Vaults.ReadDevicePasswordsAsync(second.Database, CancellationToken.None), p => p.Data["entry_type"]?.GetValue<string>() is "login" or "ssh-key");
        var malformed = before.Data.DeepClone().AsObject();
        malformed["password_plain"] = new JsonObject { ["unexpected"] = true };
        await Assert.ThrowsAsync<InvalidDataException>(() => second.Vaults.TryApplyDevicePasswordAsync(second.Database, before.GlobalId,
            before.LocalId, before.Guard, false, malformed, CancellationToken.None));
        Assert.Equal("synthetic", Assert.Single(await second.Repository.GetPasswordsAsync()).Password);
    }

    private static DeviceSyncCoordinator Coordinator(Fixture fixture, IVaultSessionService session, IDeviceSyncTransport network) =>
        new(fixture.Settings, fixture.Provider, session, network, transferRoot: TestTempPaths.CreateDirectoryPath());

    [Fact]
    public async Task Remembered_devices_reconnect_with_real_unicast_discovery_when_multicast_is_disabled()
    {
        await using var first = await Fixture.CreateAsync();
        await using var second = await Fixture.CreateAsync();
        using var firstSession = new VaultSessionService();
        using var secondSession = new VaultSessionService();
        firstSession.MarkUnlocked(); secondSession.MarkUnlocked();
        var network = new UnicastOnlyNetwork();
        await using var left = Coordinator(first, firstSession, network);
        await using var right = Coordinator(second, secondSession, network);
        await first.Repository.SavePasswordAsync(new PasswordEntry { Title = "Unicast", Password = "synthetic-start" });
        await left.PairAsync();
        await right.PairAsync(Assert.Single(left.State.Invitations!));
        await PumpUntilAsync(left, right, () => left.State.Peers.Count == 1 && right.State.Peers.Count == 1 &&
            left.State.Peers[0].LastSyncedAt is not null && right.State.Peers[0].LastSyncedAt is not null);
        Assert.Equal("127.0.0.1", Assert.Single((await first.Settings.LoadAsync()).Peers).Address);
        await left.DisposeAsync();
        var changed = Assert.Single(await second.Repository.GetPasswordsAsync());
        changed.Password = "synthetic-unicast-update";
        await second.Repository.SavePasswordAsync(changed);
        await using var restarted = Coordinator(first, firstSession, network);
        await PumpUntilAsync(restarted, right, async () =>
            (await first.Repository.GetPasswordsAsync()).Any(item => item.Password == "synthetic-unicast-update"));
        Assert.True(network.KnownAddressProbes > 0);
    }

    // Use the real UDP discovery and TCP transport, with multicast explicitly disabled.
    private sealed class UnicastOnlyNetwork : IDeviceSyncTransport
    {
        public int KnownAddressProbes { get; private set; }
        public JsonElement Request(object request)
        {
            var input = JsonSerializer.SerializeToNode(request)!.AsObject();
            var operation = input["operation"]!.GetValue<string>();
            if (operation == "start") input["listen_address"] = "127.0.0.1";
            if (operation == "discover")
            {
                input["multicast"] = false;
                if (input["targets"] is JsonArray { Count: > 0 }) KnownAddressProbes++;
            }
            return LanSyncNative.Request(input);
        }
    }

    private static Task PumpUntilAsync(DeviceSyncCoordinator left, DeviceSyncCoordinator right, Func<bool> condition) =>
        PumpUntilAsync(left, right, () => Task.FromResult(condition()));

    private static async Task PumpUntilAsync(DeviceSyncCoordinator left, DeviceSyncCoordinator right, Func<Task<bool>> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(35), $"Sync timed out: {left.State.Phase}/{left.State.Error}; {right.State.Phase}/{right.State.Error}");
            await left.TickAsync();
            await right.TickAsync();
            await Task.Delay(20);
        }
    }

    /// Only discovery is replaced. Pairing, TCP, encryption, snapshots and writes use the real native libraries.
    private sealed class LoopbackNetwork : IDeviceSyncTransport
    {
        private readonly Dictionary<ulong, object> _offers = [];
        public bool Offline { get; set; }
        public bool HasContentReceived { get; private set; }
        public int Enrollments { get; private set; }
        public string Invitation { get; private set; } = "";
        public string EnrollmentCode { get; private set; } = "";

        public JsonElement Request(object request)
        {
            var input = JsonSerializer.SerializeToElement(request);
            var operation = input.GetProperty("operation").GetString();
            if (operation == "discover")
                return JsonSerializer.SerializeToElement(new { devices = Offline ? [] : _offers.Values.ToArray() });
            var result = LanSyncNative.Request(request);
            if (operation is "start" or "connect")
            {
                var device = input.GetProperty("device");
                var trusted = input.TryGetProperty("expected_device_id", out var expected) && expected.ValueKind == JsonValueKind.String;
                if (!trusted) Enrollments++;
                if (operation == "start")
                {
                    var handle = result.GetProperty("handle").GetUInt64();
                    var port = result.GetProperty("port").GetInt32();
                    var code = result.GetProperty("code").GetString()!;
                    if (!trusted)
                    {
                        EnrollmentCode = code;
                        Invitation = $"monica-lan:2:127.0.0.1:{port}:{code}";
                    }
                    _offers[handle] = new
                    {
                        device_id = device.GetProperty("device_id").GetString(),
                        session = device.GetProperty("pair_id").GetString(),
                        name = device.GetProperty("name").GetString(),
                        trusted,
                        address = "127.0.0.1",
                        port,
                        revision = input.GetProperty("revision").GetString()
                    };
                }
            }
            if (operation is "cancel" or "release") _offers.Remove(input.GetProperty("handle").GetUInt64());
            if (operation == "progress")
            {
                var phase = result.GetProperty("phase").GetString();
                if (phase == "received" && result.GetProperty("peer").GetProperty("revision").GetString() != DevicePairingSnapshot.Revision)
                    HasContentReceived = true;
                if (phase != "waiting") _offers.Remove(input.GetProperty("handle").GetUInt64());
            }
            return result;
        }
    }

    private static async Task ExchangeAsync(Fixture first, Fixture second)
    {
        for (var round = 0; round < 3; round++)
        {
            var left = await first.Provider.PrepareAsync(TestTempPaths.CreateDirectoryPath(), CancellationToken.None);
            var right = await second.Provider.PrepareAsync(TestTempPaths.CreateDirectoryPath(), CancellationToken.None);
            await second.Provider.ApplyAsync(left.Directory, Peer(left), CancellationToken.None);
            await first.Provider.ApplyAsync(right.Directory, Peer(right), CancellationToken.None);
        }
    }

    [Fact]
    public async Task Reading_native_passwords_does_not_advertise_a_new_content_revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Repository.SavePasswordAsync(new PasswordEntry { Title = "Stable", Password = "synthetic" });
        var before = await fixture.Vaults.GetLanRevisionAsync(fixture.Database, CancellationToken.None);
        await fixture.Vaults.ReadDevicePasswordsAsync(fixture.Database, CancellationToken.None);
        await fixture.Vaults.ReadDevicePasswordsAsync(fixture.Database, CancellationToken.None);
        Assert.Equal(before, await fixture.Vaults.GetLanRevisionAsync(fixture.Database, CancellationToken.None));
    }

    [Fact]
    public async Task Legacy_wrapped_passwords_are_read_and_updated_without_clearing_the_secret()
    {
        await using var first = await Fixture.CreateAsync();
        await using var second = await Fixture.CreateAsync();
        var bridge = new MdbxUniffiNativeBridge();
        using (var native = await bridge.OpenVaultAsync(first.Database.FilePath, first.Database.EncryptedPassword!, "legacy-fixture"))
        {
            var project = await native.CreateProjectAsync("Legacy");
            var payload = JsonSerializer.Serialize(new
            {
                kind = "password",
                schemaVersion = 1,
                data = new
                {
                    entry = new
                    {
                        id = 42,
                        title = "Legacy",
                        username = "legacy-user",
                        password = "synthetic-legacy",
                        loginType = 0,
                        wifiMetadata = new { ssid = "synthetic-wifi" },
                        creditCardCVV = "synthetic-cvv"
                    },
                    customFields = new[] { new { id = 1, entryId = 42, title = "Protected", value = "synthetic-field", isProtected = true, sortOrder = 0 } }
                }
            });
            await native.CreateEntryAsync(project.ProjectId, "login", "Legacy", payload);
        }
        await ExchangeAsync(first, second);
        var received = Assert.Single(await second.Repository.GetPasswordsAsync());
        Assert.Equal("synthetic-legacy", received.Password);
        Assert.Equal("legacy-user", received.Username);
        var portable = Assert.Single(await second.Vaults.ReadDevicePasswordsAsync(second.Database, CancellationToken.None), p => p.Data["entry_type"]?.GetValue<string>() == "login");
        Assert.Equal("synthetic-wifi", JsonNode.Parse(portable.Data["wifi_metadata"]!.GetValue<string>())!["ssid"]!.GetValue<string>());
        Assert.Equal("synthetic-cvv", portable.Data["credit_card_cvv_plain"]!.GetValue<string>());
        received.Password = "synthetic-updated";
        await second.Repository.SavePasswordAsync(received);
        await ExchangeAsync(first, second);
        Assert.Equal("synthetic-updated", Assert.Single(await first.Repository.GetPasswordsAsync()).Password);
    }

    private static JsonElement Peer(DeviceSyncSnapshot snapshot) => JsonSerializer.SerializeToElement(new
    { pairing = new { content_key = snapshot.ContentKey } });

    private sealed class Fixture : IAsyncDisposable
    {
        public required IMonicaRepository Repository { get; init; }
        public required LocalMdbxDatabase Database { get; init; }
        public required MdbxVaultStore Vaults { get; init; }
        public required DevicePasswordSyncProvider Provider { get; init; }
        public required DeviceSyncPreferenceStore Settings { get; init; }
        public required IPasskeyStore Passkeys { get; init; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = TestTempPaths.CreateDirectoryPath();
            var bridge = new MdbxUniffiNativeBridge();
            var database = new LocalMdbxDatabase
            {
                Name = "Personal",
                FilePath = Path.Combine(directory, "personal.mdbx"),
                EncryptedPassword = "synthetic-native-password",
                IsDefault = true,
                IsOfflineAvailable = true
            };
            database.WorkingCopyPath = database.FilePath;
            using (await bridge.CreateVaultAsync(database.FilePath, database.EncryptedPassword, Guid.NewGuid().ToString(), MdbxTigaMode.Multi)) { }
            var factory = new SqliteConnectionFactory(Path.Combine(directory, "metadata.db"));
            var inner = new MonicaRepository(factory, new DatabaseMigrator(factory));
            var crypto = new CryptoService();
            crypto.InitializeSession("synthetic-session", new byte[16]);
            var keys = new PasskeyStore(factory, new DatabaseMigrator(factory), new PasskeyPrivateKeyStore(factory, new DatabaseMigrator(factory), crypto));
            var vaults = new MdbxVaultStore(bridge);
            var repository = new MdbxBackedMonicaRepository(inner, vaults);
            await repository.SaveMdbxDatabaseAsync(database);
            var settings = new DeviceSyncPreferenceStore(new FixtureProtector(), Path.Combine(directory, "peers.protected"));
            await settings.SaveAsync(new DeviceSyncPreferences());
            return new Fixture
            {
                Repository = repository,
                Database = database,
                Vaults = vaults,
                Settings = settings,
                Passkeys = keys,
                Provider = new DevicePasswordSyncProvider(repository, vaults, settings, Path.Combine(directory, "journal.replica"), keys)
            };
        }
        public async ValueTask DisposeAsync() { await Provider.PauseAsync(); Vaults.Dispose(); }
    }

    private sealed class FixtureProtector : ISecretProtector
    {
        public PlatformIntegrationCapability Capability => new(PlatformFeatureKeys.SecretProtection, PlatformFeatureStatus.Available, "Fixture");
        public Task<string> ProtectAsync(string value, CancellationToken token = default) => Task.FromResult(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
        public Task<string> UnprotectAsync(string value, CancellationToken token = default) => Task.FromResult(Encoding.UTF8.GetString(Convert.FromBase64String(value)));
    }
}
