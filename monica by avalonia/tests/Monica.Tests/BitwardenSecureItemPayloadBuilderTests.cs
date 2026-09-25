using System.Text;
using System.Text.Json;
using Monica.Core.Bitwarden;
using Monica.Core.Models;
using Monica.Platform.Bitwarden;

namespace Monica.Tests;

/// <summary>
/// Proof that a note, card or identity edited on this device can be written to Bitwarden and read back
/// unchanged. Every test starts from a cipher the real decoder pulled, because that - not a fixture typed
/// by hand - is the only local state a write is expected to survive, and it ends by feeding the emitted
/// payload back through the same decoder.
/// </summary>
public sealed class BitwardenSecureItemPayloadBuilderTests
{
    private const string CipherId = "cipher-secure-1";
    private const string FirstRevision = "2026-03-04T05:06:07.000Z";
    private const string SecondRevision = "2026-03-05T05:06:07.000Z";
    private const string NoteTitle = "Server note";
    private const string NoteBody = "first line\nsecond line";
    private const string CardNumber = "4111111111111111";
    private const string Cardholder = "Ada Lovelace";
    private const string Brand = "Visa Classic";
    private const string PassportNumber = "P-77-42";
    private const string IdentityName = "Ada Lovelace";
    private const string IdentityCity = "London";
    private const string IdentityCountry = "GB";
    private const string IdentityAddress = "12 Example Street";

    private static readonly DateTimeOffset Now = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    [Fact]
    public void An_edited_note_written_back_decodes_into_the_same_row()
    {
        using var key = TestKey();
        var note = Pull(key, NoteCipher(key)).SecureItem!;
        SaveNote(note, "Renamed on this device", $"{note.Notes}\nwritten here");

        var again = Pull(key, AsRemoteCipher(BitwardenCipherPayloadBuilder.BuildSecureItemCipher(note, key)));

        Assert.Equal(BitwardenPayloadFingerprint.ForSecureItem(note), again.Metadata.PayloadHash);
        var item = again.SecureItem!;
        Assert.Equal(VaultItemType.Note, item.ItemType);
        Assert.Equal("Renamed on this device", item.Title);
        Assert.Equal(note.ItemData, item.ItemData);
        Assert.Equal("[]", item.ImagePaths);
    }

    [Fact]
    public void An_edited_card_written_back_decodes_into_the_same_row()
    {
        using var key = TestKey();
        var card = Pull(key, CardCipher(key)).SecureItem!;
        var data = WalletItemDataCodec.DecodeBankCard(card);
        data.CardNumber = "5500005555555559";
        data.ExpiryMonth = "07";
        card.ItemData = WalletItemDataCodec.EncodeBankCard(data);

        var again = Pull(key, AsRemoteCipher(BitwardenCipherPayloadBuilder.BuildSecureItemCipher(card, key)));

        Assert.Equal(BitwardenPayloadFingerprint.ForSecureItem(card), again.Metadata.PayloadHash);
        var back = WalletItemDataCodec.DecodeBankCard(again.SecureItem!);
        Assert.Equal("5500005555555559", back.CardNumber);
        Assert.Equal("07", back.ExpiryMonth);
        Assert.Equal(Brand, back.Brand);
        Assert.Equal(Cardholder, back.CardholderName);
    }

    [Fact]
    public void An_edited_identity_written_back_keeps_the_details_the_pull_handed_over()
    {
        using var key = TestKey();
        var identity = Pull(key, IdentityCipher(key)).SecureItem!;
        var data = WalletItemDataCodec.DecodeDocument(identity);
        data.DocumentNumber = "P-99-01";
        identity.ItemData = WalletItemDataCodec.EncodeDocument(data);
        identity.Title = "Renewed passport";

        var again = Pull(key, AsRemoteCipher(BitwardenCipherPayloadBuilder.BuildSecureItemCipher(identity, key)));

        Assert.Equal(BitwardenPayloadFingerprint.ForSecureItem(identity), again.Metadata.PayloadHash);
        var back = WalletItemDataCodec.DecodeDocument(again.SecureItem!);
        Assert.Equal("P-99-01", back.DocumentNumber);
        Assert.Equal("PASSPORT", back.DocumentTypeString);
        Assert.Equal(IdentityName, back.FullName);
        Assert.Equal(IdentityCountry, back.Nationality);
        // The details block is the only home Bitwarden gives these, so it has to come back line for line.
        Assert.Contains($"City: {IdentityCity}", back.AdditionalInfo, StringComparison.Ordinal);
        Assert.Contains($"Address: {IdentityAddress}", back.AdditionalInfo, StringComparison.Ordinal);
    }

