using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task ResetMasterPasswordWithSecurityQuestionsAsync()
    {
        if (!TryValidateSecurityRecoveryReset(out var recovery, out var answer1, out var answer2, out var newPassword)) return;
        if (!TryBeginSecurityMaintenance(() => IsResettingMasterPassword = true)) return;

        StatusMessage = _localization.Get("ResetMasterPasswordInProgress");
        try
        {
            var answersValid = await Task.Run(() =>
                _securityQuestionService.VerifyAnswer(answer1, recovery.Question1AnswerHash, recovery.Question1AnswerSalt) &&
                _securityQuestionService.VerifyAnswer(answer2, recovery.Question2AnswerHash, recovery.Question2AnswerSalt));
            if (!answersValid)
            {
                StatusMessage = _localization.Get("SecurityQuestionAnswersIncorrect");
                return;
            }

            var result = await _masterPasswordMaintenanceService.ResetMasterPasswordFromUnlockedVaultAsync(newPassword);
            if (!result.Success)
            {
                ReportSettingsFailure(
                    "Master password reset reported a failure",
                    "ResetMasterPasswordFailed",
                    result.Message);
                return;
            }

            ClearRecoveryResetInputs();
            MasterPassword = "";
            ConfirmMasterPassword = "";
            StatusMessage = _localization.Format("ResetMasterPasswordChangedFormat", result.TotalSecretsReencrypted);
        }
        catch (Exception ex)
        {
            ReportSettingsFailure("Master password reset failed", "ResetMasterPasswordFailed", ex);
        }
        finally
        {
            EndSecurityMaintenance(() => IsResettingMasterPassword = false);
        }
    }

    [RelayCommand]
    private async Task SaveSecurityQuestionsAsync()
    {
        if (!SecurityRecoveryEnabled)
        {
            _settingsService.Current.SecurityRecovery.IsEnabled = false;
            QueueSaveSettings();
            RaiseSecurityRecoveryState();
            StatusMessage = _localization.Get("SecurityQuestionsDisabled");
            return;
        }

        if (!TryBuildSecurityQuestionDrafts(out var question1, out var question2)) return;
        if (!TryBeginSecurityMaintenance(() => IsSavingSecurityQuestions = true)) return;
        try
        {
            var setup = await Task.Run(() => _securityQuestionService.CreateSetup(question1, question2));
            _settingsService.Current.SecurityRecovery = setup;
            ApplySecurityRecoverySettings(setup);
            QueueSaveSettings();
            RaiseSecurityRecoveryState();
            StatusMessage = _localization.Get("SecurityQuestionsSaved");
        }
        catch (Exception ex)
        {
            ReportSettingsFailure("Saving security questions failed", "SecurityQuestionsSaveFailed", ex);
        }
        finally
        {
            EndSecurityMaintenance(() => IsSavingSecurityQuestions = false);
        }
    }

    private bool TryBuildSecurityQuestionDrafts(
        out SecurityQuestionDraft question1,
        out SecurityQuestionDraft question2)
    {
        var question1Text = GetSecurityQuestionText(SecurityQuestion1Id, SecurityQuestion1CustomText);
        var question2Text = GetSecurityQuestionText(SecurityQuestion2Id, SecurityQuestion2CustomText);
        question1 = new(SecurityQuestion1Id, question1Text, SecurityQuestion1Answer);
        question2 = new(SecurityQuestion2Id, question2Text, SecurityQuestion2Answer);

        if (string.IsNullOrWhiteSpace(question1Text) || string.IsNullOrWhiteSpace(question2Text))
        {
            StatusMessage = _localization.Get("SecurityQuestionTextRequired");
            return false;
        }

        if (string.IsNullOrWhiteSpace(SecurityQuestion1Answer) || string.IsNullOrWhiteSpace(SecurityQuestion2Answer))
        {
            StatusMessage = _localization.Get("SecurityQuestionAnswersRequired");
            return false;
        }

        if (string.Equals(question1Text.Trim(), question2Text.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = _localization.Get("SecurityQuestionsMustDiffer");
            return false;
        }

        return true;
    }

    private bool TryValidateSecurityRecoveryReset(
        out Monica.Core.Models.SecurityRecoverySettings recovery,
        out string answer1,
        out string answer2,
        out string newPassword)
    {
        recovery = _settingsService.Current.SecurityRecovery;
        answer1 = SecurityRecoveryAnswer1;
        answer2 = SecurityRecoveryAnswer2;
        newPassword = RecoveryNewMasterPassword;
        if (!IsUnlocked) return FailRecoveryReset("VaultLocked");
        if (!recovery.IsEnabled) return FailRecoveryReset("SecurityRecoveryDisabled");
        if (!recovery.HasCompleteSetup) return FailRecoveryReset("SecurityQuestionsNotConfigured");
        if (string.IsNullOrWhiteSpace(answer1) || string.IsNullOrWhiteSpace(answer2)) return FailRecoveryReset("SecurityQuestionAnswersRequired");
        if (string.IsNullOrWhiteSpace(newPassword)) return FailRecoveryReset("EnterNewMasterPassword");
        if (!VaultMasterPasswordPolicy.MeetsMinimumLength(newPassword)) return FailRecoveryReset("MasterPasswordMinLength");
        if (!string.Equals(newPassword, RecoveryConfirmNewMasterPassword, StringComparison.Ordinal)) return FailRecoveryReset("ConfirmationMismatch");
        return true;
    }

    private bool FailRecoveryReset(string messageKey)
    {
        StatusMessage = _localization.Get(messageKey);
        return false;
    }

    private void ClearRecoveryResetInputs()
    {
        SecurityRecoveryAnswer1 = "";
        SecurityRecoveryAnswer2 = "";
        RecoveryNewMasterPassword = "";
        RecoveryConfirmNewMasterPassword = "";
    }

    private void RaiseSecurityRecoveryState()
    {
        OnPropertyChanged(nameof(SecurityRecoveryStatusText));
        OnPropertyChanged(nameof(SecurityRecoveryQuestion1PromptText));
        OnPropertyChanged(nameof(SecurityRecoveryQuestion2PromptText));
        OnPropertyChanged(nameof(CanResetMasterPasswordWithSecurityQuestions));
        OnPropertyChanged(nameof(CanRunResetMasterPassword));
    }
}
