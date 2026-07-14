using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class PasswordEditorViewModel
{
    public IReadOnlyList<string> GetPasswordRows() => NormalizePasswordRows(SplitRows(PasswordLines));

    public IReadOnlyList<CustomField> GetCustomFields()
    {
        return SplitRows(CustomFieldsText)
            .Select((row, index) => ParseCustomField(row, index))
            .Where(field => field is not null)
            .Select(field => field!)
            .ToArray();
    }

    private string EncodeWebsites() => string.Join(", ", ParseWebsiteRows(WebsiteLines));

    private static IReadOnlyList<string> ParseWebsiteRows(string value)
    {
        return SplitRows(value.Replace('，', ','))
            .SelectMany(row => row.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(row => row.Trim())
            .Where(row => row.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizePasswordRows(IEnumerable<string> values)
    {
        return values
            .Select(row => row.Trim())
            .Where(row => row.Length > 0)
            .ToArray();
    }

    private static string NormalizeCustomIconType(string? value)
    {
        return value?.Trim().ToUpperInvariant() switch
        {
            "SIMPLE_ICON" => "SIMPLE_ICON",
            "UPLOADED" => "UPLOADED",
            _ => "NONE"
        };
    }

    private static IEnumerable<string> SplitRows(string value)
    {
        return value.Split(["\r\n", "\n", "\r"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string EncodeCustomFields(IEnumerable<CustomField> fields)
    {
        return string.Join(
            Environment.NewLine,
            fields
                .OrderBy(field => field.SortOrder)
                .ThenBy(field => field.Id)
                .Where(field => !string.IsNullOrWhiteSpace(field.Title) && !string.IsNullOrWhiteSpace(field.Value))
                .Select(field => $"{(field.IsProtected ? "!" : "")}{field.Title.Trim()}={field.Value.Trim()}"));
    }

    private static CustomField? ParseCustomField(string row, int sortOrder)
    {
        var separator = row.IndexOf('=');
        if (separator < 0)
        {
            separator = row.IndexOf(':');
        }

        if (separator <= 0)
        {
            return null;
        }

        var rawTitle = row[..separator].Trim();
        var isProtected = rawTitle.StartsWith('!');
        var title = isProtected ? rawTitle[1..].Trim() : rawTitle;
        var value = row[(separator + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new CustomField
        {
            Title = title,
            Value = value,
            IsProtected = isProtected,
            SortOrder = sortOrder
        };
    }
}
