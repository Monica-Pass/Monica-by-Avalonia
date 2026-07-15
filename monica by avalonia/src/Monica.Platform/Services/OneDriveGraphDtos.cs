using System.Text.Json;
using System.Text.Json.Serialization;

namespace Monica.Platform.Services;

internal sealed record OneDriveDriveItemDto(
    string Id,
    string? Name,
    long? Size,
    [property: JsonPropertyName("eTag")] string? ETag,
    DateTimeOffset? LastModifiedDateTime,
    [property: JsonPropertyName("@microsoft.graph.downloadUrl")] string? DownloadUrl);

internal sealed record OneDriveUploadSessionDto(
    string UploadUrl,
    DateTimeOffset? ExpirationDateTime,
    IReadOnlyList<string>? NextExpectedRanges);

internal sealed record OneDriveUploadSessionRequestDto(OneDriveUploadSessionItemDto Item);

internal sealed record OneDriveUploadSessionItemDto(
    [property: JsonPropertyName("@microsoft.graph.conflictBehavior")] string ConflictBehavior);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(OneDriveDriveItemDto))]
[JsonSerializable(typeof(OneDriveUploadSessionDto))]
[JsonSerializable(typeof(OneDriveUploadSessionRequestDto))]
internal sealed partial class OneDriveGraphJsonContext : JsonSerializerContext;
