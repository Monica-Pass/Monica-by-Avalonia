using System.Globalization;
using System.Text.Json;

namespace Monica.Core.ImportExport;

public sealed partial class BitwardenJsonImporter
{
    private static JsonElement RequireArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw InvalidExport();
        }

        return value;
    }

    private static JsonElement? GetObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Object ? value : throw InvalidExport();
    }

    private static JsonElement? GetArray(JsonElement parent, string name, int maximum)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw InvalidExport();
        }

        EnsureCount(value.GetArrayLength(), maximum);
        return value;
    }

    private static int CountArray(JsonElement parent, string name, int maximum) =>
        GetArray(parent, name, maximum)?.GetArrayLength() ?? 0;

    private static string GetString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return "";
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => throw InvalidExport()
        };
    }

    private static int GetInt32(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result))
        {
            return result;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result)
            ? result
            : 0;
    }

    private static bool GetBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False or JsonValueKind.Null => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var result) => result,
            _ => false
        };
    }

    private static DateTimeOffset GetDate(JsonElement parent, string name, DateTimeOffset fallback)
    {
        var value = GetString(parent, name);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var result)
            ? result
            : fallback;
    }

    private static SourceMetadata ReadMetadata(JsonElement item, string sourceId, int type)
    {
        var createdAt = GetDate(item, "creationDate", DateTimeOffset.UnixEpoch);
        var updatedAt = GetDate(item, "revisionDate", createdAt);
        var deletedAt = GetDate(item, "deletedDate", DateTimeOffset.MinValue);
        var revisionDate = GetString(item, "revisionDate");
        return new SourceMetadata(
            sourceId,
            type,
            GetString(item, "name").Trim(),
            GetString(item, "notes"),
            GetString(item, "folderId").Trim(),
            GetBoolean(item, "favorite"),
            createdAt,
            updatedAt,
            deletedAt == DateTimeOffset.MinValue ? null : deletedAt,
            revisionDate);
    }

    private sealed record SourceMetadata(
        string SourceId,
        int Type,
        string Title,
        string Notes,
        string FolderId,
        bool IsFavorite,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? DeletedAt,
        string RevisionDate);
}
