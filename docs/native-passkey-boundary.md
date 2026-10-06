# Native Passkey Boundary

## Supported Desktop Scope

Monica Desktop preserves WebAuthn/FIDO2 metadata imported through supported vault formats. On Windows the native service can probe `webauthn.dll` with `WebAuthNGetApiVersionNumber` on demand. The startup capability catalog does not perform that native call, so first-frame rendering remains independent of the operating-system API probe.

The WebAuthn client API lets an application ask Windows to use its platform authenticator. Monica now has a native ceremony boundary for Windows: registration and assertion requests can invoke Windows Hello, and only the public credential metadata, credential id and signed WebAuthn response cross back into Monica. The Windows Hello private key remains managed by Windows.

The vault has two deliberate modes:

- `BW_COMPAT`: an exportable Monica-managed authenticator. It can travel between Android and desktop through the vault/Bitwarden-compatible metadata and is the cross-device fallback.
- `WINDOWS_HELLO`: a Windows platform credential. It offers the Windows Hello fingerprint, face or PIN prompt, but its private key is device-bound and cannot be exported to Android or another desktop.

## Unsupported Provider Scope

Monica Desktop does not claim to be a Windows system passkey provider. A browser-wide provider requires a separately packaged native WebAuthn plugin/credential-provider component, operating-system registration, lifecycle handling outside the Avalonia process, and a separate security review. Calling Windows Hello from Monica's own passkey ceremony is supported; intercepting every browser/app request is a different product boundary.

The native service therefore reports all of the following explicitly:

- whether the Windows WebAuthn client API is available;
- whether Windows reports a user-verifying platform authenticator (Windows Hello) available;
- the detected API version;
- `CanActAsWindowsCredentialProvider = false`;
- `PlatformLimited` status for the native-passkey integration.

## Security Boundary

The Windows boundary passes only the RP/user metadata and WebAuthn challenge required for the active ceremony. It never exports a Windows Hello private key. If the API or platform authenticator is absent, the operation fails explicitly; it does not silently create a roaming software key.

Android Credential Provider behavior remains Android-specific. Desktop metadata support must not be presented as equivalent to Android provider registration.
