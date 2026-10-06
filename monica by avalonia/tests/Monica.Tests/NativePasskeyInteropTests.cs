using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Monica.Core.Passkeys;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class NativePasskeyInteropTests
{
    [Theory]
    [InlineData(true, 8u, 192, 160, 168)]
    [InlineData(false, 6u, 168, 136, 144)]
    public void Native_response_preserves_the_exact_bytes_used_by_the_authenticator(
        bool registration, uint version, int structureSize, int lengthOffset, int pointerOffset)
    {
        // These offsets are from the 64-bit Windows SDK webauthn.h layout, independent of
        // Monica's managed structures. All shipped Windows RIDs and test runners use 64 bits.
        Assert.Equal(8, IntPtr.Size);
        var challenge = RandomNumberGenerator.GetBytes(32);
        var type = registration ? PasskeyClientData.CreateType : PasskeyClientData.AssertionType;
        var requested = PasskeyClientData.Build(type, challenge, "https://example.com").Json;
        var native = Encoding.UTF8.GetBytes(" " + Encoding.UTF8.GetString(requested));
        using var response = new NativeResponse(structureSize);
        using var clientData = new NativeResponse(native.Length);
        Marshal.Copy(native, 0, clientData.Pointer, native.Length);
        Marshal.WriteInt32(response.Pointer, 0, checked((int)version));
        Marshal.WriteInt32(response.Pointer, lengthOffset, native.Length);
        Marshal.WriteIntPtr(response.Pointer, pointerOffset, clientData.Pointer);

        var returned = registration
            ? WindowsNativePasskeyService.CopyCredentialAttestationClientData(response.Pointer, version, requested)
            : WindowsNativePasskeyService.CopyAssertionClientData(response.Pointer, version, requested);
        Assert.Equal(native, returned);
        Assert.True(NativePasskeyValidation.VerifyClientData(returned, type, challenge, "https://example.com"));
        Assert.True(PasskeyClientData.TryParse(returned, out var parsed));
        Assert.Equal(SHA256.HashData(native), parsed!.ClientDataHash);
        Assert.NotEqual(SHA256.HashData(requested), parsed.ClientDataHash);
    }

    [Theory]
    [InlineData(true, 96)]
    [InlineData(false, 72)]
    public void Legacy_structures_use_input_client_data_without_reading_the_new_tail(bool registration, int size)
    {
        using var response = new NativeResponse(size);
        Marshal.WriteInt32(response.Pointer, 0, 1);
        var requested = PasskeyClientData.Build(PasskeyClientData.AssertionType, new byte[32], "https://example.com").Json;

        var returned = registration
            ? WindowsNativePasskeyService.CopyCredentialAttestationClientData(response.Pointer, 1, requested)
            : WindowsNativePasskeyService.CopyAssertionClientData(response.Pointer, 1, requested);

        Assert.Equal(requested, returned);
    }

    [Theory]
    [InlineData(true, 8u, 192, 160)]
    [InlineData(false, 6u, 168, 136)]
    public void Invalid_native_client_data_bounds_are_rejected_without_falling_back_to_input(
        bool registration, uint version, int size, int lengthOffset)
    {
        using var response = new NativeResponse(size);
        Marshal.WriteInt32(response.Pointer, lengthOffset, 32); // null pointer with nonzero length
        var requested = new byte[32];

        Assert.Throws<InvalidOperationException>(() => registration
            ? WindowsNativePasskeyService.CopyCredentialAttestationClientData(response.Pointer, version, requested)
            : WindowsNativePasskeyService.CopyAssertionClientData(response.Pointer, version, requested));

        Marshal.WriteInt32(response.Pointer, lengthOffset, NativePasskeyValidation.MaximumResponseBytes + 1);
        Assert.Throws<InvalidOperationException>(() => registration
            ? WindowsNativePasskeyService.CopyCredentialAttestationClientData(response.Pointer, version, requested)
            : WindowsNativePasskeyService.CopyAssertionClientData(response.Pointer, version, requested));
    }

    private sealed class NativeResponse : IDisposable
    {
        public NativeResponse(int size)
        {
            Pointer = Marshal.AllocHGlobal(size);
            Marshal.Copy(new byte[size], 0, Pointer, size);
        }

        public nint Pointer { get; }
        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }
}
