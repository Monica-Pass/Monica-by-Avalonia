using System.Runtime.InteropServices;
using Monica.Core.Passkeys;

namespace Monica.Platform.Services;

public sealed record NativePasskeySupport(
    bool IsWebAuthnClientApiAvailable,
    uint WebAuthnApiVersion,
    bool CanActAsWindowsCredentialProvider,
    string StatusReason,
    bool IsUserVerifyingPlatformAuthenticatorAvailable = false)
{
    public static NativePasskeySupport Unavailable(string reason) =>
        new(false, 0, false, reason);
}

internal static class NativePasskeyProbe
{
    public static uint TryGetWebAuthnApiVersion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        try
        {
            return WebAuthNGetApiVersionNumber();
        }
        catch (DllNotFoundException)
        {
            return 0;
        }
        catch (EntryPointNotFoundException)
        {
            return 0;
        }
        catch (BadImageFormatException)
        {
            return 0;
        }
    }

    public static bool TryIsUserVerifyingPlatformAuthenticatorAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return WebAuthNIsUserVerifyingPlatformAuthenticatorAvailable(out var available) == 0 && available != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    [DllImport("webauthn.dll", EntryPoint = "WebAuthNGetApiVersionNumber")]
    private static extern uint WebAuthNGetApiVersionNumber();

    [DllImport("webauthn.dll", EntryPoint = "WebAuthNIsUserVerifyingPlatformAuthenticatorAvailable")]
    private static extern int WebAuthNIsUserVerifyingPlatformAuthenticatorAvailable(out int available);
}

public sealed class WindowsNativePasskeyService : INativePasskeyService
{
    private readonly PlatformIntegrationCapability _capability;

    public WindowsNativePasskeyService(IPlatformIntegrationService platformIntegrationService)
    {
        var version = NativePasskeyProbe.TryGetWebAuthnApiVersion();
        var uvAvailable = NativePasskeyProbe.TryIsUserVerifyingPlatformAuthenticatorAvailable();
        _capability = platformIntegrationService.GetCapability(PlatformFeatureKeys.NativePasskey);
        Support = new NativePasskeySupport(
            version > 0,
            version,
            CanActAsWindowsCredentialProvider: false,
            version > 0
                ? $"Windows WebAuthn client API v{version} is present; Windows Hello platform authenticator={uvAvailable}. A packaged credential-provider extension is still required for browser-wide system passkey requests."
                : "Windows WebAuthn client API is unavailable; a packaged credential-provider extension is required for system passkey requests.",
            uvAvailable);
    }

    public PlatformIntegrationCapability Capability => _capability;
    public NativePasskeySupport Support { get; }
    public bool IsAvailable => Support.IsWebAuthnClientApiAvailable;
    public bool IsUserVerifyingPlatformAuthenticatorAvailable => Support.IsUserVerifyingPlatformAuthenticatorAvailable;

    public Task<NativePasskeyRegistration> CreateAsync(
        NativePasskeyCreateRequest request,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => CreateCore(request), cancellationToken);

    public Task<NativePasskeyAssertion> GetAssertionAsync(
        NativePasskeyAssertionRequest request,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => GetAssertionCore(request), cancellationToken);

    public static PlatformIntegrationCapability CreateCapability()
    {
        return PlatformIntegrationService.PlatformLimited(
            PlatformFeatureKeys.NativePasskey,
            "Windows WebAuthn client API availability is probed on demand; Monica is not a packaged system credential provider.");
    }