    [Fact]
    public void A_secure_item_payload_carries_cipher_strings_and_no_plain_value()
    {
        using var key = TestKey();
        var card = Pull(key, CardCipher(key)).SecureItem!;

        var json = BitwardenCipherPayloadBuilder.BuildSecureItemCipher(card, key);

        Assert.DoesNotContain(CardNumber, json, StringComparison.Ordinal);
        Assert.DoesNotContain(Cardholder, json, StringComparison.Ordinal);
        Assert.Contains("\"type\":3", json, StringComparison.Ordinal);
        Assert.False(JsonDocument.Parse(json).RootElement.TryGetProperty("id", out _));
        Assert.False(JsonDocument.Parse(json).RootElement.TryGetProperty("revisionDate", out _));
        AssertOnlyCipherStrings(json);
    }

    // The library asks this question without a vault key to decide what to offer for upload, so it must
    // never disagree with the encoder that actually writes. The three refusal theories above prove the
    // other direction, one row per shape Bitwarden has no field for.
    [Fact]
    public void The_publish_question_agrees_with_the_encoder_for_every_pulled_shape()
    {
        using var key = TestKey();

        Assert.True(BitwardenCipherPayloadBuilder.CanEncode(Pull(key, NoteCipher(key)).SecureItem!));
        Assert.True(BitwardenCipherPayloadBuilder.CanEncode(Pull(key, CardCipher(key)).SecureItem!));
        Assert.True(BitwardenCipherPayloadBuilder.CanEncode(Pull(key, IdentityCipher(key)).SecureItem!));
    }

    [Theory]
    [InlineData("tags")]
    [InlineData("markdown")]
    [InlineData("attachment-list")]
    [InlineData("stale-notes-column")]
    public void A_note_holding_what_bitwarden_has_no_field_for_refuses(string shape)
    {
        using var key = TestKey();
        var note = Pull(key, NoteCipher(key)).SecureItem!;
        switch (shape)
        {
            case "tags":
                SaveNote(note, note.Title, note.Notes, tags: "urgent, personal");
                break;
            case "markdown":
                SaveNote(note, note.Title, note.Notes, isMarkdown: true);
                break;
            case "attachment-list":
                note.ImagePaths = """["front.jpg"]""";
                break;
            case "stale-notes-column":
                note.Notes += "   ";
                break;
        }

        Assert.False(BitwardenCipherPayloadBuilder.CanEncode(note));
        Assert.Throws<BitwardenProtocolException>(
            () => BitwardenCipherPayloadBuilder.BuildSecureItemCipher(note, key));
    }

    [Theory]
    [InlineData("billing-address")]
    [InlineData("nickname")]
    [InlineData("debit")]
    [InlineData("bank-name")]
    [InlineData("images")]
    public void A_card_holding_what_bitwarden_has_no_field_for_refuses(string shape)
    {
        using var key = TestKey();
        var card = Pull(key, CardCipher(key)).SecureItem!;
        var data = WalletItemDataCodec.DecodeBankCard(card);
        switch (shape)
        {
            case "billing-address":
                data.BillingAddress = IdentityAddress;
                break;
            case "nickname":
                data.Nickname = "Everyday card";
                break;
            case "debit":
                data.CardTypeString = "DEBIT";
                break;
            case "bank-name":
                data.BankName = "Example Bank";
                break;
            case "images":
                card.ImagePaths = """["front.jpg"]""";
                break;
        }

        card.ItemData = WalletItemDataCodec.EncodeBankCard(data);
        Assert.False(BitwardenCipherPayloadBuilder.CanEncode(card));
        Assert.Throws<BitwardenProtocolException>(
            () => BitwardenCipherPayloadBuilder.BuildSecureItemCipher(card, key));
    }

    [Theory]
    [InlineData("issued-date")]
    [InlineData("expiry-date")]
    [InlineData("plain-id-card")]
    [InlineData("foreign-detail")]
    [InlineData("line-break-in-value")]
    [InlineData("split-country")]
    public void An_identity_holding_what_bitwarden_has_no_field_for_refuses(string shape)
    {
        using var key = TestKey();
        var identity = Pull(key, IdentityCipher(key)).SecureItem!;
        var data = WalletItemDataCodec.DecodeDocument(identity);
        switch (shape)
        {
            case "issued-date":
                data.IssuedDate = "2020-01-02";
                break;
            case "expiry-date":
                data.ExpiryDate = "2030-01-02";
                break;
            case "plain-id-card":
                data.DocumentTypeString = "ID_CARD";
                break;
            case "foreign-detail":
                data.AdditionalInfo = $"{data.AdditionalInfo}{Environment.NewLine}Blood type: O-";
                break;
            case "line-break-in-value":
                data.AdditionalInfo =
                    $"{data.AdditionalInfo}{Environment.NewLine}Company: Two{Environment.NewLine}Lines";
                break;
            case "split-country":
                data.Nationality = "Ireland";
                break;
        }

        identity.ItemData = WalletItemDataCodec.EncodeDocument(data);
        Assert.False(BitwardenCipherPayloadBuilder.CanEncode(identity));
        Assert.Throws<BitwardenProtocolException>(
            () => BitwardenCipherPayloadBuilder.BuildSecureItemCipher(identity, key));
    }

