# Android MDBX payload fixtures

These redacted fixtures mirror the flat business payload written by Monica Android at commit `ad503a45`.

Authoritative sources:

- `app/src/main/java/takagi/ru/monica/repository/MdbxVaultStore.kt`, `passwordEntryMutation` and `secureItemEntryMutation`
- `app/src/main/java/takagi/ru/monica/viewmodel/MdbxViewModel.kt`, `importPasswordEntry`, `restoreCustomFields`, and `importSecureItem`

The MDBX native record owns the entry title, entry type, entry ID, project ID, and deletion state. These files contain only `payload_json`, so titles and deletion flags are supplied separately by codec tests.

All credentials and identifiers are synthetic. New Avalonia writes must use these snake_case field names without the legacy `{ kind, schemaVersion, data }` wrapper.
