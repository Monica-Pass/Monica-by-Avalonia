# Bitwarden Online Sync Boundary

Monica desktop follows the Monica Android product contract for Bitwarden online
sync while keeping protocol, persistence, transport, and presentation ownership
inside explicit desktop layers.

## Implemented synchronization flow

The desktop application currently implements:

- Account sign-in against official or self-hosted HTTPS endpoints.
- Supported two-factor challenges without persisting the one-time code.
- Lock-aware in-memory sessions and access-token refresh.
- Upload of queued local mutations before downloading the remote vault.
- Authenticated remote vault download, bounded CipherString decoding, and local merge.
- Nested remote folder metadata.
- Conflict backups and retry classification for pending operations.
- A WinUI-style account, sign-in, synchronization status, pending-change, and
  conflict workflow.

Only one synchronization run per account is owned at a time. Locking Monica
stops the active run, clears the session manager, invalidates outstanding
secret leases, and clears transient authentication UI state.

## Layer ownership

`Monica.Core/Bitwarden` owns protocol constants, endpoint validation, KDF
policy, key derivation, CipherString cryptography, secret containers, merge
decisions, retry policy, and the lock-aware in-memory session lifecycle. It has
no HTTP, database, or UI dependencies.

`Monica.Data/Bitwarden` owns encrypted account records, remote folder
metadata, pending operations, conflict backups, mutation processing, pull
application, and synchronization coordination. It never performs protocol
cryptography in a ViewModel.

`Monica.Platform/Bitwarden` owns HTTPS identity requests, vault download,
mutation transport, certificate policy, token refresh, and remote payload
decoding. It consumes short-lived leases from the Core session manager.

`Monica.App/Features/Sync/Bitwarden` owns the desktop account, sign-in,
two-factor, synchronization status, pending-change, and conflict presentation.
ViewModels depend on service interfaces and expose only task-oriented UI state.

## Persistent-secret boundary

Account PII, tokens, Bitwarden keys, synchronization errors, custom certificate
paths, and certificate passwords use Monica's unlocked-vault AEAD envelope
with the `vault:v1:` prefix. Pending-operation payloads and conflict backups
use the same protected boundary.

Lookup indexes use one-way SHA-256 identifiers instead of plaintext email
addresses. Secret material is decrypted only while the Monica vault is
unlocked and is returned through owned, disposable containers or short-lived
leases.

## Endpoint and transport policy

Only absolute HTTPS endpoint bases are accepted. Embedded credentials, queries,
fragments, backslashes, and encoded path separators are rejected. Custom ports
and normalized subpaths remain available for self-hosted servers.

Certificate and hostname validation remains with the operating-system trust
stack. Monica does not expose a “trust all certificates” path. A future custom
CA feature must add trust without disabling hostname verification.

HTTP payloads, error bodies, encoded fields, and decoded fields have explicit
size limits. Authentication and synchronization failures publish sanitized
messages instead of raw tokens, passwords, or server payloads.

## KDF and CipherString limits

Server-provided KDF values are checked before expensive work:

- PBKDF2-SHA256 is capped at 2,000,000 iterations.
- Argon2id is capped at 10 iterations, 256 MB, and parallelism 16.
- Accounts above these limits receive an explicit unsupported-parameter result
  instead of allocating attacker-controlled resources.

Master keys are 32 bytes. Argon2id uses SHA-256 of the canonical lowercase
email as salt. The authentication hash uses PBKDF2-SHA256 with the master key
as seed, the master password as salt, and one iteration. Stretched encryption
and MAC keys use HKDF-Expand SHA-256 with `enc` and `mac` info values.

CipherString handling accepts authenticated Type 2 values only:
AES-256-CBC with PKCS7 padding and HMAC-SHA256 over `IV || ciphertext`.
HMAC is verified with a fixed-time comparison before decryption. Encoded and
decoded lengths are bounded before allocation, and owned temporary byte arrays
and key material are cleared when their lifetime ends.

## Deliberate compatibility limits

Unauthenticated Type 0 CipherStrings remain unsupported for online sync.
Supporting legacy unauthenticated content requires a separate compatibility
decision with explicit integrity warnings and tests.

The desktop implementation does not claim that every Bitwarden server feature
or every Android integration is present. Unsupported remote content must fail
closed or remain untouched; it must not be silently rewritten into a lossy
local representation.

## Evidence

The primary automated evidence is in:

- `BitwardenProtocolTests.cs`
- `BitwardenNetworkAuthenticationTests.cs`
- `BitwardenSyncTransportTests.cs`
- `BitwardenMutationProcessorTests.cs`
- `BitwardenPullMergeServiceTests.cs`
- `BitwardenSyncCoordinatorTests.cs`
- `BitwardenSyncWorkflowUiTests.cs`