    [Fact]
    public void A_trashed_or_nameless_secure_item_refuses_because_another_route_decides_it()
    {
        using var key = TestKey();
        var trashed = Pull(key, NoteCipher(key)).SecureItem!;
        trashed.IsDeleted = true;
        Assert.Throws<BitwardenPayloadRefusalException>(
            () => BitwardenCipherPayloadBuilder.BuildSecureItemCipher(trashed, key));

        var nameless = Pull(key, NoteCipher(key)).SecureItem!;
        nameless.Title = "   ";
        Assert.Equal(
            BitwardenPayloadRefusal.MissingTitle,
            Assert.Throws<BitwardenPayloadRefusalException>(
                () => BitwardenCipherPayloadBuilder.BuildSecureItemCipher(nameless, key)).Reason);

        var address = Pull(key, NoteCipher(key)).SecureItem!;
        address.ItemType = VaultItemType.BillingAddress;
        Assert.Equal(
            BitwardenPayloadRefusal.UnsupportedShape,
            Assert.Throws<BitwardenPayloadRefusalException>(
                () => BitwardenCipherPayloadBuilder.BuildSecureItemCipher(address, key)).Reason);
    }

    /// <summary>
    /// What the editor leaves behind when a note is saved: the title, body and attachment list the codec
    /// derives, not the strings that were typed.
    /// </summary>
    private static void SaveNote(
        SecureItem item,
        string title,
        string content,
        string tags = "",
        bool isMarkdown = false)
    {
        var saved = NoteContentCodec.BuildSavePayload(title, content, tags, isMarkdown);
        item.Title = saved.Title;
        item.Notes = saved.NotesCache;
        item.ItemData = saved.ItemData;
        item.ImagePaths = saved.ImagePaths;
    }

    private static void AssertOnlyCipherStrings(string json)
    {
        using var document = JsonDocument.Parse(json);
        var strings = new List<string>();
        CollectStrings(document.RootElement, strings);
        Assert.NotEmpty(strings);
        Assert.All(strings, value => Assert.StartsWith("2.", value, StringComparison.Ordinal));
    }

    private static void CollectStrings(JsonElement element, List<string> found)
    {
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    found.Add(property.Value.GetString()!);
                    break;
                case JsonValueKind.Object:
                    CollectStrings(property.Value, found);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        CollectStrings(item, found);
                    }

                    break;
            }
        }
    }

    private static BitwardenDecodedCipher Pull(BitwardenSymmetricKey key, VaultCipherDto cipher) =>
        new BitwardenCipherDecoder(key)
            .Decode(
                new VaultSyncDto
                {
                    Profile = new VaultProfileDto { Id = "user-1", Name = "Round Trip" },
                    Ciphers = [cipher]
                },
                Now)
            .DecodedCiphers
            .Single();

    private static VaultCipherDto AsRemoteCipher(string json) =>
        BitwardenHttpContent.Deserialize<VaultCipherDto>(Encoding.UTF8.GetBytes(json)) with
        {
            Id = CipherId,
            RevisionDate = SecondRevision
        };

    private static VaultCipherDto NoteCipher(BitwardenSymmetricKey key) => new()
    {
        Id = CipherId,
        Type = 2,
        Name = Encrypted(NoteTitle, key),
        Notes = Encrypted(NoteBody, key),
        SecureNote = new VaultSecureNoteDto { Type = 0 },
        RevisionDate = FirstRevision
    };

    private static VaultCipherDto CardCipher(BitwardenSymmetricKey key) => new()
    {
        Id = CipherId,
        Type = 3,
        Name = Encrypted("Example card", key),
        Notes = Encrypted("notes about the card", key),
        Card = new VaultCardDto
        {
            CardholderName = Encrypted(Cardholder, key),
            Brand = Encrypted(Brand, key),
            Number = Encrypted(CardNumber, key),
            ExpMonth = Encrypted("03", key),
            ExpYear = Encrypted("2030", key),
            Code = Encrypted("123", key)
        },
        RevisionDate = FirstRevision
    };

    private static VaultCipherDto IdentityCipher(BitwardenSymmetricKey key) => new()
    {
        Id = CipherId,
        Type = 4,
        Name = Encrypted("Passport", key),
        Identity = new VaultIdentityDto
        {
            FirstName = Encrypted("Ada", key),
            LastName = Encrypted("Lovelace", key),
            PassportNumber = Encrypted(PassportNumber, key),
            Country = Encrypted(IdentityCountry, key),
            City = Encrypted(IdentityCity, key),
            Address1 = Encrypted(IdentityAddress, key)
        },
        RevisionDate = FirstRevision
    };

    private static string Encrypted(string value, BitwardenSymmetricKey key) =>
        BitwardenCipherStringCrypto.EncryptString(value, key);

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
