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

`.kdbx` decode is measured against databases written by KeePassXC 2.7.11 (KDBX4, AES-256,
AES-KDF 1,000,000 rounds) opened through `KeePassVaultService.OpenAsync`. The eager snapshot
list is gone: a locked file now becomes a session that indexes groups and counts on open and
resolves each entry's secrets, custom fields and attachment bytes one at a time while the
caller enumerates it.

Console harness, 128 MB working-set sampling:

| entries | file bytes | open | stream every detail | peak private | working set |
| --- | --- | --- | --- | --- | --- |
| 2,000 (20 groups) | 123,998 | 676 ms | 72 ms | +21 MB | 55 MB |
| 20,000 (20 groups) | 1,185,246 | 1,742 ms | 295 ms | +74 MB | 115 MB |

Decoding is correct at third-party fidelity: 20,000 of 20,000 `otp` seeds, URLs and protected
password strings came back, and the same holds for KDBX 3.1. Neither the crypto nor the
projection dominates the cost any more, and the model is not pinned: closing the session and
releasing it makes the whole decrypted graph collectable, which
`KeePass_released_session_lets_the_decrypted_model_be_collected` now guards. An earlier draft
of this page claimed `KeePassLib` kept ~2.45 KB per entry permanently (49 MB at 20,000
entries); that was a harness artifact and is wrong.

Whether the process gives the memory back is now measured on the shipped artifact rather than
in a harness. `--smoke-ui-keepass-file` (with `--smoke-ui-keepass-password`,
`--smoke-ui-keepass-stream-details` and the optional `--smoke-ui-keepass-max-growth-mb` gate)
runs `KeePassVaultService.OpenAsync` inside the locked app and reports private bytes at every
stage. Both ends of the growth measurement are compacted first: an earlier draft sampled a
baseline that still held other smoke phases' uncollected garbage and reported a flattering
−27 MB. win-x64 jit, unlocked state:

| entries | open ms | stream ms | before | open | streamed | after collect | growth |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 2,000 | 539 | 54 | 75.2 MB | 85.1 | 93.5 | 74.4 | −0.8 MB |
| 20,000 | 2,297 | 371 | 76.2 MB | 148.4 | 150.1 | 81.6 | +5.4 MB |

Nothing is retained. At 20,000 entries the app costs ~72 MB of private bytes while the file is
open (working set 167.7 → 244.3 MB, peak 251.4 MB, managed heap 24.1 → 81.3 MB) and `Dispose()`
is what returns it: the managed heap is back at 26.3 MB and the working set at 180.4 MB, so the
residual ~5 MB is heap the runtime kept, not a pinned decrypted graph. Streaming all 20,000
details costs only +1.7 MB over open, which is the one-entry-at-a-time resolution holding in the
real app. After the probe the same run still passes the 120 MB locked budget (82.6 MB). The
console harness's "+52 MB and the root group still reachable" is confirmed as its own artifact:
top-level statements keep locals alive to the end of `Main`, and the identical pattern there
refused to release a plain 40 MB array. The honest cost of opening a 20,000-entry `.kdbx` is a
temporary ~150 MB private / ~250 MB working set.

Two `KeePassLib` constraints found while making this green, both of which bind the write-back
slice:

- **`.kdbx` saves must be serialized process-wide.** Concurrent `KdbxFile.Save` calls are not
  thread safe: 6 of 240 files written in parallel rejected the key that created them, while 72
  written serially never failed. Silent data loss on a user's real vault is the failure mode,
  so any future save path needs one gate, not a per-database lock.
- **Concurrent unlocks are safe, but only if `Close()` always runs.** 180 parallel opens of 12
  distinct databases, with two held open at a time, produced zero failures both with and
  without a global lock. Ownership is guarded in `OpenCore` with a `finally` that closes the
  database whenever ownership was not transferred.

