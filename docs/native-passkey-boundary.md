# Native Passkey Boundary

## Supported Desktop Scope

Monica Desktop preserves WebAuthn/FIDO2 metadata imported through supported vault formats. On Windows the native service can probe `webauthn.dll` with `WebAuthNGetApiVersionNumber` on demand. The startup capability catalog does not perform that native call, so first-frame rendering remains independent of the operating-system API probe.

The WebAuthn client API lets an application ask Windows to use an authenticator. It does not register Monica as a credential provider for passkey requests initiated by browsers or other applications.

## Unsupported Provider Scope

Monica Desktop does not claim to be a Windows system passkey provider. That capability requires a packaged native credential-provider extension, operating-system registration, lifecycle handling outside the Avalonia process, and a separate security review.

The native service therefore reports all of the following explicitly:

- whether the Windows WebAuthn client API is available;
- the detected API version;
- `CanActAsWindowsCredentialProvider = false`;
- `PlatformLimited` status for the native-passkey integration.

## Security Boundary

The Windows probe calls only the version function and does not pass credential IDs, relying-party data, challenges, private key material, or vault state across the native boundary. No fallback credential creation or assertion flow is attempted when the API is absent.

Android Credential Provider behavior remains Android-specific. Desktop metadata support must not be presented as equivalent to Android provider registration.
