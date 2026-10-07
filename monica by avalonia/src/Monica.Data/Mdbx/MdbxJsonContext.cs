using System.Text.Json.Serialization;
using Monica.Core.Models;

namespace Monica.Data.Mdbx;

/// <summary>
/// NativeAOT metadata for the legacy MDBX payloads. These payloads are read during the
/// canonical-vault bootstrap path, so reflection-based JsonSerializer overloads are not
/// acceptable even though they work in the JIT build.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    IgnoreReadOnlyProperties = true)]
[JsonSerializable(typeof(PasswordEntry))]
[JsonSerializable(typeof(SecureItem))]
[JsonSerializable(typeof(MdbxVaultStore.MdbxPayload<PasswordEntry>))]
[JsonSerializable(typeof(MdbxVaultStore.MdbxPayload<SecureItem>))]
[JsonSerializable(typeof(MdbxVaultStore.MdbxPayload<MdbxVaultStore.MdbxPasswordPayload>))]
[JsonSerializable(typeof(MdbxVaultStore.MdbxPayload<MdbxVaultStore.MdbxSecureItemPayload>))]
internal partial class MdbxJsonContext : JsonSerializerContext
{
}
