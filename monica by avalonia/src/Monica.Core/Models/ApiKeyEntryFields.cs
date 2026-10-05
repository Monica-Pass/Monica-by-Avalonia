namespace Monica.Core.Models;

/// <summary>
/// The Android API-key compatibility shape. The secret stays in the encrypted password value;
/// these two public custom fields only identify the entry and carry its optional request endpoint.
/// </summary>
public static class ApiKeyEntryFields
{
    public const string Type = "API_KEY";
    public const string Marker = "monica_api_key_type";
    public const string ApiUrl = "monica_api_key_url";

    public static bool IsMarker(CustomField field) =>
        string.Equals(field.Title, Marker, StringComparison.Ordinal) &&
        string.Equals(field.Value, Type, StringComparison.Ordinal);

    public static bool Owns(string title) =>
        string.Equals(title, Marker, StringComparison.Ordinal) ||
        string.Equals(title, ApiUrl, StringComparison.Ordinal);

    /// <summary>Validates an optional endpoint before the desktop offers it as a link.</summary>
    public static bool IsValidOptionalUrl(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0)
        {
            return true;
        }

        if (text.Length > 2048 || text.Any(char.IsControl) ||
            !Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.UserInfo.Length > 0)
        {
            return false;
        }

        return uri.Port is -1 or >= 1 and <= 65535;
    }
}
