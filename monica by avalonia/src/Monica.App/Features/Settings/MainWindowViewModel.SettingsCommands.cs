using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void SelectSettingsPage(string? page)
    {
        SelectedSettingsPage = NormalizeSettingsPage(page);
    }

    private static string NormalizeSettingsPage(string? page) =>
        page?.Trim().ToLowerInvariant() switch
        {
            "security" => "Security",
            "securityrecovery" or "security-recovery" or "recovery" => "SecurityRecovery",
            "data" or "datamanagement" or "data-management" => "Data",
            "desktop" => "Desktop",
            "integrations" or "platform" => "Integrations",
            "about" => "About",
            "danger" or "dangerzone" or "danger-zone" => "Danger",
            _ => "General"
        };

    [RelayCommand(CanExecute = nameof(CanOpenExternalLinks))]
    private async Task OpenGitHubRepositoryAsync()
    {
        try
        {
            await _externalLinkService.OpenAsync(new Uri(GitHubRepositoryUrl, UriKind.Absolute));
            StatusMessage = _localization.Get("GitHubRepositoryOpened");
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("GitHubRepositoryOpenFailedFormat", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ClearVaultDataAsync(string? scope)
    {
        if (!IsUnlocked)
        {
            StatusMessage = _localization.Get("VaultLocked");
            return;
        }

        if (!TryBeginSecurityMaintenance(() => IsClearingVaultData = true))
        {
            return;
        }

        var clearScope = scope?.ToLowerInvariant() switch
        {
            "passwords" => VaultClearScope.Passwords,
            "secureitems" or "secure-items" => VaultClearScope.SecureItems,
            _ => VaultClearScope.All
        };

        try
        {
            var requiredPhrase = _localization.Get("ClearVaultConfirmationPhrase");
            var confirmed = await _confirmationDialogService.ConfirmTypedAsync(
                _localization.Get("ClearVaultTypedConfirmationTitle"),
                _localization.Format("ClearVaultTypedConfirmationMessageFormat", LocalizeVaultClearScope(clearScope)),
                requiredPhrase,
                _localization.Format("ClearVaultConfirmationInstructionFormat", requiredPhrase),
                _localization.Get("Delete"),
                _localization.Cancel);
            if (!confirmed)
            {
                StatusMessage = _localization.Get("ClearVaultCancelled");
                return;
            }

            await _repository.ClearVaultDataAsync(clearScope);
            DangerZoneConfirmationText = "";
            await LoadAsync();
            StatusMessage = _localization.Format("ClearedVaultDataFormat", LocalizeVaultClearScope(clearScope));
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ClearVaultDataFailedFormat", ex.Message);
        }
        finally
        {
            EndSecurityMaintenance(() => IsClearingVaultData = false);
        }
    }

    [RelayCommand]
    private async Task ChangeMasterPasswordAsync()
    {
        if (!IsUnlocked)
        {
            StatusMessage = _localization.Get("VaultLocked");
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentMasterPassword))
        {
            StatusMessage = _localization.Get("EnterCurrentMasterPassword");
            return;
        }

        if (string.IsNullOrWhiteSpace(NewMasterPassword))
        {
            StatusMessage = _localization.Get("EnterNewMasterPassword");
            return;
        }

        if (!VaultMasterPasswordPolicy.MeetsMinimumLength(NewMasterPassword))
        {
            StatusMessage = _localization.Get("MasterPasswordMinLength");
            return;
        }

        if (!string.Equals(NewMasterPassword, ConfirmNewMasterPassword, StringComparison.Ordinal))
        {
            StatusMessage = _localization.Get("ConfirmationMismatch");
            return;
        }

        var currentPassword = CurrentMasterPassword;
        var newPassword = NewMasterPassword;

        if (!TryBeginSecurityMaintenance(() => IsChangingMasterPassword = true))
        {
            return;
        }

        StatusMessage = _localization.Get("ChangeMasterPasswordInProgress");
        try
        {
            var result = await _masterPasswordMaintenanceService.ChangeMasterPasswordAsync(
                currentPassword,
                newPassword);
            if (!result.Success)
            {
                var message = result.Message.Contains("incorrect", StringComparison.OrdinalIgnoreCase)
                    ? _localization.Get("WrongMasterPassword")
                    : result.Message;
                StatusMessage = _localization.Format("ChangeMasterPasswordFailedFormat", message);
                return;
            }

            CurrentMasterPassword = "";
            NewMasterPassword = "";
            ConfirmNewMasterPassword = "";
            MasterPassword = "";
            ConfirmMasterPassword = "";
            StatusMessage = _localization.Format("MasterPasswordChangedFormat", result.TotalSecretsReencrypted);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ChangeMasterPasswordFailedFormat", ex.Message);
        }
        finally
        {
            EndSecurityMaintenance(() => IsChangingMasterPassword = false);
        }
    }

}
