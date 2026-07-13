# Progress Log

## Context Recovery Block

- **Current milestone**: #3 - Extract unlock ViewModel state and add focused coverage
- **Current status**: IN_PROGRESS
- **Last completed**: #2 - Extract unlock view
- **Current artifact**: `TODO.csv`
- **Key context**: Preserve `MainWindowViewModel` as DataContext in the first extraction to avoid a speculative one-adapter seam.
- **Known issues**: `StatusMessage` is shared across all features and remains in the shell until status reporting is redesigned.
- **Next action**: Move cohesive unlock state and workflow into the feature directory without changing credential behavior.

## Milestone 2: Extract unlock view

- **Status**: DONE
- **What was done**:
  - Created `Features/Unlock/UnlockView` with compiled bindings and local styles.
  - Replaced the locked-vault markup in `MainWindow` with the feature view.
- **Key decisions**:
  - Preserve `MainWindowViewModel` as DataContext until unlock state is extracted with focused tests.
- **Validation**: `dotnet build Monica.slnx --no-restore` -> 0 warnings, 0 errors; `dotnet test Monica.slnx --no-restore --no-build` -> 229 passed.
- **Next step**: Milestone 3 - Extract unlock ViewModel state and add focused coverage.
