using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.Tests;

public sealed class ApiKeyImportExportTests
{
    [Fact]
    public void Password_csv_round_trips_api_key_type_and_endpoint()
    {
        var entry = new PasswordEntry
        {
            Id = 7,
            Title = "API service",
            Password = "sk-secret",
            LoginType = PasswordLoginType.ApiKey
        };
        IReadOnlyDictionary<long, IReadOnlyList<CustomField>> fields = new Dictionary<long, IReadOnlyList<CustomField>>
        {
            [7] =
            [
                new CustomField { EntryId = 7, Title = ApiKeyEntryFields.Marker, Value = ApiKeyEntryFields.Type },
                new CustomField { EntryId = 7, Title = ApiKeyEntryFields.ApiUrl, Value = "https://api.example.test/v1" }
            ]
        };

        var service = new ImportExportService();
        var csv = service.ExportPasswordCsv([entry], fields);
        var imported = Assert.Single(service.ImportPasswordCsvWithCustomFields(csv));

        Assert.Equal(PasswordLoginType.ApiKey, imported.Entry.LoginType);
        Assert.Contains(imported.CustomFields, field => field.Title == ApiKeyEntryFields.Marker);
        Assert.Contains(imported.CustomFields, field => field.Title == ApiKeyEntryFields.ApiUrl &&
            field.Value == "https://api.example.test/v1");
    }
}
