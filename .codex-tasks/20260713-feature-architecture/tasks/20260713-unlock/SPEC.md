# Unlock Feature Extraction

## Goals

- Establish the feature-folder convention under `Monica.App/Features`.
- Move the locked-vault view out of `MainWindow.axaml`.
- Move cohesive unlock state and workflow code out of the monolithic ViewModel file.
- Preserve existing commands, bindings, smoke automation, and vault loading behavior.

## Non-Goals

- Redesigning the unlock screen.
- Changing vault credential formats or cryptographic behavior.
- Extracting the unlocked workspace in this delivery.

## Done-When

- Unlock code has feature locality.
- Compiled XAML bindings succeed.
- Existing tests pass.
- Changes are committed and pushed, then the application is launched for manual testing.
