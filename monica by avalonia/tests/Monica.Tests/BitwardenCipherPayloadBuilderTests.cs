using System.Text;
using System.Text.Json;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Platform.Bitwarden;

namespace Monica.Tests;

public sealed class BitwardenCipherPayloadBuilderTests
{
    private const string Title = "Round Trip Mail";
    private const string Website = "https://mail.example.test";
    private const string Username = "roundtrip@example.test";
    private const string Secret = "RoundTrip-Secret-42";
    private const string Notes = "written back by Monica";
    private const string TotpSeed = "JBSWY3DPEHPK3PXP";
    private const string FieldName = "PIN";
    private const string FieldValue = "4-1-9-2";
    private const string HistorySecret = "RoundTrip-Old-Secret-7";
    private const string PasskeyId = "dQw4w9WgXcQ-credential";
    private const string PasskeyPublicKey = "pQ8a3Ym5cZ0-fido-public";
    private const string PasskeyRpId = "https://mail.example.test";
    private const string PasskeyUserHandle = "user-handle-07";

    private static readonly DateTimeOffset HistoryAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    [Fact]
    public void Written_back_cipher_decodes_back_to_the_same_entry()
    {
        using var key = TestKey();
        var entry = LoginEntry();
        var fields = new List<CustomField>
        {
            new() { Title = FieldName, Value = FieldValue, IsProtected = true, SortOrder = 0 }
        };
        var history = new List<PasswordHistoryEntry>
        {
            new() { Password = HistorySecret, LastUsedAt = HistoryAt }
        };

        var decoded = Decode(key, entry, fields, history);

        var back = decoded.Password!;
        Assert.Equal(entry.Title, back.Title);
        Assert.Equal(entry.Website, back.Website);
        Assert.Equal(entry.Username, back.Username);
        Assert.Equal(entry.Password, back.Password);
        Assert.Equal(entry.Notes, back.Notes);
        Assert.Equal(entry.AuthenticatorKey, back.AuthenticatorKey);
        Assert.True(back.IsFavorite);
        Assert.Equal(entry.BitwardenFolderId, back.BitwardenFolderId);
        Assert.Equal(entry.BitwardenCipherType, back.BitwardenCipherType);
        Assert.Equal(FieldValue, Assert.Single(decoded.CustomFields).Value);
        Assert.Equal(HistorySecret, Assert.Single(decoded.PasswordHistory).Password);
        Assert.Equal(HistoryAt, Assert.Single(decoded.PasswordHistory).LastUsedAt);

        // The merge engine decides "did this change?" purely from this hash, so an unchanged
        // local edit must reproduce the hash the pull produced or every sync looks like a conflict.
        Assert.Equal(
            BitwardenPayloadFingerprint.ForPassword(entry, fields, history),
            decoded.Metadata.PayloadHash);
    }

    [Fact]
    public void Passkey_bindings_survive_the_write_back()
    {
        using var key = TestKey();
        var entry = LoginEntry();
        entry.PasskeyBindings = DecoderShapedPasskeys();

        var decoded = Decode(key, entry, [], []);

        Assert.Equal(entry.PasskeyBindings, decoded.Password!.PasskeyBindings);
    }

    [Fact]
    public void Payload_never_carries_a_plain_field_value()
    {
        using var key = TestKey();
        var entry = LoginEntry();
        entry.PasskeyBindings = DecoderShapedPasskeys();
        var json = BitwardenCipherPayloadBuilder.BuildLoginCipher(
            entry,
            key,
            [new CustomField { Title = FieldName, Value = FieldValue, IsProtected = true }],
            [new PasswordHistoryEntry { Password = HistorySecret, LastUsedAt = HistoryAt }]);

        foreach (var secret in new[]
                 {
                     Title, Website, Username, Secret, Notes, TotpSeed, FieldName, FieldValue,
                     HistorySecret, PasskeyId, PasskeyPublicKey, PasskeyRpId, PasskeyUserHandle
                 })
        {
            Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        }

        // Everything that reached the server must still be a type-2 cipher string.
        var cipherStrings = CountType2Strings(JsonDocument.Parse(json).RootElement);
        Assert.True(cipherStrings >= 12, $"expected the payload to hold only cipher strings, saw {cipherStrings}");
    }

    private static int CountType2Strings(JsonElement element)
    {
        var total = 0;
        foreach (var property in element.EnumerateObject())
        {
            total += property.Value.ValueKind switch
            {
                JsonValueKind.String when property.Value.GetString()!.StartsWith("2.", StringComparison.Ordinal) => 1,
                JsonValueKind.Array => property.Value.EnumerateArray().Sum(CountType2Strings),
                JsonValueKind.Object => CountType2Strings(property.Value),
                _ => 0
            };
        }

        return total;
    }

    private static int CountType2Strings(IEnumerable<JsonElement> elements) =>
        elements.Sum(CountType2Strings);

    [Fact]
    public void Only_the_fields_the_decoder_reads_are_written_out()
    {
        using var key = TestKey();
        var json = BitwardenCipherPayloadBuilder.BuildLoginCipher(LoginEntry(), key);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("type").GetInt32());
        Assert.False(root.TryGetProperty("id", out _));
        Assert.False(root.TryGetProperty("revisionDate", out _));
        Assert.False(root.TryGetProperty("attachments", out _));
        Assert.False(root.GetProperty("login").TryGetProperty("passwordRevisionDate", out _));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Anything_but_a_login_cipher_refuses_to_be_written_back(int cipherType)
    {
        using var key = TestKey();
        var entry = LoginEntry();
        entry.BitwardenCipherType = cipherType;

        Assert.Throws<BitwardenProtocolException>(
            () => BitwardenCipherPayloadBuilder.BuildLoginCipher(entry, key));
    }

