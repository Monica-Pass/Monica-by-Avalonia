using System.Text.Json;
using Monica.Data.Mdbx;

namespace Monica.Tests;

public sealed class AndroidMdbxPayloadMergeTests
{
    private const string Stored = """
        {"kind":"password","notes":"before","custom_fields":[
          {"title":"Alias","value":"fixture","is_protected":false,"sort_order":0,
           "future":{"counter":9007199254740993,"values":[null,false,""]}}
        ],"foreign":{"enabled":false}}
        """;

    [Fact]
    public void Unchanged_custom_fields_keep_the_entire_foreign_array()
    {
        var updated = """
            {"kind":"password","notes":"after","custom_fields":[
              {"title":"Alias","value":"fixture","is_protected":false,"sort_order":0}
            ]}
            """;
        using var stored = JsonDocument.Parse(Stored);
        using var merged = JsonDocument.Parse(Merge(updated));

        Assert.True(JsonElement.DeepEquals(stored.RootElement.GetProperty("custom_fields"),
            merged.RootElement.GetProperty("custom_fields")));
        Assert.Equal("after", merged.RootElement.GetProperty("notes").GetString());
        Assert.False(merged.RootElement.GetProperty("foreign").GetProperty("enabled").GetBoolean());
    }

    [Theory]
    [InlineData("title", "\"Changed\"")]
    [InlineData("value", "\"changed\"")]
    [InlineData("is_protected", "true")]
    [InlineData("sort_order", "1")]
    public void Edited_fields_are_never_replaced_with_old_values(string property, string value)
    {
        var fields = System.Text.Json.Nodes.JsonNode.Parse(Stored)!["custom_fields"]!.DeepClone();
        fields[0]!.AsObject().Remove("future");
        fields[0]![property] = System.Text.Json.Nodes.JsonNode.Parse(value);
        var updated = new System.Text.Json.Nodes.JsonObject { ["custom_fields"] = fields };
        using var merged = JsonDocument.Parse(Merge(updated.ToJsonString()));
        var actual = merged.RootElement.GetProperty("custom_fields")[0];

        Assert.Equal(value, actual.GetProperty(property).GetRawText());
        Assert.False(actual.TryGetProperty("future", out _));
    }

    [Theory]
    [InlineData("{\"custom_fields\":[]}")]
    [InlineData("{}")]
    public void Removing_custom_fields_does_not_restore_them(string updated)
    {
        using var merged = JsonDocument.Parse(Merge(updated));
        Assert.True(!merged.RootElement.TryGetProperty("custom_fields", out var fields) ||
            fields.GetArrayLength() == 0);
    }

    [Fact]
    public void Reordered_fields_do_not_inherit_metadata_by_array_position()
    {
        const string stored = """
            {"custom_fields":[
              {"title":"A","value":"a","is_protected":false,"sort_order":0,"foreign":"first"},
              {"title":"B","value":"b","is_protected":false,"sort_order":1,"foreign":"second"}]}
            """;
        const string updated = """
            {"custom_fields":[
              {"title":"B","value":"b","is_protected":false,"sort_order":0},
              {"title":"A","value":"a","is_protected":false,"sort_order":1}]}
            """;
        using var result = JsonDocument.Parse(AndroidMdbxPayloadMerge.PreserveForeignFields(
            stored, updated, AndroidMdbxPayloadFamily.Password));
        using var expected = JsonDocument.Parse(updated);
        Assert.True(JsonElement.DeepEquals(expected.RootElement.GetProperty("custom_fields"),
            result.RootElement.GetProperty("custom_fields")));
    }

    [Fact]
    public void Camel_case_keys_preserve_unchanged_metadata_without_duplicate_arrays()
    {
        const string stored = """
            {"customFields":[{"title":"A","value":"a","isProtected":false,"sortOrder":0,"foreign":true}]}
            """;
        const string updated = """
            {"custom_fields":[{"title":"A","value":"a","is_protected":false,"sort_order":0}]}
            """;
        using var result = JsonDocument.Parse(AndroidMdbxPayloadMerge.PreserveForeignFields(
            stored, updated, AndroidMdbxPayloadFamily.Password));
        Assert.False(result.RootElement.TryGetProperty("customFields", out _));
        Assert.True(result.RootElement.GetProperty("custom_fields")[0].GetProperty("foreign").GetBoolean());
    }

    private static string Merge(string updated) => AndroidMdbxPayloadMerge.PreserveForeignFields(
        Stored, updated, AndroidMdbxPayloadFamily.Password);
}
