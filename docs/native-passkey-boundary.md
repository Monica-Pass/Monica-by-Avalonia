# Native Passkey Boundary

## Current implementation and evidence

Monica has a software authenticator engine, an encrypted SQLite private-key store and a Windows
WebAuthn client adapter. The adapter calls `WebAuthNAuthenticatorMakeCredential` and
`WebAuthNAuthenticatorGetAssertion`, supports native cancellation, reads UP/UV from returned
authenticator data, and uses the WebAuthn DER signature format for native ES256 assertions.
Registration responses include the public attestation/client data needed by a relying party.

Compilation and simulated authenticator tests are evidence for this boundary. They do not prove a
physical Windows Hello registration or website sign-in. The desktop currently has no complete passkey
management workspace or browser/system-provider entry point. Platform registration metadata is
stored in SQLite; the passkey material is not yet a complete canonical MDBX cross-device feature.

## Two different ownership models

- `BW_COMPAT` stores a Monica-managed software key in encrypted application storage. The existing
  engine can create and sign using it, but complete backup/sync and canonical MDBX integration still
  require implementation and round-trip acceptance tests.
- `WINDOWS_HELLO` invokes a Windows platform authenticator. Monica gets public metadata and signed
  responses, with no Windows private-key export API. This adapter is for native client ceremonies;
  it does not register Monica as a system passkey manager.

Other mode names in `PasskeyModes` are reserved identifiers, not implemented native adapters.
Apple and Android platform services may themselves sync passkeys. Their provider implementations
must not be described as universally device-bound or as exporting keys into Monica.

## System passkey manager is the target

Microsoft's Windows WebAuthn Plugin API provides `IPluginAuthenticator`, plugin registration,
credential metadata caching, cancellation and Windows Hello user verification. This is distinct from
the Windows logon Credential Provider API. A Monica system passkey manager needs a packaged COM
plugin, authenticated request verification and an authenticated vault broker, plus create/get/delete
and registration rollback handling. The user must install and enable that provider in Windows
Settings. Calling the WebAuthn client API alone does not provide browser-wide integration.

The native support service keeps `CanActAsWindowsCredentialProvider = false`; this is a compatibility
property and must not be interpreted as plugin registration or as Windows account logon support.

## Security and acceptance boundaries

- Validate RP/origin, challenge sizes, algorithm and user handle before invoking native UI.
- Native results must match the challenge, origin, credential id, RP hash and required UV.
- Preserve returned authenticator data and its signature; do not synthesize verification flags.
- Cancellation must cancel the Windows ceremony and prevent late persistence or output.
- Windows controls the available fingerprint, face and PIN methods. Monica does not read biometric
  templates and cannot force a fingerprint method that the device has not configured.
- Full acceptance requires real OS-provider registration, a trusted WebAuthn test relying party,
  cancel/lock tests, credential deletion, browser selection/autofill and cross-device recovery tests.

Official references:

- [Windows WebAuthn and plugin APIs](https://learn.microsoft.com/en-us/windows/security/identity-protection/hello-for-business/webauthn-apis)
- [Microsoft Passkey Manager sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/passkeymanager/)
- [WebAuthn signature formats](https://www.w3.org/TR/webauthn-3/#sctn-signature-attestation-types)
