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
        : this(platformIntegrationService, NativePasskeyProbe.TryGetWebAuthnApiVersion(),
            NativePasskeyProbe.TryIsUserVerifyingPlatformAuthenticatorAvailable())
    {
    }

    internal WindowsNativePasskeyService(
        IPlatformIntegrationService platformIntegrationService, uint version, bool uvAvailable)
    {
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
    // This adapter always requests a platform authenticator. The DLL alone is insufficient:
    // without Windows Hello the same operation would fail in EnsureAvailable.
    public bool IsAvailable => Support.IsWebAuthnClientApiAvailable &&
        Support.IsUserVerifyingPlatformAuthenticatorAvailable;
    public bool IsUserVerifyingPlatformAuthenticatorAvailable => Support.IsUserVerifyingPlatformAuthenticatorAvailable;

    public Task<NativePasskeyRegistration> CreateAsync(
        NativePasskeyCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        NativePasskeyValidation.ValidateCreate(request);
        var snapshot = request with { UserId = request.UserId.ToArray(), Challenge = request.Challenge.ToArray() };
        return RunAsync(cancel => CreateCore(snapshot, cancel), cancellationToken);
    }

    public Task<NativePasskeyAssertion> GetAssertionAsync(
        NativePasskeyAssertionRequest request,
        CancellationToken cancellationToken = default)
    {
        NativePasskeyValidation.ValidateAssertion(request);
        var snapshot = request with { CredentialId = request.CredentialId?.ToArray(), Challenge = request.Challenge.ToArray() };
        return RunAsync(cancel => GetAssertionCore(snapshot, cancel), cancellationToken);
    }

    public static PlatformIntegrationCapability CreateCapability()
    {
        return PlatformIntegrationService.PlatformLimited(
            PlatformFeatureKeys.NativePasskey,
            "Windows WebAuthn client API availability is probed on demand; Monica is not a packaged system credential provider.");
    }

    private static Task<T> RunAsync<T>(Func<nint, T> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            EnsureAvailable();
            ThrowIfFailed(WebAuthNGetCancellationId(out var operationId), "WebAuthn cancellation initialization");
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
            try
            {
                Marshal.StructureToPtr(operationId, pointer, false);
                using var registration = cancellationToken.Register(() => WebAuthNCancelCurrentOperation(ref operationId));
                cancellationToken.ThrowIfCancellationRequested();
                T response;
                try
                {
                    response = operation(pointer);
                }
                catch (Exception error) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("The WebAuthn operation was cancelled.", error, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                return response;
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }, cancellationToken);
    }

    private static NativePasskeyRegistration CreateCore(NativePasskeyCreateRequest request, nint cancellation)
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
                3, 120_000, default, default,
                1, request.Discoverable ? 1 : 0,
                request.RequireUserVerification ? 1u : 2u, 1, 0, cancellation);
            var client = new NativeClientData(1, (uint)clientData.Json.Length, clientJson.Pointer, hashAlgorithm.Pointer);
            var hr = WebAuthNAuthenticatorMakeCredential(
                request.ParentWindowHandle,
                ref rp,
                ref user,
                ref parameters,
                ref client,
                ref options,
                out var result);
            try
            {
                ThrowIfFailed(hr, "Windows Hello passkey registration");
                if (result == nint.Zero) throw new InvalidOperationException("Windows returned no passkey registration.");
                var attestation = Marshal.PtrToStructure<NativeCredentialAttestationPrefix>(result);
                var authData = CopyBytes(attestation.pbAuthenticatorData, attestation.cbAuthenticatorData);
                var returnedClientData = CopyCredentialAttestationClientData(result, attestation.dwVersion, clientData.Json);
                var metadata = ReadCredentialAttestationMetadata(result, attestation.dwVersion, request.Discoverable);
                return new NativePasskeyRegistration(
                    CopyBytes(attestation.pbCredentialId, attestation.cbCredentialId),
                    authData,
                    CopyBytes(attestation.pbAttestationObject, attestation.cbAttestationObject),
                    returnedClientData,
                    metadata.Transport,
                    metadata.IsDiscoverable,
                    HasUserVerification(authData));
            }
            finally
            {
                if (result != nint.Zero) WebAuthNFreeCredentialAttestation(result);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(parameterMemory);
        }
    }

    private static NativePasskeyAssertion GetAssertionCore(NativePasskeyAssertionRequest request, nint cancellation)
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
                3, 120_000, credentials, default, 1,
                request.RequireUserVerification ? 1u : 2u, 0, cancellation);
            var client = new NativeClientData(1, (uint)clientData.Json.Length, clientJson.Pointer, hashAlgorithm.Pointer);
            var hr = WebAuthNAuthenticatorGetAssertion(
                request.ParentWindowHandle,
                rpId.Pointer,
                ref client,
                ref options,
                out var result);
            try
            {
                ThrowIfFailed(hr, "Windows Hello passkey assertion");
                if (result == nint.Zero) throw new InvalidOperationException("Windows returned no passkey assertion.");
                var assertion = Marshal.PtrToStructure<NativeAssertionPrefix>(result);
                var authData = CopyBytes(assertion.pbAuthenticatorData, assertion.cbAuthenticatorData);
                var returnedClientData = CopyAssertionClientData(result, assertion.dwVersion, clientData.Json);
                return new NativePasskeyAssertion(
                    CopyBytes(assertion.Credential.pbId, assertion.Credential.cbId),
                    authData,
                    CopyBytes(assertion.pbSignature, assertion.cbSignature),
                    CopyBytes(assertion.pbUserId, assertion.cbUserId),
                    returnedClientData,
                    ReadAssertionTransport(result, assertion.dwVersion),
                    HasUserVerification(authData));
            }
            finally
            {
                if (result != nint.Zero) WebAuthNFreeAssertion(result);
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
        if (!OperatingSystem.IsWindows() || NativePasskeyProbe.TryGetWebAuthnApiVersion() == 0 ||
            !NativePasskeyProbe.TryIsUserVerifyingPlatformAuthenticatorAvailable())
        {
            throw new PlatformNotSupportedException("A Windows Hello platform authenticator is unavailable on this device.");
        }
    }

    internal static void ThrowIfFailed(int result, string operation)
    {
        // These are the two cancellation statuses documented by WebAuthNGetErrorName in
        // Microsoft's webauthn.h. NotAllowedError also includes missing credentials and
        // timeouts, so it must not be treated as cancellation as a whole.
        if (result is unchecked((int)0x800704C7) or unchecked((int)0x80090036))
        {
            throw new OperationCanceledException($"{operation} was cancelled.");
        }

        if (result < 0)
        {
            throw new InvalidOperationException($"{operation} failed with HRESULT 0x{result:X8}.");
        }
    }

    private static byte[] CopyBytes(nint pointer, uint length)
    {
        if (length == 0)
        {
            return [];
        }

        if (pointer == nint.Zero || length > NativePasskeyValidation.MaximumResponseBytes)
            throw new InvalidOperationException("The native WebAuthn response has invalid bounds.");

        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, checked((int)length));
        return bytes;
    }

    internal static byte[] CopyCredentialAttestationClientData(
        nint result,
        uint structureVersion,
        byte[] requestedClientData)
    {
        // pbClientDataJSON was added in WEBAUTHN_CREDENTIAL_ATTESTATION_VERSION_8.
        // Older Windows builds do not expose it, so the input WEBAUTHN_CLIENT_DATA is the
        // only client data available on those builds. Never read beyond the returned struct
        // for an older version.
        if (structureVersion < 8)
        {
            return requestedClientData;
        }

        var attestation = Marshal.PtrToStructure<NativeCredentialAttestation>(result);
        return CopyReturnedClientData(attestation.pbClientDataJSON, attestation.cbClientDataJSON, requestedClientData);
    }

    internal static byte[] CopyAssertionClientData(
        nint result,
        uint structureVersion,
        byte[] requestedClientData)
    {
        // pbClientDataJSON was added in WEBAUTHN_ASSERTION_VERSION_6.
        if (structureVersion < 6)
        {
            return requestedClientData;
        }

        var assertion = Marshal.PtrToStructure<NativeAssertion>(result);
        return CopyReturnedClientData(assertion.pbClientDataJSON, assertion.cbClientDataJSON, requestedClientData);
    }

    private static byte[] CopyReturnedClientData(nint pointer, uint length, byte[] requestedClientData)
    {
        // Legacy options supply WEBAUTHN_CLIENT_DATA directly and cannot ask Windows to
        // construct remote client data. New result versions may therefore leave this optional
        // output absent. In that case the exact input bytes were hashed by the authenticator.
        // A partially populated or oversized output is malformed and must never fall back.
        if (pointer == nint.Zero && length == 0)
        {
            return requestedClientData;
        }

        if (length == 0)
        {
            throw new InvalidOperationException("The native WebAuthn client data has invalid bounds.");
        }

        return CopyBytes(pointer, length);
    }

    internal static (string Transport, bool IsDiscoverable) ReadCredentialAttestationMetadata(
        nint result, uint structureVersion, bool requiredResidentKey)
    {
        // Read only fields belonging to the returned version. Marshaling the current full
        // structure here would overread older native allocations.
        var usedTransport = structureVersion >= 3
            ? ReadUInt32<NativeCredentialAttestation>(result, nameof(NativeCredentialAttestation.dwUsedTransport))
            : 0;
        var transport = structureVersion >= 8
            ? MapTransports(ReadUInt32<NativeCredentialAttestation>(result, nameof(NativeCredentialAttestation.dwTransports)))
            : MapUsedTransport(usedTransport);
        var isDiscoverable = structureVersion >= 4
            ? ReadUInt32<NativeCredentialAttestation>(result, nameof(NativeCredentialAttestation.bResidentKey)) != 0
            // Before v4, successful bRequireResidentKey is the only available guarantee.
            // A request that did not require it cannot tell us whether the result is resident.
            : requiredResidentKey;
        return (transport, isDiscoverable);
    }

    internal static string ReadAssertionTransport(nint result, uint structureVersion) =>
        structureVersion >= 4
            ? MapUsedTransport(ReadUInt32<NativeAssertion>(result, nameof(NativeAssertion.dwUsedTransport)))
            : string.Empty;

    private static uint ReadUInt32<T>(nint result, string field) where T : struct =>
        unchecked((uint)Marshal.ReadInt32(result, checked((int)Marshal.OffsetOf<T>(field))));

    private static string MapUsedTransport(uint transport) => transport switch
    {
        0x01 => "usb",
        0x02 => "nfc",
        0x04 => "ble",
        0x10 => "internal",
        0x20 => "hybrid",
        0x40 => "smart-card",
        _ => string.Empty
    };

    private static string MapTransports(uint transports)
    {
        List<string> known = [];
        foreach (var flag in new uint[] { 0x01, 0x02, 0x04, 0x10, 0x20, 0x40 })
        {
            if ((transports & flag) != 0) known.Add(MapUsedTransport(flag));
        }

        return string.Join(",", known);
    }

    private static bool HasUserVerification(byte[] authenticatorData) =>
        authenticatorData.Length >= 37 && (authenticatorData[32] & (byte)PasskeyAuthenticatorFlags.UserVerified) != 0;

    [DllImport("webauthn.dll", EntryPoint = "WebAuthNGetCancellationId", ExactSpelling = true)]
    private static extern int WebAuthNGetCancellationId(out Guid id);

    [DllImport("webauthn.dll", EntryPoint = "WebAuthNCancelCurrentOperation", ExactSpelling = true)]
    private static extern int WebAuthNCancelCurrentOperation(ref Guid id);

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
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeMakeCredentialOptions(uint version, uint timeout, NativeCredentials credentials, NativeExtensions extensions, uint attachment, int resident, uint verification, uint attestation, uint flags, nint cancellation) { public readonly uint dwVersion = version, dwTimeoutMilliseconds = timeout; public readonly NativeCredentials CredentialList = credentials; public readonly NativeExtensions Extensions = extensions; public readonly uint dwAuthenticatorAttachment = attachment; public readonly int bRequireResidentKey = resident; public readonly uint dwUserVerificationRequirement = verification, dwAttestationConveyancePreference = attestation, dwFlags = flags; public readonly nint pCancellationId = cancellation; public readonly nint pExcludeCredentialList = nint.Zero; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeGetAssertionOptions(uint version, uint timeout, NativeCredentials credentials, NativeExtensions extensions, uint attachment, uint verification, uint flags, nint cancellation) { public readonly uint dwVersion = version, dwTimeoutMilliseconds = timeout; public readonly NativeCredentials CredentialList = credentials; public readonly NativeExtensions Extensions = extensions; public readonly uint dwAuthenticatorAttachment = attachment, dwUserVerificationRequirement = verification, dwFlags = flags; public readonly nint pwszU2fAppId = nint.Zero, pbU2fAppId = nint.Zero, pCancellationId = cancellation, pAllowCredentialList = nint.Zero; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeCredentialAttestationPrefix { public readonly uint dwVersion; public readonly nint pwszFormatType; public readonly uint cbAuthenticatorData; public readonly nint pbAuthenticatorData; public readonly uint cbAttestation; public readonly nint pbAttestation; public readonly uint dwAttestationDecodeType; public readonly nint pvAttestationDecode; public readonly uint cbAttestationObject; public readonly nint pbAttestationObject; public readonly uint cbCredentialId; public readonly nint pbCredentialId; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeCredentialAttestation { public readonly uint dwVersion; public readonly nint pwszFormatType; public readonly uint cbAuthenticatorData; public readonly nint pbAuthenticatorData; public readonly uint cbAttestation; public readonly nint pbAttestation; public readonly uint dwAttestationDecodeType; public readonly nint pvAttestationDecode; public readonly uint cbAttestationObject; public readonly nint pbAttestationObject; public readonly uint cbCredentialId; public readonly nint pbCredentialId; public readonly NativeExtensions Extensions; public readonly uint dwUsedTransport; public readonly int bEpAtt, bLargeBlobSupported, bResidentKey, bPrfEnabled; public readonly uint cbUnsignedExtensionOutputs; public readonly nint pbUnsignedExtensionOutputs; public readonly nint pHmacSecret; public readonly int bThirdPartyPayment; public readonly uint dwTransports; public readonly uint cbClientDataJSON; public readonly nint pbClientDataJSON; public readonly uint cbRegistrationResponseJSON; public readonly nint pbRegistrationResponseJSON; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeAssertionPrefix { public readonly uint dwVersion; public readonly uint cbAuthenticatorData; public readonly nint pbAuthenticatorData; public readonly uint cbSignature; public readonly nint pbSignature; public readonly NativeCredential Credential; public readonly uint cbUserId; public readonly nint pbUserId; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeAssertion { public readonly uint dwVersion; public readonly uint cbAuthenticatorData; public readonly nint pbAuthenticatorData; public readonly uint cbSignature; public readonly nint pbSignature; public readonly NativeCredential Credential; public readonly uint cbUserId; public readonly nint pbUserId; public readonly NativeExtensions Extensions; public readonly uint cbCredLargeBlob; public readonly nint pbCredLargeBlob; public readonly uint dwCredLargeBlobStatus; public readonly nint pHmacSecret; public readonly uint dwUsedTransport; public readonly uint cbUnsignedExtensionOutputs; public readonly nint pbUnsignedExtensionOutputs; public readonly uint cbClientDataJSON; public readonly nint pbClientDataJSON; public readonly uint cbAuthenticationResponseJSON; public readonly nint pbAuthenticationResponseJSON; }

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
