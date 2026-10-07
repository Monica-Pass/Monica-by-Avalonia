using System.Text.Json.Serialization;

namespace Monica.Core.Models;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DocumentWalletData))]
[JsonSerializable(typeof(BankCardWalletData))]
[JsonSerializable(typeof(BillingAddressWalletData))]
[JsonSerializable(typeof(PaymentAccountWalletData))]
[JsonSerializable(typeof(NoteContentCodec.NoteData))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<string>))]
internal partial class WalletNoteJsonContext : JsonSerializerContext
{
}
