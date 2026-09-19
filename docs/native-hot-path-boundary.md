# Native Core Boundary and Hot-Path Inventory

Every number here is measured, not estimated. Re-measure with
`Monica.App --benchmark-vault <entries>` for the storage rows, and see *Open unknowns* for
what is deliberately still unmeasured.

## Where the boundary is today

One self-owned native library crosses into the desktop app: `mdbx_ffi` (1,074 generated
bindings, uniffi, contract-versioned, one folder per runtime identifier under
`src/Monica.Platform/Mdbx/runtimes/<rid>/`). Everything else is seven operating-system
declarations: `user32.dll` ×5, `kernel32.dll` ×1, `webauthn.dll` ×1.

The rule: work joins the native core when it is batch, deterministic, free of UI-thread and
managed-object dependencies, **and** it either shows up in the measured inventory below or it
cannot stay inside the process memory budget in managed code. Two of those already apply to
`mdbx_ffi`; a third party native library is not permitted to enter silently.

## Storage path: measured through the published win-x64 JIT artifact

| entries | cold load | search median | search p95 | create | update | delete | db bytes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 5,000 | 186.9 ms | 0.67 ms | 1.01 ms | 27.8 ms | 23.3 ms | 11.7 ms | 2,397,656 |
| 20,000 | 506.4 ms | 3.40 ms | 4.46 ms | 31.1 ms | 21.8 ms | 11.4 ms | 12,190,136 |

Four times the entries costs 2.7 times the cold load and search stays at single-digit
milliseconds. The volume path is already native and already fast: **no further Rust work is
justified in storage.** Seeding (97 ms → 529 ms) is the one super-linear phase and is fixture
generation, not a user-facing path.

## Crypto path: measured against `Monica.Core` in Release

| work | time | peak private bytes |
| --- | --- | --- |
| desktop master key, PBKDF2-SHA256 600k | 86 ms | — |
| managed Argon2id `t=2 m=16MB p=2` | 104 ms | +19 MB |
| managed Argon2id `t=3 m=64MB p=2` (the record's own default) | 260 ms | +65 MB |
| managed Argon2id `t=2 m=256MB p=2` (policy ceiling) | 481 ms | +242 MB |
| 14,000 AES-GCM field encrypts | 101 ms | — |
| 14,000 AES-GCM field decrypts | 75 ms | — |

AES-GCM per entry is not a bottleneck: reading every secret in a 20,000-entry vault costs
75 ms, so bulk decryption is not why the security-analysis pass is slow.

**The retention measurement is the finding.** A Bitwarden server chooses the Argon2 parameters,
and `BitwardenKdfPolicy` permits up to 256 MB. After such a derivation, the managed heap
reports 1 MB committed and 0 MB alive, yet the process still holds 270 MB of private bytes —
after a full GC, forced LOH compaction, and four seconds idle. The same 256 MB allocated
off-heap and freed returns to 8 MB. Konscious holds Argon2's memory-hard cost on the managed
heap, where it is never handed back, so one sync against a high-memory KDF leaves the process
permanently above the 120 MB budget the artifact gate enforces.

This is the second place the native core is warranted, and it is warranted by memory rather
than by CPU time: `crates/mdbx-crypto` already depends on the `argon2` crate, and native
allocation is returned on drop.

## Open unknowns

The ~619 ms first-navigation security-analysis outlier is unattributed. It is not the
strength analyzer (four linear scans per password) and not bulk decryption (75 ms). The
remaining passes cannot be timed today because the rules are private `MainWindowViewModel`
methods and the benchmark fixture stores secrets in plaintext, so no crypto crosses the
repository. Attribution depends on lifting the analysis use-case out of the view model.

`.kdbx` decode is now measured, against databases written by KeePassXC 2.7.11 (KDBX4, AES-256,
AES-KDF 1,000,000 rounds) read through `KeePassVaultService.ReadAsync`:

| entries | file bytes | decode | private bytes after decode |
| --- | --- | --- | --- |
| 2,000 (21 groups) | 123,998 | 378 ms | 33 MB |
| 20,000 (21 groups) | 1,185,246 | 987 ms | 116 MB |

Decoding is correct at third-party fidelity: 20,000 of 20,000 `otp` seeds, URLs and protected
password strings all came back. The problem is not crypto and not CPU — it is the shape of the
result. A 1.18 MB file becomes a fully materialized list of 20,000 snapshots holding 116 MB of
private bytes, which is 96% of the 120 MB budget the artifact gate enforces, before the shell,
the tree or any entry detail exists. The same vault opened through the MDBX store costs 506 ms
and stays inside budget.

So the first rule for the KeePass arc: **change the data shape before adding features.** A
locked `.kdbx` must be read lazily, keeping the decrypted database behind the session and
projecting rows on demand, the way `KeePassNativeBrowser` does on Android. Streaming the
snapshot list would trade a few milliseconds for a footprint that scales with the screen
rather than with the file.

