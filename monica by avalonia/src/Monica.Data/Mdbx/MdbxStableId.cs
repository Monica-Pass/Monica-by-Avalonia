using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Monica.Data.Mdbx;

/// <summary>
/// Creates a stable positive domain ID for MDBX objects whose native identity
/// is a string. The scope prevents a project ID and attachment ID with the same
/// text from sharing a desktop domain identity.
/// </summary>
public static class MdbxStableId
{
    public static long FromString(string scope, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var bytes = Encoding.UTF8.GetBytes($"{scope}\0{value.Trim()}");
        var hash = SHA256.HashData(bytes);
        var id = BinaryPrimitives.ReadInt64LittleEndian(hash) & long.MaxValue;
        return id == 0 ? 1 : id;
    }
}