    private static Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(operation, cancellationToken);
    }

    private static NativePasskeyRegistration CreateCore(NativePasskeyCreateRequest request)
    {
        EnsureAvailable();
        var clientData = PasskeyClientData.Build(
            PasskeyClientData.CreateType,
            request.Challenge,
            request.Origin ?? $"https://{request.RpId}");

        using var rpId = new HGlobalString(request.RpId);
        using var rpName = new HGlobalString(request.RpName);
        using var userName = new HGlobalString(request.UserName);
        using var userDisplayName = new HGlobalString(request.UserDisplayName);
        using var userId = new PinnedBytes(request.UserId);
        using var clientJson = new PinnedBytes(clientData.Json);
        using var hashAlgorithm = new HGlobalString("SHA-256");
        using var credentialType = new HGlobalString("public-key");
        var rp = new NativeRpInformation(1, rpId.Pointer, rpName.Pointer, nint.Zero);
        var user = new NativeUserInformation(1, (uint)request.UserId.Length, userId.Pointer, userName.Pointer, nint.Zero, userDisplayName.Pointer);
        var parameter = new NativeCoseCredentialParameter(1, credentialType.Pointer, request.Algorithm);
        var parameterMemory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeCoseCredentialParameter>());
        var parameters = new NativeCoseCredentialParameters(1, parameterMemory);
        try
        {
            Marshal.StructureToPtr(parameter, parameterMemory, false);
            var options = new NativeMakeCredentialOptions(
                1, 120_000, default, default,
                1, request.Discoverable ? 1 : 0,
                request.RequireUserVerification ? 1u : 2u, 1, 0);
            var client = new NativeClientData(1, (uint)clientData.Json.Length, clientJson.Pointer, hashAlgorithm.Pointer);
            var hr = WebAuthNAuthenticatorMakeCredential(
                request.ParentWindowHandle,
                ref rp,
                ref user,
                ref parameters,
                ref client,
                ref options,
                out var result);
            ThrowIfFailed(hr, "Windows Hello passkey registration");
            try
            {
                var attestation = Marshal.PtrToStructure<NativeCredentialAttestationPrefix>(result);
                return new NativePasskeyRegistration(
                    CopyBytes(attestation.pbCredentialId, attestation.cbCredentialId),
                    CopyBytes(attestation.pbAuthenticatorData, attestation.cbAuthenticatorData),
                    CopyBytes(attestation.pbAttestationObject, attestation.cbAttestationObject),
                    clientData.Json,
                    "internal",
                    request.Discoverable,
                    request.RequireUserVerification);
            }
            finally
            {
                WebAuthNFreeCredentialAttestation(result);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(parameterMemory);
        }
    }

    private static NativePasskeyAssertion GetAssertionCore(NativePasskeyAssertionRequest request)
    {
        EnsureAvailable();
        var clientData = PasskeyClientData.Build(
            PasskeyClientData.AssertionType,
            request.Challenge,
            request.Origin ?? $"https://{request.RpId}");
        using var rpId = new HGlobalString(request.RpId);
        using var clientJson = new PinnedBytes(clientData.Json);
        using var hashAlgorithm = new HGlobalString("SHA-256");
        using var credentialId = request.CredentialId is { Length: > 0 } id ? new PinnedBytes(id) : null;
        using var credentialType = new HGlobalString("public-key");
        var credentialMemory = credentialId is null
            ? nint.Zero
            : Marshal.AllocHGlobal(Marshal.SizeOf<NativeCredential>());
        try
        {
            var credentials = default(NativeCredentials);
            if (credentialId is not null)
            {
                Marshal.StructureToPtr(
                    new NativeCredential(1, (uint)credentialId.Length, credentialId.Pointer, credentialType.Pointer),
                    credentialMemory,
                    false);
                credentials = new NativeCredentials(1, credentialMemory);
            }

            var options = new NativeGetAssertionOptions(
                1, 120_000, credentials, default, 1,
                request.RequireUserVerification ? 1u : 2u, 0);
            var client = new NativeClientData(1, (uint)clientData.Json.Length, clientJson.Pointer, hashAlgorithm.Pointer);
            var hr = WebAuthNAuthenticatorGetAssertion(
                request.ParentWindowHandle,
                rpId.Pointer,
                ref client,
                ref options,
                out var result);
            ThrowIfFailed(hr, "Windows Hello passkey assertion");
            try
            {
                var assertion = Marshal.PtrToStructure<NativeAssertionPrefix>(result);
                return new NativePasskeyAssertion(
                    CopyBytes(assertion.Credential.pbId, assertion.Credential.cbId),
                    CopyBytes(assertion.pbAuthenticatorData, assertion.cbAuthenticatorData),
                    CopyBytes(assertion.pbSignature, assertion.cbSignature),
                    CopyBytes(assertion.pbUserId, assertion.cbUserId),
                    clientData.Json,
                    "internal",
                    request.RequireUserVerification);
            }
            finally
            {
                WebAuthNFreeAssertion(result);
            }
        }
        finally
        {
            if (credentialMemory != nint.Zero)
            {
                Marshal.FreeHGlobal(credentialMemory);
            }
        }
    }

    private static void EnsureAvailable()
    {
        if (!OperatingSystem.IsWindows() || NativePasskeyProbe.TryGetWebAuthnApiVersion() == 0)
        {
            throw new PlatformNotSupportedException("Windows WebAuthn is unavailable on this device.");
        }
    }

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"{operation} failed with HRESULT 0x{result:X8}.");
        }
    }

    private static byte[] CopyBytes(nint pointer, uint length)
    {
        if (pointer == nint.Zero || length == 0)
        {
            return [];
        }

        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, checked((int)length));
        return bytes;
    }

    [DllImport("webauthn.dll", EntryPoint = "WebAuthNAuthenticatorMakeCredential", ExactSpelling = true)]
    private static extern int WebAuthNAuthenticatorMakeCredential(
        nint hWnd, ref NativeRpInformation rp, ref NativeUserInformation user,
        ref NativeCoseCredentialParameters parameters, ref NativeClientData client,
        ref NativeMakeCredentialOptions options, out nint result);

    [DllImport("webauthn.dll", EntryPoint = "WebAuthNAuthenticatorGetAssertion", ExactSpelling = true)]
    private static extern int WebAuthNAuthenticatorGetAssertion(
        nint hWnd, nint rpId, ref NativeClientData client,
        ref NativeGetAssertionOptions options, out nint result);

    [DllImport("webauthn.dll", EntryPoint = "WebAuthNFreeCredentialAttestation", ExactSpelling = true)]
    private static extern void WebAuthNFreeCredentialAttestation(nint result);

    [DllImport("webauthn.dll", EntryPoint = "WebAuthNFreeAssertion", ExactSpelling = true)]
    private static extern void WebAuthNFreeAssertion(nint result);

    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeRpInformation(uint version, nint id, nint name, nint icon) { public readonly uint dwVersion = version; public readonly nint pwszId = id, pwszName = name, pwszIcon = icon; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeUserInformation(uint version, uint idLength, nint id, nint name, nint icon, nint displayName) { public readonly uint dwVersion = version, cbId = idLength; public readonly nint pbId = id, pwszName = name, pwszIcon = icon, pwszDisplayName = displayName; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeClientData(uint version, uint length, nint json, nint hash) { public readonly uint dwVersion = version, cbClientDataJson = length; public readonly nint pbClientDataJson = json; public readonly nint pwszHashAlgId = hash; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeCoseCredentialParameter(uint version, nint type, int algorithm) { public readonly uint dwVersion = version; public readonly nint pwszCredentialType = type; public readonly int lAlg = algorithm; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeCoseCredentialParameters(uint count, nint pointer) { public readonly uint cCredentialParameters = count; public readonly nint pCredentialParameters = pointer; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeCredential(uint version, uint idLength, nint id, nint type) { public readonly uint dwVersion = version, cbId = idLength; public readonly nint pbId = id, pwszCredentialType = type; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeCredentials(uint count, nint pointer) { public readonly uint cCredentials = count; public readonly nint pCredentials = pointer; }
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeExtensions
    {
        public readonly uint cExtensions;
        public readonly nint pExtensions;
        public NativeExtensions()
        {
            cExtensions = 0;
            pExtensions = nint.Zero;
        }
    }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeMakeCredentialOptions(uint version, uint timeout, NativeCredentials credentials, NativeExtensions extensions, uint attachment, int resident, uint verification, uint attestation, uint flags) { public readonly uint dwVersion = version, dwTimeoutMilliseconds = timeout; public readonly NativeCredentials CredentialList = credentials; public readonly NativeExtensions Extensions = extensions; public readonly uint dwAuthenticatorAttachment = attachment; public readonly int bRequireResidentKey = resident; public readonly uint dwUserVerificationRequirement = verification, dwAttestationConveyancePreference = attestation, dwFlags = flags; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeGetAssertionOptions(uint version, uint timeout, NativeCredentials credentials, NativeExtensions extensions, uint attachment, uint verification, uint flags) { public readonly uint dwVersion = version, dwTimeoutMilliseconds = timeout; public readonly NativeCredentials CredentialList = credentials; public readonly NativeExtensions Extensions = extensions; public readonly uint dwAuthenticatorAttachment = attachment, dwUserVerificationRequirement = verification, dwFlags = flags; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeCredentialAttestationPrefix { public readonly uint dwVersion; public readonly nint pwszFormatType; public readonly uint cbAuthenticatorData; public readonly nint pbAuthenticatorData; public readonly uint cbAttestation; public readonly nint pbAttestation; public readonly uint dwAttestationDecodeType; public readonly nint pvAttestationDecode; public readonly uint cbAttestationObject; public readonly nint pbAttestationObject; public readonly uint cbCredentialId; public readonly nint pbCredentialId; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeAssertionPrefix { public readonly uint dwVersion; public readonly uint cbAuthenticatorData; public readonly nint pbAuthenticatorData; public readonly uint cbSignature; public readonly nint pbSignature; public readonly NativeCredential Credential; public readonly uint cbUserId; public readonly nint pbUserId; }

    private sealed class HGlobalString(string value) : IDisposable
    {
        public nint Pointer { get; } = Marshal.StringToHGlobalUni(value);
        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }

    private sealed class PinnedBytes(byte[] value) : IDisposable
    {
        private readonly GCHandle _handle = value.Length == 0 ? default : GCHandle.Alloc(value, GCHandleType.Pinned);
        public int Length { get; } = value.Length;
        public nint Pointer => Length == 0 ? nint.Zero : _handle.AddrOfPinnedObject();
        public void Dispose() { if (_handle.IsAllocated) _handle.Free(); }
    }
}
