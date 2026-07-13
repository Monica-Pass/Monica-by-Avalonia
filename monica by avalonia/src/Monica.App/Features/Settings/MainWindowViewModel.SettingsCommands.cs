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

        var requiredPhrase = _localization.Get("ClearVaultConfirmationPhrase");
        if (!string.Equals(DangerZoneConfirmationText.Trim(), requiredPhrase, StringComparison.Ordinal))
        {
            StatusMessage = _localization.Format("ClearVaultConfirmationFailedFormat", requiredPhrase);
            return;
        }

        var clearScope = scope?.ToLowerInvariant() switch
        {
            "passwords" => VaultClearScope.Passwords,
            "secureitems" or "secure-items" => VaultClearScope.SecureItems,
            _ => VaultClearScope.All
        };

        await _repository.ClearVaultDataAsync(clearScope);
        DangerZoneConfirmationText = "";
        await LoadAsync();
        StatusMessage = _localization.Format("ClearedVaultDataFormat", LocalizeVaultClearScope(clearScope));
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

        if (NewMasterPassword.Length < 8)
        {
            StatusMessage = _localization.Get("MasterPasswordMinLength");
            return;
        }

        if (!string.Equals(NewMasterPassword, ConfirmNewMasterPassword, StringComparison.Ordinal))
        {
            StatusMessage = _localization.Get("ConfirmationMismatch");
            return;
        }

        IsChangingMasterPassword = true;
        StatusMessage = _localization.Get("ChangeMasterPasswordInProgress");
        try
        {
            var result = await _masterPasswordMaintenanceService.ChangeMasterPasswordAsync(
                CurrentMasterPassword,
                NewMasterPassword);
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
            IsChangingMasterPassword = false;
        }
    }

    [RelayCommand]
    private async Task ResetMasterPasswordWithSecurityQuestionsAsync()
    {
        if (!IsUnlocked)
        {
            StatusMessage = _localization.Get("VaultLocked");
            return;
        }

        var recovery = _settingsService.Current.SecurityRecovery;
        if (!recovery.HasCompleteSetup)
        {
            StatusMessage = _localization.Get("SecurityQuestionsNotConfigured");
            return;
        }

        if (string.IsNullOrWhiteSpace(SecurityRecoveryAnswer1) || string.IsNullOrWhiteSpace(SecurityRecoveryAnswer2))
        {
            StatusMessage = _localization.Get("SecurityQuestionAnswersRequired");
            return;
        }

        if (!_securityQuestionService.VerifyAnswer(SecurityRecoveryAnswer1, recovery.Question1AnswerHash, recovery.Question1AnswerSalt) ||
            !_securityQuestionService.VerifyAnswer(SecurityRecoveryAnswer2, recovery.Question2AnswerHash, recovery.Question2AnswerSalt))
        {
            StatusMessage = _localization.Get("SecurityQuestionAnswersIncorrect");
            return;
        }

        if (string.IsNullOrWhiteSpace(RecoveryNewMasterPassword))
        {
            StatusMessage = _localization.Get("EnterNewMasterPassword");
            return;
        }

        if (RecoveryNewMasterPassword.Length < 8)
        {
            StatusMessage = _localization.Get("MasterPasswordMinLength");
            return;
        }

        if (!string.Equals(RecoveryNewMasterPassword, RecoveryConfirmNewMasterPassword, StringComparison.Ordinal))
        {
            StatusMessage = _localization.Get("ConfirmationMismatch");
            return;
        }

        IsResettingMasterPassword = true;
        StatusMessage = _localization.Get("ResetMasterPasswordInProgress");
        try
        {
            var result = await _masterPasswordMaintenanceService.ResetMasterPasswordFromUnlockedVaultAsync(RecoveryNewMasterPassword);
            if (!result.Success)
            {
                StatusMessage = _localization.Format("ResetMasterPasswordFailedFormat", result.Message);
                return;
            }

            SecurityRecoveryAnswer1 = "";
            SecurityRecoveryAnswer2 = "";
            RecoveryNewMasterPassword = "";
            RecoveryConfirmNewMasterPassword = "";
            MasterPassword = "";
            ConfirmMasterPassword = "";
            StatusMessage = _localization.Format("ResetMasterPasswordChangedFormat", result.TotalSecretsReencrypted);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("ResetMasterPasswordFailedFormat", ex.Message);
        }
        finally
        {
            IsResettingMasterPassword = false;
        }
    }

    [RelayCommand]
    private void SaveSecurityQuestions()
    {
        if (!SecurityRecoveryEnabled)
        {
            _settingsService.Current.SecurityRecovery.IsEnabled = false;
            QueueSaveSettings();
            OnPropertyChanged(nameof(SecurityRecoveryStatusText));
            OnPropertyChanged(nameof(CanResetMasterPasswordWithSecurityQuestions));
            OnPropertyChanged(nameof(CanRunResetMasterPassword));
            StatusMessage = _localization.Get("SecurityQuestionsDisabled");
            return;
        }

        try
        {
            var setup = _securityQuestionService.CreateSetup(
                new SecurityQuestionDraft(SecurityQuestion1Id, GetSecurityQuestionText(SecurityQuestion1Id, SecurityQuestion1CustomText), SecurityQuestion1Answer),
                new SecurityQuestionDraft(SecurityQuestion2Id, GetSecurityQuestionText(SecurityQuestion2Id, SecurityQuestion2CustomText), SecurityQuestion2Answer));
            _settingsService.Current.SecurityRecovery = setup;
            ApplySecurityRecoverySettings(setup);
            SecurityQuestion1Answer = "";
            SecurityQuestion2Answer = "";
            QueueSaveSettings();
            OnPropertyChanged(nameof(SecurityRecoveryStatusText));
            OnPropertyChanged(nameof(SecurityRecoveryQuestion1PromptText));
            OnPropertyChanged(nameof(SecurityRecoveryQuestion2PromptText));
            OnPropertyChanged(nameof(CanResetMasterPasswordWithSecurityQuestions));
            OnPropertyChanged(nameof(CanRunResetMasterPassword));
            StatusMessage = _localization.Get("SecurityQuestionsSaved");
        }
        catch (ArgumentException ex)
        {
            StatusMessage = _localization.Format("SecurityQuestionsSaveFailedFormat", ex.Message);
        }
    }
}
