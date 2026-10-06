# Native Passkey Boundary

## Current implementation and evidence

Monica has a software authenticator engine, an encrypted SQLite private-key store and a Windows
WebAuthn client adapter. The adapter calls `WebAuthNAuthenticatorMakeCredential` and
`WebAuthNAuthenticatorGetAssertion`, supports native cancellation, reads UP/UV from returned
authenticator data, uses the WebAuthn DER signature format for native ES256 assertions and reads the
client data returned by the native response structure. Registration responses include the public
attestation/client data needed by a relying party.

Software ES256 assertions now use the same ASN.1 DER wire format. Regression tests verify Monica
output with independent .NET cryptography, reject otherwise valid raw P1363 signatures and verify the
published W3C assertion vector. RSA key and signature formats are unchanged.

Compilation and simulated authenticator tests are evidence for this boundary. They do not prove a
physical Windows Hello registration or website sign-in. The desktop management workspace now lists
and searches local records, shows public account/storage metadata and confirms deletion. Platform-owned
records can be removed from Monica without claiming to delete the OS credential. The page does not
offer a standalone fake website registration or an unverified signing operation. Metadata and software
private keys are stored in SQLite; they are not included in canonical MDBX snapshots or vault sync.

Credential and private-key writes/deletes share a SQLite transaction. Key rotation removes unreferenced
keys, duplicate equivalent credential IDs are rejected within an RP, and master-password maintenance
rewraps encrypted passkey material without changing its reference or PKCS#8 format. App-injected
passkey operations require an unlocked vault session and capture its cancellation token, so locking
then immediately unlocking again cannot revive an old operation. The workspace drops held account
projections and pending confirmations when locked or released to the background.

## Two different ownership models

- `BW_COMPAT` stores a Monica-managed software key in encrypted application storage. The existing
  engine can create and sign using it, but complete backup/sync and canonical MDBX integration still
  require implementation and round-trip acceptance tests.
- `WINDOWS_HELLO` invokes a Windows platform authenticator. Monica gets public metadata and signed
  responses, with no Windows private-key export API. This adapter is for native client ceremonies;
  it does not register Monica as a system passkey manager.

Other mode names in `PasskeyModes` are storage/provider identifiers, not implemented native adapters
in this Avalonia process. The three desktop targets are Windows, macOS and Linux: macOS needs a
separate AuthenticationServices Credential Provider Extension, while Linux needs a browser/FIDO2
route selected by the active desktop environment. Apple and Android platform services may themselves
sync passkeys. Their provider implementations must not be described as universally device-bound or as
exporting keys into Monica.

## System passkey manager is the target

The code exposes these as separate platform capabilities: `native-passkey` means Monica can start a
platform ceremony from its own process; `system-passkey-provider` means the operating system/browser
can dispatch WebAuthn requests to Monica. A platform can have the first capability without the second.

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
- Cancellation must cancel the Windows ceremony and prevent late Monica persistence or output. If
  Windows has already created a credential, this boundary does not automatically delete that OS key.
- Input RP/origin checks are not a replacement for trusted browser-origin validation or public-suffix
  checks. A system/browser provider must validate the requesting client before it reaches this engine.
- The low-level software engine's UP/UV flags require an operation-level consent and verification
  boundary in the future provider; merely exposing a create/sign button would not establish that proof.
- Windows controls the available fingerprint, face and PIN methods. Monica does not read biometric
  templates and cannot force a fingerprint method that the device has not configured.
- Full acceptance requires real OS-provider registration, a trusted WebAuthn test relying party,
  cancel/lock tests, credential deletion, browser selection/autofill and cross-device recovery tests.

Official references:

- [Windows WebAuthn and plugin APIs](https://learn.microsoft.com/en-us/windows/security/identity-protection/hello-for-business/webauthn-apis)
- [Microsoft Passkey Manager sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/passkeymanager/)
- [WebAuthn signature formats](https://www.w3.org/TR/webauthn-3/#sctn-signature-attestation-types)
