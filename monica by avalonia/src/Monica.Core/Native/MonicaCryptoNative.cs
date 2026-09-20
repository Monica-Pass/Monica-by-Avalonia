using System.Runtime.InteropServices;

namespace Monica.Core.Native;

internal static class MonicaCryptoNative
{
    private const string LibraryName = "monica_crypto";
    private const int OutputLen = 32;

    private static readonly Lazy<(bool Available, string? Error)> NativeAvailability = new(() =>
    {
        try
        {
            var probe = new byte[OutputLen];
            DeriveArgon2id(
                new byte[] { 0 },
                new byte[8],
                1, 8, 1,
                probe);
            return (true, null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or SEHException
                                   or BadImageFormatException or EntryPointNotFoundException)
        {
            return (false, DescribeLoadFailure(ex));
        }
    });

    public static bool IsAvailable => NativeAvailability.Value.Available;
    public static string? AvailabilityError => NativeAvailability.Value.Error;

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "monica_argon2id")]
    private static extern int Argon2idRaw(
        IntPtr passwordPtr, int passwordLen,
        IntPtr saltPtr, int saltLen,
        uint iterations, uint memoryKib, uint parallelism,
        IntPtr outputPtr);

    public static void DeriveArgon2id(
        ReadOnlySpan<byte> password,
        ReadOnlySpan<byte> salt,
        uint iterations,
        uint memoryKib,
        uint parallelism,
        Span<byte> output)
    {
        if (output.Length != OutputLen)
        {
            throw new ArgumentException(
                $"Output buffer must be exactly {OutputLen} bytes.", nameof(output));
        }

        unsafe
        {
            fixed (byte* pwPtr = password)
            fixed (byte* saltPtr = salt)
            fixed (byte* outPtr = output)
            {
                var rc = Argon2idRaw(
                    (IntPtr)pwPtr, password.Length,
                    (IntPtr)saltPtr, salt.Length,
                    iterations, memoryKib, parallelism,
                    (IntPtr)outPtr);
                if (rc != 0)
                {
                    throw new InvalidOperationException(
                        $"Native Argon2id derivation failed with error code {rc}.");
                }
            }
        }
    }

    private static string DescribeLoadFailure(Exception ex) =>
        $"The native crypto library ({ExpectedLibraryName()}) could not be loaded: {ex.GetType().Name}: {ex.Message}";

    private static string ExpectedLibraryName()
    {
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "monica_crypto.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "libmonica_crypto.dylib"
                : "libmonica_crypto.so";
        return $"{fileName}, {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}";
    }
}
