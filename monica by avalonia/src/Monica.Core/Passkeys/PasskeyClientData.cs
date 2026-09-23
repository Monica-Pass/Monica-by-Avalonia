using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;

namespace Monica.Core.Passkeys;

/// <summary>
/// The collected-client-data a WebAuthn signature commits to. The exact serialized bytes are kept
/// alongside the fields because a relying party hashes them verbatim, so nothing may re-serialize
/// after signing.
/// </summary>
public sealed class PasskeyClientData
{
    public const string CreateType = "webauthn.create";
    public const string AssertionType = "webauthn.get";

    private PasskeyClientData(string type, string challenge, string origin, bool crossOrigin, byte[] json)
    {
        Type = type;
        Challenge = challenge;
        Origin = origin;
        CrossOrigin = crossOrigin;
        Json = json;
    }

    public string Type { get; }

    public string Challenge { get; }

    public string Origin { get; }

    public bool CrossOrigin { get; }

    public byte[] Json { get; }

    public byte[] ClientDataHash => SHA256.HashData(Json);

    public string ClientDataJsonBase64Url => PasskeyBase64Url.Encode(Json);

    public static PasskeyClientData Build(string type, byte[] challenge, string origin)
    {
        if (type is not (CreateType or AssertionType))
        {
            throw new ArgumentException($"Unsupported client data type '{type}'.", nameof(type));
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", type);
            writer.WriteString("challenge", PasskeyBase64Url.Encode(challenge));
            writer.WriteString("origin", origin);
            writer.WriteBoolean("crossOrigin", false);
            writer.WriteEndObject();
        }

        var json = stream.ToArray();
        return new PasskeyClientData(
            type,
            PasskeyBase64Url.Encode(challenge),
            origin,
            crossOrigin: false,
            json);
    }

    public static bool TryParse(ReadOnlySpan<byte> json, out PasskeyClientData? parsed)
    {
        parsed = null;
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryReadString(root, "type", out var type) ||
                !TryReadString(root, "challenge", out var challenge) ||
                !TryReadString(root, "origin", out var origin))
            {
                return false;
            }

            var crossOrigin = root.TryGetProperty("crossOrigin", out var flag) &&
                flag.ValueKind == JsonValueKind.True;
            parsed = new PasskeyClientData(type, challenge, origin, crossOrigin, json.ToArray());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadString(
        JsonElement root,
        string name,
        [NotNullWhen(true)] out string? value)
    {
        value = null;
        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            (value = property.GetString()) is not null;
    }
}
