using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Data;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanUnlockVault))]
    private async Task UnlockAsync()
    {
        if (IsUnlocking)
        {
            return;
        }

        IsUnlocking = true;
        HasUnlockError = false;
        try
        {
            HasPendingLegacyBusinessData = false;
            AppDiagnostics.Info(
                $"Unlock requested. initialized={IsVaultInitialized}, " +
                $"legacyImportRequired={_legacyVaultDetection.RequiresImport}");
            var result = await AppDiagnostics.MeasureAsync(
                "Unlock credential verification",
                () => _vaultUnlockCoordinator.UnlockOrCreateAsync(
                    MasterPassword,
                    ConfirmMasterPassword,
                    _legacyVaultDetection));
            AppDiagnostics.Info($"Unlock result={result.Status}");

            switch (result.Status)
            {
                case VaultUnlockStatus.MissingPassword:
                case VaultUnlockStatus.LegacyImportRequired:
                case VaultUnlockStatus.PasswordTooShort:
                case VaultUnlockStatus.ConfirmationMismatch:
                    IsVaultInitialized = result.IsVaultInitialized;
                    SetUnlockError(_localization.Get(result.MessageKey));
                    return;
                case VaultUnlockStatus.WrongPassword:
                    IsVaultInitialized = result.IsVaultInitialized;
                    _cryptoService.Lock();
                    IsUnlocked = false;
                    ClearVaultAccessSecrets();
                    SetUnlockError(_localization.Get(result.MessageKey));
                    return;
                case VaultUnlockStatus.Failed:
                    IsVaultInitialized = result.IsVaultInitialized || DefaultVaultDatabaseExists();
                    _cryptoService.Lock();
                    IsUnlocked = false;
                    if (result.Error is not null)
                    {
                        AppDiagnostics.Error("Unlock coordinator reported failure", result.Error);
                    }

                    ClearVaultAccessSecrets();
                    SetUnlockError(_localization.Get("VaultAccessUnlockFailed"));
                    return;
                case VaultUnlockStatus.CreatedAndUnlocked:
                case VaultUnlockStatus.Unlocked:
                    IsVaultInitialized = result.IsVaultInitialized;
                    await ReloadSettingsAfterUnlockAsync();
                    IsUnlocked = true;
                    HasPendingLegacyBusinessData = result.LegacyBusinessDataPending;
                    ClearVaultAccessSecrets();
                    StatusMessage = _localization.Format(
                        "VaultUnlockedLoadingFormat",
                        _localization.Get(result.MessageKey));
                    _ = LoadAfterUnlockAsync();
                    return;
                default:
                    _cryptoService.Lock();
                    IsUnlocked = false;
                    ClearVaultAccessSecrets();
                    SetUnlockError(_localization.Get("VaultAccessUnlockFailed"));
                    return;
            }
        }
        catch (Exception exception)
        {
            _cryptoService.Lock();
            IsUnlocked = false;
            AppDiagnostics.Error("Unlock workflow failed", exception);
            ClearVaultAccessSecrets();
            SetUnlockError(_localization.Get("VaultAccessUnlockFailed"));
        }
        finally
        {
            IsUnlocking = false;
        }
    }

    private async Task ReloadSettingsAfterUnlockAsync()
    {
        await _settingsService.LoadAsync();
        ApplySettings(_settingsService.Current);
        ResumeSettingsSave();
    }

    private bool CanUnlockVault() =>
        IsVaultAccessReady &&
        !IsUnlocking &&
        !_legacyVaultDetection.RequiresImport &&
        !string.IsNullOrEmpty(MasterPassword) &&
        (IsVaultInitialized || IsCreateVaultPasswordInputValid);

    private static bool DefaultVaultDatabaseExists()
    {
        try
        {
            return File.Exists(MonicaAppDataPaths.GetDatabasePath());
        }
        catch
        {
            return false;
        }
    }
}
