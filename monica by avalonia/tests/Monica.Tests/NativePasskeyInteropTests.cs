using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Monica.Core.Passkeys;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class NativePasskeyInteropTests
{
    [Theory]
    [InlineData(0u, false, false)]
    [InlineData(0u, true, false)]
    [InlineData(9u, false, false)]
    [InlineData(9u, true, true)]
    public void Platform_ceremony_availability_requires_both_the_API_and_Windows_Hello(
        uint apiVersion, bool helloAvailable, bool expected)
    {
        var integration = new PlatformIntegrationService("Windows", []);
        var service = new WindowsNativePasskeyService(integration, apiVersion, helloAvailable);

        Assert.Equal(expected, service.IsAvailable);
        Assert.Equal(apiVersion > 0, service.Support.IsWebAuthnClientApiAvailable);
        Assert.Equal(helloAvailable, service.IsUserVerifyingPlatformAuthenticatorAvailable);
        Assert.False(service.Support.CanActAsWindowsCredentialProvider);
    }

    [Theory]
    [InlineData(unchecked((int)0x800704C7))] // HRESULT_FROM_WIN32(ERROR_CANCELLED)
    [InlineData(unchecked((int)0x80090036))] // NTE_USER_CANCELLED
    public void User_cancellation_is_reported_as_cancellation(int status)
    {
        Assert.Throws<OperationCanceledException>(() =>
            WindowsNativePasskeyService.ThrowIfFailed(status, "Passkey registration"));
    }

    [Theory]
    [InlineData(unchecked((int)0x80090011))] // NTE_NOT_FOUND
    [InlineData(unchecked((int)0x800705B4))] // HRESULT_FROM_WIN32(ERROR_TIMEOUT)
    [InlineData(unchecked((int)0x80090027))] // NTE_INVALID_PARAMETER
    public void Missing_credentials_timeouts_and_invalid_requests_are_not_user_cancellation(int status)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            WindowsNativePasskeyService.ThrowIfFailed(status, "Passkey assertion"));

        Assert.Contains($"0x{status:X8}", error.Message);
    }

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
    [InlineData(true, 8u, 192)]
    [InlineData(false, 6u, 168)]
    public void Absent_optional_native_client_data_keeps_the_exact_input_used_with_legacy_options(
        bool registration, uint version, int size)
    {
        using var response = new NativeResponse(size);
        var requested = Encoding.UTF8.GetBytes(" {\"type\":\"webauthn.get\",\"origin\":\"https://example.com\",\"challenge\":\"AA\"}");

        var returned = registration
            ? WindowsNativePasskeyService.CopyCredentialAttestationClientData(response.Pointer, version, requested)
            : WindowsNativePasskeyService.CopyAssertionClientData(response.Pointer, version, requested);

        Assert.Equal(requested, returned);
        Assert.Equal(SHA256.HashData(requested), SHA256.HashData(returned));
    }

    [Theory]
    [InlineData(true, 8u, 192, 168)]
    [InlineData(false, 6u, 168, 144)]
    public void A_client_data_pointer_without_bytes_is_malformed_instead_of_optional(
        bool registration, uint version, int size, int pointerOffset)
    {
        using var response = new NativeResponse(size);
        using var data = new NativeResponse(1);
        Marshal.WriteIntPtr(response.Pointer, pointerOffset, data.Pointer);

        Assert.Throws<InvalidOperationException>(() => registration
            ? WindowsNativePasskeyService.CopyCredentialAttestationClientData(response.Pointer, version, new byte[32])
            : WindowsNativePasskeyService.CopyAssertionClientData(response.Pointer, version, new byte[32]));
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

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Reported_resident_key_status_takes_precedence_over_the_creation_request(
        bool requiredResidentKey, bool returnedResidentKey)
    {
        Assert.Equal(8, IntPtr.Size);
        using var response = new NativeResponse(128); // WEBAUTHN_CREDENTIAL_ATTESTATION v4
        Marshal.WriteInt32(response.Pointer, 112, 0x10);
        Marshal.WriteInt32(response.Pointer, 124, returnedResidentKey ? 1 : 0);

        var metadata = WindowsNativePasskeyService.ReadCredentialAttestationMetadata(
            response.Pointer, 4, requiredResidentKey);

        Assert.Equal(returnedResidentKey, metadata.IsDiscoverable);
        Assert.Equal("internal", metadata.Transport);
    }

    [Fact]
    public void Registration_preserves_all_reported_supported_transports()
    {
        Assert.Equal(8, IntPtr.Size);
        using var response = new NativeResponse(192);
        Marshal.WriteInt32(response.Pointer, 112, 0x10); // used transport
        Marshal.WriteInt32(response.Pointer, 156, 0x30); // supports internal and hybrid

        var metadata = WindowsNativePasskeyService.ReadCredentialAttestationMetadata(response.Pointer, 8, false);

        Assert.Equal("internal,hybrid", metadata.Transport);
    }

    [Fact]
    public void Unknown_supported_transports_do_not_fabricate_a_device_transport()
    {
        Assert.Equal(8, IntPtr.Size);
        using var response = new NativeResponse(192);
        Marshal.WriteInt32(response.Pointer, 112, 0x10);
        Marshal.WriteInt32(response.Pointer, 156, 0x88); // test and unknown future transport

        var metadata = WindowsNativePasskeyService.ReadCredentialAttestationMetadata(response.Pointer, 8, false);

        Assert.Empty(metadata.Transport);
    }

    [Theory]
    [InlineData(0x01, "usb")]
    [InlineData(0x02, "nfc")]
    [InlineData(0x04, "ble")]
    [InlineData(0x10, "internal")]
    [InlineData(0x20, "hybrid")]
    [InlineData(0x40, "smart-card")]
    [InlineData(0x00, "")]
    [InlineData(0x08, "")]
    [InlineData(0x80, "")]
    [InlineData(0x30, "")]
    public void Ceremony_transport_is_taken_from_the_versioned_native_output(int flag, string expected)
    {
        Assert.Equal(8, IntPtr.Size);
        using var registration = new NativeResponse(120); // attestation v3
        using var assertion = new NativeResponse(128); // assertion v4
        Marshal.WriteInt32(registration.Pointer, 112, flag);
        Marshal.WriteInt32(assertion.Pointer, 120, flag);

        Assert.Equal(expected, WindowsNativePasskeyService.ReadCredentialAttestationMetadata(
            registration.Pointer, 3, false).Transport);
        Assert.Equal(expected, WindowsNativePasskeyService.ReadAssertionTransport(assertion.Pointer, 4));
    }

    [Fact]
    public void Older_native_results_do_not_read_unavailable_metadata()
    {
        // Allocate only each old prefix. Neither dwUsedTransport nor bResidentKey exists here.
        using var registration = new NativeResponse(96);
        using var assertion = new NativeResponse(72);

        var notRequired = WindowsNativePasskeyService.ReadCredentialAttestationMetadata(
            registration.Pointer, 1, false);
        var required = WindowsNativePasskeyService.ReadCredentialAttestationMetadata(
            registration.Pointer, 1, true);

        Assert.Empty(notRequired.Transport);
        Assert.False(notRequired.IsDiscoverable);
        Assert.True(required.IsDiscoverable); // successful bRequireResidentKey guarantees this
        Assert.Empty(WindowsNativePasskeyService.ReadAssertionTransport(assertion.Pointer, 1));
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
