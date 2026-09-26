# Android kotpass KDBX fixture

`android-kotpass-v1.kdbx` (sha256 `e9285ef62ffe30046a08d5df6e956873012d46e578dfd60f7770c91f42bffa87`,
1853 bytes) was written by **kotpass 0.10.0** - the exact coordinate Monica Android pins at
`app/build.gradle:319` - running on a JVM, not by anything in this repository. That is the point:
the desktop's KeePass support is measured against the bytes the other client actually produces.

Unlock password: `kdbx-parity-fixture-not-a-secret`. Not a secret; it exists so the fixture can be
unlocked in CI, and no test prints a field value.

## How it was produced

`eng/kdbx/KotpassShapeFixture.kt` is compiled against the cached kotpass jar and run through
`eng/kdbx/build-kotpass-fixture.sh` (needs the Android project's Gradle dependency cache). The
program mirrors Monica Android's own creation path: `KeePassDatabase.Ver4x.create` with
`Meta(generator = "Monica Password Manager")`, then the header copy that
`LocalKeePassViewModel.createConfiguredDatabase` performs, then `encode` with
`BaseCiphers.entries` as the cipher providers.

## What is inside, as measured by kotpass reading it back

| Shape | Measured |
|---|---|
| Format version | KDBX 4.1 |
| Outer cipher | AES-256, uuid `31c1f2e6-bf71-4350-be58-05216afc5aff` |
| KDF | Argon2d, parallelism 2, memory 33554432 bytes, iterations 8, algorithm version 0x13, 32-byte salt |
| Compression | GZip |
| Inner random stream | ChaCha20, 64-byte key |
| History settings | `historyMaxItems=10`, `historyMaxSize=6291456`, `maintenanceHistoryDays=365` |
| Memory protection | `Password` only |
| Groups | `Root` (kotpass writes the name the app passes) with one nested group `Work` |
| Entries | `parity-root-01` under `Root`, `parity-work-02` under `Work` |
| Fields per entry | `Title`, `UserName`, `Password` (protected), `URL`, `Notes`, `Reference`, `MonicaLocalId`; the nested one adds protected `Card Number` |
| Attachment | one 32-byte `parity.bin` referenced from the nested entry |
| AutoType | enabled on the nested entry with one association (`chrome.exe` -> `{USERNAME}{TAB}{PASSWORD}{ENTER}`) |
| History | the nested entry carries one older revision |
| Recycle bin / deleted objects | disabled / none - the bin shape is the second file below |

`keepassxc-cli 2.7.11 db-info` reads the same file and reports `AES 256` with
`Argon2d (8 rounds, 32768 KB)`, 2 groups and 2 entries, so the fixture is conformant KDBX and not
merely self-consistent.

## Deliberate content

The nested entry is the interesting one: it is the only way to tell whether a desktop save keeps
history, AutoType associations, attachments and protected custom fields instead of silently
dropping them. Regenerating the fixture changes its embedded timestamps, so the byte hash above is
a record of the committed copy, not an assertion.

# Android kotpass recycle-bin fixture

`android-kotpass-bin-v1.kdbx` (sha256 `3241113bf5606c1ad25e8bbcadcf4c4063f0fe92de1ffe119a7e279231f74c10`,
1597 bytes as committed) is written by the same program - `eng/kdbx/KotpassShapeFixture.kt`, `probe-bin`
mode - and exists for one question the first file cannot answer: what does a bin look like when the other
client made it, and can this client put an entry back out of it? `tests/Monica.Tests/KeePassAndroidBinShapeTests.cs`
reads it. Unlock password: the same non-secret.

## What is inside, as measured by kotpass reading its own write back

| Shape | Measured |
|---|---|
| Format version | KDBX 4.1, the only version with a slot for the folder an entry left |
| Header | Same cipher and Argon2d settings as the fixture above |
| Groups | `Root` with `Probe Origin` and `Recycle Bin` under it |
| Entries | `probe-live` under `Root`, `probe-binned` under `Recycle Bin` |
| `PreviousParentGroup` on the binned entry | `11111111-2222-3333-4444-555555555555`, i.e. `Probe Origin`, and it survives kotpass's own encode -> decode |
| Meta | `recycleBinEnabled=true`, `recycleBinUuid=66666666-…-000000000001`, `recycleBinChanged=2026-02-03T04:05:06Z` |
| Deletion list | One record: `66666666-…-000000000004` at `2026-02-03T04:05:06Z`, for a uuid that is nowhere in the tree |
| History / attachments | none |

The deletion record is two fields because that is all kotpass has: `javap` on the pinned
`app.keemobile.kotpass/0.10.0` jar gives `DeletedObject` a `(UUID, Instant)` constructor only, its XML
tags are `DeletedObjects/Object/DeletedObject/UUID/DeletionTime`, and neither `HistoryPos` nor
`DeleteRemovalTime` appears anywhere in its xml classes. `PreviousParentGroup` is marshalled on the
entry, not on the deletion record - which is why the desktop's `RecordDeletions` writing uuid plus time
is the matching shape, and why a restore has to read the pointer off the entry itself.

Every timestamp in the file is pinned, so the decoded shape above is stable across regenerations. The
bytes are not: kotpass draws a fresh KDF salt each time it builds a database, so the hash above records
the committed copy rather than asserting on a rebuild.
