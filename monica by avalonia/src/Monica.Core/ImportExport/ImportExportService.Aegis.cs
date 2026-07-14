using System.Text.Json;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.Core.ImportExport;

public sealed partial class ImportExportService
{
    public string ExportAegisJson(IEnumerable<SecureItem> secureItems)
    {
        var entries = secureItems
            .Where(item => item.ItemType == VaultItemType.Totp)
            .Select(CreateAegisEntry)
            .OfType<AegisEntryDto>()
            .ToList();
        var package = new AegisExportPackageDto(
            1,
            new AegisHeaderDto([], new AegisHeaderParamsDto("", "")),
            new AegisDatabaseDto(3, entries));
        return JsonSerializer.Serialize(package, MonicaJsonContext.Default.AegisExportPackageDto);
    }

    public IReadOnlyList<SecureItem> ImportAegisJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("db", out var db))
        {
            throw new InvalidOperationException("Invalid Aegis JSON payload.");
        }

        if (db.ValueKind == JsonValueKind.String)
        {
            throw new NotSupportedException("Encrypted Aegis JSON imports are not supported yet.");
        }

        if (db.ValueKind != JsonValueKind.Object || !db.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Invalid Aegis database payload.");
        }

        var imported = new List<SecureItem>();
        foreach (var entry in entries.EnumerateArray())
        {
            var item = CreateSecureItemFromAegisEntry(entry);
            if (item is not null)
            {
                imported.Add(item);
            }
        }

        return imported;
    }

    private static AegisEntryDto? CreateAegisEntry(SecureItem item)
    {
        var data = TotpDataResolver.ParseStoredItemData(item.ItemData, item.Title);
        if (data is null || string.IsNullOrWhiteSpace(data.Secret))
        {
            return null;
        }

        var normalized = TotpDataResolver.Normalize(data);
        if (!string.Equals(normalized.OtpType, "TOTP", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized.OtpType, "STEAM", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = FirstNonEmpty(normalized.AccountName, item.Title, normalized.Issuer, "Authenticator");
        var issuer = string.IsNullOrWhiteSpace(normalized.Issuer) ? item.Title.Trim() : normalized.Issuer;
        var note = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim();
        var entryType = string.Equals(normalized.OtpType, "STEAM", StringComparison.OrdinalIgnoreCase) ? "steam" : "totp";
        return new AegisEntryDto(
            entryType,
            Guid.NewGuid().ToString(),
            name,
            issuer,
            note,
            new AegisEntryInfoDto(normalized.Secret, normalized.Algorithm, normalized.Digits, normalized.Period));
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.Select(value => value.Trim()).First(value => !string.IsNullOrWhiteSpace(value));

    private static SecureItem? CreateSecureItemFromAegisEntry(JsonElement entry)
    {
        if (!entry.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var secret = ReadJsonString(info, "secret");
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var type = ReadJsonString(entry, "type");
        var otpType = type.Trim().ToLowerInvariant() switch
        {
            "steam" => "STEAM",
            "totp" or "" => "TOTP",
            _ => ""
        };
        if (otpType.Length == 0)
        {
            return null;
        }

        var name = ReadJsonString(entry, "name");
        var issuer = ReadJsonString(entry, "issuer");
        var data = TotpDataResolver.Normalize(new TotpData(
            secret,
            issuer,
            name,
            ReadJsonInt(info, "period") ?? 30,
            ReadJsonInt(info, "digits") ?? (otpType == "STEAM" ? 5 : 6),
            ReadJsonString(info, "algo", "SHA1"),
            otpType));
        var now = DateTimeOffset.UtcNow;
        return new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = FirstNonEmpty(name, issuer, "Authenticator"),
            Notes = ReadJsonString(entry, "note"),
            ItemData = TotpDataResolver.ToItemData(data),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static string ReadJsonString(JsonElement element, string name, string fallback = "") =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static int? ReadJsonInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
}
