using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data.Services;

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
            SetStatusNotice("GitHubRepositoryOpened");
        }
        catch (Exception ex)
        {
            ReportSettingsFailure("Opening the GitHub repository failed", "GitHubRepositoryOpenFailed", ex);
        }
    }

    [RelayCommand]
    private async Task ClearVaultDataAsync(string? scope)
    {
        if (!IsUnlocked)
        {
            SetStatusMessage("VaultLocked");
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
                SetStatusNotice("ClearVaultCancelled");
                return;
            }

            await _repository.ClearVaultDataAsync(clearScope);
            DangerZoneConfirmationText = "";
            await LoadAsync();
            SetStatusNotice("ClearedVaultDataFormat", LocalizeVaultClearScope(clearScope));
        }
        catch (Exception ex)
        {
            ReportSettingsFailure("Clearing vault data failed", "ClearVaultDataFailed", ex);
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
            SetStatusMessage("VaultLocked");
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentMasterPassword))
        {
            SetStatusMessage("EnterCurrentMasterPassword");
            return;
        }

        if (string.IsNullOrWhiteSpace(NewMasterPassword))
        {
            SetStatusMessage("EnterNewMasterPassword");
            return;
        }

        if (!VaultMasterPasswordPolicy.MeetsMinimumLength(NewMasterPassword))
        {
            SetStatusMessage("MasterPasswordMinLength");
            return;
        }

        if (!string.Equals(NewMasterPassword, ConfirmNewMasterPassword, StringComparison.Ordinal))
        {
            SetStatusFailure("ConfirmationMismatch");
            return;
        }

        var currentPassword = CurrentMasterPassword;
        var newPassword = NewMasterPassword;

        if (!TryBeginSecurityMaintenance(() => IsChangingMasterPassword = true))
        {
            return;
        }

        SetStatusMessage("ChangeMasterPasswordInProgress");
        try
        {
            var result = await GetChangeMasterPasswordUseCase().ExecuteAsync(currentPassword, newPassword);
            if (!result.Success)
            {
                if (result.IsCurrentPasswordIncorrect)
                {
                    SetStatusFailure("WrongMasterPassword");
                    return;
                }

                ReportSettingsFailure(
                    "Master password update reported a failure",
                    "ChangeMasterPasswordFailed",
                    result.FailureMessage ?? "no detail");
                return;
            }

            CurrentMasterPassword = "";
            NewMasterPassword = "";
            ConfirmNewMasterPassword = "";
            MasterPassword = "";
            ConfirmMasterPassword = "";
            SetStatusNotice("MasterPasswordChangedFormat", result.TotalSecretsReencrypted);
        }
        catch (Exception ex)
        {
            ReportSettingsFailure("Master password update failed", "ChangeMasterPasswordFailed", ex);
        }
        finally
        {
            EndSecurityMaintenance(() => IsChangingMasterPassword = false);
        }
    }

}
