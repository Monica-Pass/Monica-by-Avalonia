using System.Globalization;
using System.Text.Json;
using Monica.Data.Mdbx;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IEnumerable<PasswordDetailField> CreateMdbxUnknownEntryFields(MdbxUnknownEntryDetail detail)
    {
        var descriptor = detail.Descriptor;
        var fields = new List<PasswordDetailField>
        {
            CreateMdbxUnknownEntryField(_localization.Get("MdbxUnknownEntryId"), descriptor.EntryId),
            CreateMdbxUnknownEntryField(_localization.Get("MdbxUnknownProjectId"), descriptor.ProjectId),
            CreateMdbxUnknownEntryField(_localization.Get("MdbxUnknownEntryType"), descriptor.EntryType),
            CreateMdbxUnknownEntryField(_localization.Get("Title"), descriptor.Title),
            CreateMdbxUnknownEntryField(_localization.Get("MdbxUnknownPayloadVersion"), descriptor.PayloadSchemaVersion.ToString(CultureInfo.InvariantCulture)),
            CreateMdbxUnknownEntryField(_localization.Get("MdbxUnknownHeadCommit"), descriptor.HeadCommitId),
            CreateMdbxUnknownEntryField(_localization.Get("MdbxUnknownUpdatedAt"), descriptor.UpdatedAt)
        };

        try
        {
            using var document = JsonDocument.Parse(detail.PayloadJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.GetPropertyCount() is > 0 and <= 1000)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    var rawValue = property.Value.GetRawText();
                    var value = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? ""
                        : rawValue;
                    fields.Add(CreateMdbxUnknownEntryField(property.Name, value, rawValue));
                }
            }
            else
            {
                fields.Add(CreateMdbxUnknownEntryField("JSON", detail.PayloadJson));
            }
        }
        catch (JsonException)
        {
            fields.Add(CreateMdbxUnknownEntryField("JSON", detail.PayloadJson));
        }

        return fields;
    }

    private PasswordDetailField CreateMdbxUnknownEntryField(string label, string value, string? rawValue = null)
    {
        // Quoted JSON keeps empty/whitespace strings revealable while copying their exact value.
        var displayValue = string.IsNullOrWhiteSpace(value) ? rawValue ?? JsonSerializer.Serialize(value) : value;
        var field = new PasswordDetailField(label, displayValue, value, isSensitive: true);
        field.ConfigureVisibilityLabels(_localization.Get("ShowSensitiveField"), _localization.Get("HideSensitiveField"));
        return field;
    }
}
