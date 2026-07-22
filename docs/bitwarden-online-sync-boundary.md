# Bitwarden Online Sync Boundary

Monica's desktop Bitwarden integration follows the Android product behavior while using desktop-owned protocol, persistence, transport, and presentation layers.

## Layer ownership

`Monica.Core/Bitwarden` owns protocol constants, endpoint validation, KDF policy, key derivation, and CipherString cryptography. It has no HTTP, database, or UI dependencies.

`Monica.Data/Bitwarden` will own encrypted account records, remote folder metadata, pending operations, and conflict backups. Secret columns use Monica's unlocked-vault AEAD envelope with the `vault:v1:` prefix.

`Monica.Platform/Bitwarden` will own HTTPS requests, certificate policy, token refresh, and the in-memory account session. Locking the Monica vault clears all Bitwarden session keys and tokens.

`Monica.App/Features/Sync/Bitwarden` will own the WinUI-style account, sign-in, synchronization status, and conflict views. View models depend on service interfaces and never implement protocol cryptography.

## Protocol security limits

Only absolute HTTPS endpoint bases are accepted. Embedded credentials, queries, fragments, backslashes, and encoded path separators are rejected. Custom ports and normalized subpaths remain available for self-hosted servers. Certificate and hostname validation stays with the system trust stack unless a later explicit custom-CA policy adds trust without disabling hostname verification.

Server-provided KDF values are checked before expensive work. PBKDF2-SHA256 is capped at 2,000,000 iterations. Argon2id is capped at 10 iterations, 256 MB, and parallelism 16. Accounts above these desktop resource limits receive an explicit unsupported-parameter result instead of allocating attacker-controlled memory.

Master keys are 32 bytes. Argon2id uses SHA-256 of the canonical lower-case email as its salt. The authentication hash uses PBKDF2-SHA256 with the master key as seed, the master password as salt, and one iteration. Stretched encryption and MAC keys use HKDF-Expand SHA-256 with `enc` and `mac` info values.

CipherString handling currently accepts authenticated Type 2 values only: AES-256-CBC with PKCS7 padding and HMAC-SHA256 over `IV || ciphertext`. HMAC is verified with a fixed-time comparison before decryption. Encoded and decoded lengths are bounded before large allocations. Temporary byte arrays and owned key material are cleared when their lifetime ends.

Unauthenticated Type 0 CipherStrings remain unsupported for online synchronization. Supporting legacy content requires a separate compatibility decision with explicit integrity warnings and tests.