    [Fact]
    public void Local_shapes_bitwarden_has_no_home_for_refuse_to_be_written_back()
    {
        using var key = TestKey();

        var ssh = LoginEntry();
        ssh.LoginType = PasswordLoginType.SshKey;
        Assert.Throws<BitwardenProtocolException>(
            () => BitwardenCipherPayloadBuilder.BuildLoginCipher(ssh, key));

        var attached = LoginEntry();
        attached.HasAttachments = true;
        Assert.Throws<BitwardenProtocolException>(
            () => BitwardenCipherPayloadBuilder.BuildLoginCipher(attached, key));

        var trashed = LoginEntry();
        trashed.IsDeleted = true;
        Assert.Throws<BitwardenProtocolException>(
            () => BitwardenCipherPayloadBuilder.BuildLoginCipher(trashed, key));

        var nameless = LoginEntry();
        nameless.Title = "   ";
        Assert.Throws<BitwardenProtocolException>(
            () => BitwardenCipherPayloadBuilder.BuildLoginCipher(nameless, key));

        var blankField = LoginEntry();
        Assert.Throws<BitwardenProtocolException>(
            () => BitwardenCipherPayloadBuilder.BuildLoginCipher(
                blankField,
                key,
                [new CustomField { Title = " ", Value = FieldValue }]));
    }

    [Fact]
    public void Malformed_passkey_bindings_refuse_instead_of_erasing_the_remote_credential()
    {
        using var key = TestKey();
        var entry = LoginEntry();
        entry.PasskeyBindings = """[{"credentialId":7}]""";

        Assert.Throws<BitwardenProtocolException>(
            () => BitwardenCipherPayloadBuilder.BuildLoginCipher(entry, key));
    }

    [Fact]
    public void Missing_passkey_identity_refuses_instead_of_pushing_a_credentialless_entry()
    {
        using var key = TestKey();
        var entry = LoginEntry();
        entry.PasskeyBindings = DecoderShapedPasskeys().Replace(PasskeyId, "");

        Assert.Throws<BitwardenProtocolException>(
            () => BitwardenCipherPayloadBuilder.BuildLoginCipher(entry, key));
    }

    [Fact]
    public void A_note_only_entry_still_round_trips()
    {
        using var key = TestKey();
        var entry = new PasswordEntry
        {
            Title = Title,
            BitwardenVaultId = 1,
            BitwardenCipherId = "cipher-1",
            BitwardenCipherType = 1,
            IsFavorite = false
        };

        var decoded = Decode(key, entry, [], []);

        Assert.Equal(Title, decoded.Password!.Title);
        Assert.Equal("", decoded.Password.Website);
        Assert.Equal("", decoded.Password.Username);
        Assert.Equal("", decoded.Password.Password);
        Assert.Equal("", decoded.Password.AuthenticatorKey);
        Assert.Equal("", decoded.Password.PasskeyBindings);
        Assert.Empty(decoded.CustomFields);
    }

    private static BitwardenDecodedCipher Decode(
        BitwardenSymmetricKey key,
        PasswordEntry entry,
        IReadOnlyList<CustomField> fields,
        IReadOnlyList<PasswordHistoryEntry> history)
    {
        var json = BitwardenCipherPayloadBuilder.BuildLoginCipher(entry, key, fields, history);
        var cipher = BitwardenHttpContent.Deserialize<VaultCipherDto>(Encoding.UTF8.GetBytes(json)) with
        {
            Id = "cipher-1",
            RevisionDate = "2026-03-04T05:06:07.000Z"
        };
        var dto = new VaultSyncDto
        {
            Profile = new VaultProfileDto { Id = "user-1", Name = "Round Trip" },
            Ciphers = [cipher]
        };

        return new BitwardenCipherDecoder(key)
            .Decode(dto, HistoryAt)
            .DecodedCiphers
            .Single();
    }

    private static PasswordEntry LoginEntry() => new()
    {
        Title = Title,
        Website = Website,
        Username = Username,
        Password = Secret,
        Notes = Notes,
        AuthenticatorKey = TotpSeed,
        IsFavorite = true,
        BitwardenVaultId = 1,
        BitwardenCipherId = "cipher-1",
        BitwardenFolderId = "folder-1",
        BitwardenCipherType = 1,
        BitwardenLocalModified = true
    };

    private static string DecoderShapedPasskeys() =>
        JsonSerializer.Serialize(
            new[]
            {
                new
                {
                    credentialId = PasskeyId,
                    keyType = "public-key",
                    keyAlgorithm = "ECDSA",
                    keyCurve = "P-256",
                    keyValue = PasskeyPublicKey,
                    rpId = PasskeyRpId,
                    rpName = "Example Mail",
                    counter = "17",
                    userHandle = PasskeyUserHandle,
                    userName = Username,
                    userDisplayName = Username,
                    discoverable = "true",
                    creationDate = "2026-03-04T05:06:07.000Z"
                }
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static BitwardenSymmetricKey TestKey()
    {
        var encryption = new byte[BitwardenSymmetricKey.ComponentSize];
        var mac = new byte[BitwardenSymmetricKey.ComponentSize];
        for (var index = 0; index < encryption.Length; index++)
        {
            encryption[index] = (byte)(index + 1);
            mac[index] = (byte)(200 - index);
        }

        return new BitwardenSymmetricKey(encryption, mac);
    }
}
