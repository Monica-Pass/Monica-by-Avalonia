# Desktop MDBX native source

`native-source.json` pins the engine to the Android runtime's source baseline, commit
`90005c8c608c952093a4522ffa507a562e2e39a4`, plus the five patches recorded in
`Monica/Monica for Android/mdbx-engine/MDBX3_RUNTIME_PROVENANCE.json`. The baseline and
patches retain the upstream MIT license. Patches are stored without Git newline conversion.

The patches preserve JSON numbers and literal internal-marker keys, enforce bounded sync deltas,
reuse policy-bounded disclosure sessions and derive composite commit kinds. Building vanilla
upstream alone passes a simple vault smoke but omits those compatibility and data-preservation fixes.

`build-native.ps1` checks every patch's SHA-256, archives the pinned commit from the supplied source
repository into ignored build artifacts, applies patches only in that isolated copy, then verifies
the final overlapping files against the Android provenance after LF normalization. It uses a Rust
target matching the requested RID and `cargo --locked`; cross-compiling requires the target's linker
and SDK. It does not modify the caller's MDBX checkout. CI checks out the pinned source before publish.

The generated C# bindings remain unchanged. Native runtime checks must still prove that the staged
library is compatible and can create, read and update a canonical vault. A matching source hash or a
successful compile alone does not prove Android/desktop interoperability or passkey sync.
