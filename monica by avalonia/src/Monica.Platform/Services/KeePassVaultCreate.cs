using System.Security.Cryptography;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace Monica.Platform.Services;

/// <summary>
/// Builds a brand-new database in the shape Monica for Android writes one. Every number here was read
/// off a file kotpass 0.10.0 produced, not chosen: the Android client creates KDBX 4.1 with AES and
/// Argon2d at 8 iterations / 32 MiB / parallelism 2 / algorithm version 0x13, names the root folder
/// <c>Root</c>, and leaves the recycle bin switched off until the first entry is recycled. The version
/// is the part that is not cosmetic - 4.1 is the only KDBX revision with a slot for the folder an entry
/// was moved out of, so a database created at 3.1 cannot carry a recycle-and-restore round trip.
/// </summary>
internal static class KeePassVaultCreate
{
    /// <summary>
    /// The folder name the Android client passes as its root on create.
    /// </summary>
    public const string RootGroupName = "Root";

    public const uint FormatVersion = KeePassVaultWrite.Kdbx41;

    public const ulong ArgonIterations = 8;
    public const ulong ArgonMemoryBytes = 32L * 1024L * 1024L;
    public const uint ArgonParallelism = 2;
    public const uint ArgonAlgorithmVersion = 0x13;
    public const int ArgonSaltBytes = 32;

    /// <summary>
    /// KDF parameter keys are the names KPCLib spells them with, which are the same one-byte tags the
    /// on-disk variant dictionary carries. The Android client writes the memory and iteration counts as
    /// 64-bit values and the parallelism and algorithm version as 32-bit ones, so the types below are
    /// part of the shape, not an implementation detail.
    /// </summary>
    private static KdfParameters Argon2Shape()
    {
        var parameters = new Argon2Kdf(Argon2Type.D).GetDefaultParameters();
        parameters.SetByteArray(Argon2Kdf.ParamSalt, RandomNumberGenerator.GetBytes(ArgonSaltBytes));
        parameters.SetUInt64(Argon2Kdf.ParamIterations, ArgonIterations);
        parameters.SetUInt64(Argon2Kdf.ParamMemory, ArgonMemoryBytes);
        parameters.SetUInt32(Argon2Kdf.ParamParallelism, ArgonParallelism);
        parameters.SetUInt32(Argon2Kdf.ParamVersion, ArgonAlgorithmVersion);
        return parameters;
    }

    /// <summary>
    /// Returns the open model and the payload that was verified against it: the bytes have already been
    /// re-opened once by <see cref="KeePassVaultWrite.BuildVerifiedPayload"/>, so a caller can hand the
    /// model to a session instead of paying for the key derivation a second time.
    /// </summary>
    public static (PwDatabase Database, byte[] Payload) Build(
        string fileName,
        string password,
        string? databaseName)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(password));
        var database = new PwDatabase();
        try
        {
            database.New(IOConnectionInfo.FromPath(fileName), key);
            ApplyAndroidShape(database, databaseName);
            return (database, KeePassVaultWrite.BuildVerifiedPayload(database, key, FormatVersion));
        }
        catch
        {
            if (database.IsOpen)
            {
                database.Close();
            }

            throw;
        }
    }

    private static void ApplyAndroidShape(PwDatabase database, string? databaseName)
    {
        database.RootGroup.Name = RootGroupName;
        database.Name = string.IsNullOrWhiteSpace(databaseName)
            ? Path.GetFileNameWithoutExtension(database.IOConnectionInfo.Path)
            : databaseName.Trim();
        database.RecycleBinEnabled = false;
        database.RecycleBinUuid = PwUuid.Zero;
        database.KdfParameters = Argon2Shape();
    }
}
