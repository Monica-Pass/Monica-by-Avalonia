using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Monica.Platform.Services;

namespace Monica.App.Services;

internal sealed class DeviceAssetTransfer(string directory, string key)
{
    private readonly string _path = Path.Combine(directory, "assets.monica");
    private Dictionary<string, long>? _offsets;
    public bool Contains(string id)
    {
        if (!File.Exists(_path)) return false;
        LoadIndex(); return _offsets!.ContainsKey(id);
    }
    private void LoadIndex() => _offsets ??= LanSyncNative.Request(new { operation = "asset_list", path = _path }).GetProperty("items").EnumerateArray()
        .ToDictionary(item => item.GetProperty("id").GetString()!, item => item.GetProperty("offset").GetInt64());

    public void Append(string id, byte[] bytes, CancellationToken token, Action<long>? progress = null)
    {
        const int chunk = 512 * 1024;
        for (var start = 0; start < bytes.Length || start == 0; start += chunk)
        {
            token.ThrowIfCancellationRequested();
            var size = Math.Min(chunk, bytes.Length - start);
            LanSyncNative.Request(new
            {
                operation = "asset_append",
                path = _path,
                key,
                id,
                index = start / chunk,
                final = start + size == bytes.Length,
                data = Convert.ToBase64String(bytes, start, size)
            });
            progress?.Invoke(start + size);
        }
    }

    public Task<byte[]> ReadAsync(JsonObject descriptor, CancellationToken token) => Task.Run(() =>
    {
        var id = descriptor["id"]!.GetValue<string>();
        var size = descriptor["size"]!.GetValue<long>();
        if (size is < 0 or > 64L * 1024 * 1024) throw new InvalidDataException("asset_limit");
        LoadIndex();
        if (!_offsets!.TryGetValue(id, out var offset)) throw new InvalidDataException("asset_unavailable");
        var bytes = new byte[size];
        var written = 0; var index = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var result = LanSyncNative.Request(new { operation = "asset_read", path = _path, key, offset });
                if (result.GetProperty("id").GetString() != id || result.GetProperty("index").GetInt32() != index++) throw new InvalidDataException("invalid_asset");
                var chunk = Convert.FromBase64String(result.GetProperty("data").GetString()!);
                try
                {
                    if (written + chunk.Length > bytes.Length) throw new InvalidDataException("invalid_asset");
                    chunk.CopyTo(bytes, written); written += chunk.Length;
                }
                finally { CryptographicOperations.ZeroMemory(chunk); }
                if (result.GetProperty("final").GetBoolean()) break;
                offset = result.GetProperty("next").GetInt64();
            }
            if (written != size || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(id, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("invalid_asset");
            return bytes;
        }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
    }, token);
}
