# Avalonia Feature Architecture

## Goal

- Restructure Monica by Avalonia into maintainable feature modules, using Monica Android's feature-oriented packages as the reference.

## Non-Goals

- Do not redesign storage formats or change vault compatibility.
- Do not change user-visible behavior during structural extraction unless a defect blocks validation.

## Constraints

- Preserve compiled Avalonia bindings and existing desktop behavior.
- Keep each delivery independently buildable, testable, committable, and runnable.
- Push every completed delivery to `origin/main` before manual testing.
- Add no external dependency unless a later feature requires it.

## Risk Assessment

- `MainWindowViewModel`, `MainWindow.axaml`, and window code-behind are highly coupled and require incremental extraction.
- Unlock and vault loading are security-sensitive and must retain existing credential behavior.
- One settings persistence test is timing-sensitive; isolated reruns currently pass.

## Child Deliverables

- Extract the unlock feature and locked shell surface.
- Extract the password vault feature.
- Extract secure notes.
- Extract TOTP and wallet features.
- Extract settings, sync, import/export, and MDBX management.
- Reduce the main window to application shell coordination and add headless UI coverage.

## Dependency Notes

- Each feature extraction builds on the shell conventions established by the unlock extraction.

## Done-When

- [ ] Every row in `SUBTASKS.csv` is `DONE`.
- [ ] Main window files contain shell coordination rather than feature implementations.
- [ ] Full build and test suite pass.
